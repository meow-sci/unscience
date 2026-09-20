using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using RabbitEars.Net;
using RabbitEars.Pal;
using RabbitEars.Rf;
using RabbitEars.Sources;

namespace RabbitEars.Live;

/// <summary>
/// The live multiplexer: in-process channels (cards, ffmpeg) plus network sender slots. The mux itself is
/// single-threaded per call, so channel changes are queued here and carried out between two Process calls.
/// </summary>
public sealed class MuxProducer : IWidebandProducer, IDisposable
{
    private readonly WidebandMux _mux;
    private readonly ConcurrentQueue<Action> _betweenBlocks = new();
    private readonly object _gate = new();
    private readonly List<LiveChannel> _channels = new();

    /// <summary>Headroom is the whole plan, so a sender joining later never changes anybody's level.</summary>
    public MuxProducer(ChannelPlan plan, int maxThreads = 0)
    {
        Plan = plan;
        _mux = new WidebandMux(plan, plan.MaxChannels, maxThreads);
    }

    public ChannelPlan Plan { get; }
    public double ReferenceAmplitude => _mux.ReferenceAmplitude;

    public IReadOnlyList<LiveChannel> Channels
    {
        get { lock (_gate) return _channels.OrderBy(c => c.Slot).ToArray(); }
    }

    public bool IsSlotTaken(int slot)
    {
        lock (_gate) return _channels.Any(c => c.Slot == slot);
    }

    /// <summary>Adds a channel encoded in this process. Safe at any time: the mux picks it up at the next block.</summary>
    public LiveChannel AddLocal(int slot, string spec, IFrameSource source, double levelDb = 0)
    {
        var encoder = new PalEncoder(source, noiseSeed: (ulong)(slot + 1));
        var channel = new LiveChannel(slot, source.Name, spec, Plan.CarrierHz(slot)) { Encoder = encoder, FrameSource = source, LevelDb = levelDb };
        Register(channel, encoder);
        return channel;
    }

    /// <summary>Adds the channel of a newly claimed sender slot (called from the sender server's thread).</summary>
    public LiveChannel AddSender(SenderSlot sender)
    {
        var channel = new LiveChannel(sender.Slot, sender.Name, "sender", Plan.CarrierHz(sender.Slot)) { Sender = sender };
        Register(channel, sender);
        return channel;
    }

    private void Register(LiveChannel channel, ICvbsSource cvbs)
    {
        lock (_gate)
        {
            if (_channels.Any(c => c.Slot == channel.Slot)) throw new InvalidOperationException($"slot {channel.Slot} is already in use");
            _channels.Add(channel);
        }
        _betweenBlocks.Enqueue(() => channel.Transmitter = _mux.AddChannel(channel.Slot, cvbs, channel.Name, channel.LevelDb));
    }

    public bool Remove(int slot)
    {
        LiveChannel? channel;
        lock (_gate)
        {
            channel = _channels.FirstOrDefault(c => c.Slot == slot);
            if (channel is null) return false;
            _channels.Remove(channel);
        }
        _betweenBlocks.Enqueue(() =>
        {
            _mux.RemoveChannel(slot);
            channel.FrameSource?.Dispose();
        });
        return true;
    }

    public bool SetLevel(int slot, double levelDb)
    {
        LiveChannel? channel = Find(slot);
        if (channel is null) return false;
        channel.LevelDb = levelDb;
        _betweenBlocks.Enqueue(() =>
        {
            if (channel.Transmitter is not null) channel.Transmitter.LevelDb = levelDb;
        });
        return true;
    }

    public LiveChannel? Find(int slot)
    {
        lock (_gate) return _channels.FirstOrDefault(c => c.Slot == slot);
    }

    public void Produce(float[] block, int lowRateSamples)
    {
        while (_betweenBlocks.TryDequeue(out Action? change)) change();
        _mux.Process(block, lowRateSamples);
    }

    public void Dispose()
    {
        foreach (LiveChannel channel in Channels) channel.FrameSource?.Dispose();
    }
}
