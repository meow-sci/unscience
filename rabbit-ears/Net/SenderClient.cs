using System;
using System.IO;
using System.Net.Sockets;

namespace RabbitEars.Net;

/// <summary>The sending side of the sender contract: connect, claim a slot, then stream CVBS blocks.</summary>
public sealed class SenderClient : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly CvbsFormat _format;
    private byte[] _wire = Array.Empty<byte>();
    private long _nextIndex;

    /// <summary>Connects and performs the handshake; throws <see cref="IOException"/> when the server refuses.</summary>
    public SenderClient(string host, int port, SenderHello hello)
    {
        _format = hello.Format;
        _client = new TcpClient { NoDelay = true, SendBufferSize = 1 << 20 };
        try
        {
            _client.Connect(host, port);
            _stream = _client.GetStream();
            _stream.Write(SenderProtocol.EncodeHello(hello));
            SenderReply reply = SenderProtocol.ReadReply(_stream);
            if (reply.Status != SenderStatus.Ok) throw new IOException($"sender refused: {SenderProtocol.Describe(reply.Status)}");
            AssignedSlot = reply.AssignedSlot;
        }
        catch
        {
            _client.Dispose();
            throw;
        }
    }

    public int AssignedSlot { get; }

    /// <summary>Samples sent so far (the index of the next block's first sample).</summary>
    public long SamplesSent => _nextIndex;

    /// <summary>Sends one block of composite video in volts. Blocks while the server's FIFO is full.</summary>
    public void Send(ReadOnlySpan<float> volts)
    {
        int payload = volts.Length * CvbsStreamCodec.BytesPerSample(_format);
        int total = SenderProtocol.BlockHeaderBytes + payload;
        if (_wire.Length < total) _wire = new byte[total];
        SenderProtocol.WriteBlockHeader(_wire, new SenderBlockHeader(volts.Length, _nextIndex));
        CvbsStreamCodec.Encode(volts, _format, _wire.AsSpan(SenderProtocol.BlockHeaderBytes, payload));
        _stream.Write(_wire, 0, total);
        _nextIndex += volts.Length;
    }

    public void Dispose() => _client.Dispose();
}
