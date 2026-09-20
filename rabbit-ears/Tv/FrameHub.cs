using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace RabbitEars.Tv;

/// <summary>One decoded picture as it goes to viewers: a 16-byte header followed by the raw pixels.</summary>
public sealed record FramePacket(byte[] Bytes, int Width, int Height, int Channels, uint Sequence)
{
    /// <summary>u32 width | u32 height | u32 channels (3 = RGB, 1 = grey) | u32 sequence, little-endian.</summary>
    public const int HeaderBytes = 16;
}

/// <summary>Holds the newest decoded frame; any number of viewers wait for the next one. Slow viewers skip frames.</summary>
public sealed class FrameHub
{
    private readonly object _gate = new();
    private readonly Queue<long> _times = new();
    private FramePacket? _latest;
    private uint _sequence;

    public FramePacket? Latest
    {
        get { lock (_gate) return _latest; }
    }

    /// <param name="bytes">Header room (<see cref="FramePacket.HeaderBytes"/>, filled in here) followed by the pixels.</param>
    public void Publish(byte[] bytes, int width, int height, int channels)
    {
        lock (_gate)
        {
            _sequence++;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)width);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)height);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)channels);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), _sequence);
            _latest = new FramePacket(bytes, width, height, channels, _sequence);
            long now = Stopwatch.GetTimestamp();
            _times.Enqueue(now);
            while (_times.Count > 0 && now - _times.Peek() > 2 * Stopwatch.Frequency) _times.Dequeue();
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>The next frame after <paramref name="afterSequence"/>, or null when none arrived in time.</summary>
    public FramePacket? WaitNext(uint afterSequence, int timeoutMilliseconds)
    {
        lock (_gate)
        {
            if (_latest is null || _latest.Sequence == afterSequence) Monitor.Wait(_gate, timeoutMilliseconds);
            return _latest is not null && _latest.Sequence != afterSequence ? _latest : null;
        }
    }

    /// <summary>Frames published per second over the last two seconds.</summary>
    public double FramesPerSecond
    {
        get
        {
            lock (_gate)
            {
                long now = Stopwatch.GetTimestamp();
                while (_times.Count > 0 && now - _times.Peek() > 2 * Stopwatch.Frequency) _times.Dequeue();
                return _times.Count / 2.0;
            }
        }
    }
}
