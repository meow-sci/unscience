using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RabbitEars.Pal;
using RabbitEars.Rf;

namespace RabbitEars.Net;

/// <summary>
/// TCP listener for network senders. Each accepted sender claims one slot of the band plan and streams CVBS into
/// that slot's <see cref="SenderSlot"/> FIFO, which the mux consumes at its own clock. The server paces nothing.
/// A slot outlives its connection (it falls back to "no programme"), and a later sender may claim it again.
/// </summary>
public sealed class SenderServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly ChannelPlan _plan;
    private readonly Func<int, bool> _slotTakenLocally;
    private readonly Action<SenderSlot> _onSlotCreated;
    private readonly object _gate = new();
    private readonly Dictionary<int, SenderSlot> _slots = new();
    private readonly List<TcpClient> _clients = new();
    private volatile bool _stopping;

    /// <param name="slotTakenLocally">True for slots that in-process channels occupy (never given to senders).</param>
    /// <param name="onSlotCreated">Called once per slot, the first time it is claimed: add it to the mux.</param>
    public SenderServer(IPAddress address, int port, ChannelPlan plan, Func<int, bool> slotTakenLocally, Action<SenderSlot> onSlotCreated)
    {
        _plan = plan;
        _slotTakenLocally = slotTakenLocally;
        _onSlotCreated = onSlotCreated;
        _listener = new TcpListener(address, port);
        _listener.Start();
        new Thread(AcceptLoop) { IsBackground = true, Name = "sender-accept" }.Start();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public IReadOnlyList<SenderSlot> Slots
    {
        get { lock (_gate) return _slots.Values.OrderBy(s => s.Slot).ToArray(); }
    }

    public SenderSlot? FindSlot(int slot)
    {
        lock (_gate) return _slots.TryGetValue(slot, out SenderSlot? found) ? found : null;
    }

    private void AcceptLoop()
    {
        while (!_stopping)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            lock (_gate) _clients.Add(client);
            new Thread(() => Serve(client)) { IsBackground = true, Name = "sender-connection" }.Start();
        }
    }

    private void Serve(TcpClient client)
    {
        SenderSlot? slot = null;
        string remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        try
        {
            client.NoDelay = true;
            client.ReceiveBufferSize = 1 << 20;
            NetworkStream stream = client.GetStream();
            stream.ReadTimeout = 5000;                                    // a sender that goes silent is dropped
            SenderReply reply = Admit(stream, out slot);
            stream.Write(SenderProtocol.EncodeReply(reply));
            if (slot is null)
            {
                Console.WriteLine($"rabbit-ears: sender {remote} refused: {SenderProtocol.Describe(reply.Status)}");
                return;
            }
            Console.WriteLine($"rabbit-ears: sender '{slot.Name}' ({remote}, {slot.Format.ToString().ToLowerInvariant()}) is on slot {slot.Slot}");
            Receive(stream, slot);
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or InvalidDataException)
        {
            if (!_stopping && slot is not null) Console.WriteLine($"rabbit-ears: sender on slot {slot.Slot} disconnected ({e.Message})");
        }
        finally
        {
            if (slot is not null) slot.Connected = false;
            lock (_gate) _clients.Remove(client);
            client.Dispose();
        }
    }

    private SenderReply Admit(Stream stream, out SenderSlot? slot)
    {
        slot = null;
        SenderHello hello;
        try
        {
            hello = SenderProtocol.ReadHello(stream);
        }
        catch (InvalidDataException)
        {
            return new SenderReply(SenderStatus.BadHandshake, -1);
        }
        if (hello.SampleRate != PalTiming.SampleRate) return new SenderReply(SenderStatus.UnsupportedFormat, -1);

        bool created = false;
        lock (_gate)
        {
            int wanted = hello.RequestedSlot;
            if (wanted == SenderProtocol.AnySlot)
            {
                wanted = FirstFreeSlot();
                if (wanted < 0) return new SenderReply(SenderStatus.NoFreeSlot, -1);
            }
            else if (wanted < 0 || wanted >= _plan.MaxChannels)
            {
                return new SenderReply(SenderStatus.SlotOutOfRange, -1);
            }
            else if (!IsFree(wanted))
            {
                return new SenderReply(SenderStatus.SlotInUse, -1);
            }
            if (!_slots.TryGetValue(wanted, out slot))
            {
                _slots[wanted] = slot = new SenderSlot(wanted);
                created = true;
            }
            slot.Name = hello.Name.Length > 0 ? hello.Name : $"sender {wanted + 1}";
            slot.Format = hello.Format;
            slot.Connected = true;
        }
        if (created) _onSlotCreated(slot);
        return new SenderReply(SenderStatus.Ok, slot.Slot);
    }

    private bool IsFree(int slot) =>
        !_slotTakenLocally(slot) && !(_slots.TryGetValue(slot, out SenderSlot? existing) && existing.Connected);

    private int FirstFreeSlot()
    {
        for (int slot = 0; slot < _plan.MaxChannels; slot++)
            if (IsFree(slot)) return slot;
        return -1;
    }

    private static void Receive(Stream stream, SenderSlot slot)
    {
        var header = new byte[SenderProtocol.BlockHeaderBytes];
        byte[] wire = Array.Empty<byte>();
        short[] samples = Array.Empty<short>();
        int size = CvbsStreamCodec.BytesPerSample(slot.Format);
        while (true)
        {
            SenderProtocol.ReadExactly(stream, header);
            SenderBlockHeader block = SenderProtocol.ParseBlockHeader(header);
            if (wire.Length < block.SampleCount * size) wire = new byte[block.SampleCount * size];
            if (samples.Length < block.SampleCount) samples = new short[block.SampleCount];
            SenderProtocol.ReadExactly(stream, wire.AsSpan(0, block.SampleCount * size));
            CvbsStreamCodec.DecodeToS16(wire, slot.Format, samples.AsSpan(0, block.SampleCount));
            if (!slot.Write(samples.AsSpan(0, block.SampleCount), block.FirstSampleIndex)) return;
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _listener.Stop();
        lock (_gate)
        {
            foreach (SenderSlot slot in _slots.Values) slot.Close();
            foreach (TcpClient client in _clients) client.Close();
        }
    }
}
