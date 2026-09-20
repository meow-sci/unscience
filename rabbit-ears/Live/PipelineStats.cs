using System;
using System.Diagnostics;
using System.Threading;

namespace RabbitEars.Live;

/// <summary>Smoothed busy time of one stage per block → how many times faster than real time it could run.</summary>
public sealed class StageTimer
{
    private double _smoothedSeconds;

    public void Add(double seconds)
    {
        double previous = Volatile.Read(ref _smoothedSeconds);
        Volatile.Write(ref _smoothedSeconds, previous == 0 ? seconds : previous + 0.05 * (seconds - previous));
    }

    public double MillisecondsPerBlock => Volatile.Read(ref _smoothedSeconds) * 1000.0;

    /// <summary>Block duration / busy time: above 1 the stage keeps up, with that much to spare.</summary>
    public double RealTimeFactor
    {
        get
        {
            double busy = Volatile.Read(ref _smoothedSeconds);
            return busy > 0 ? LivePipeline.BlockSeconds / busy : 0;
        }
    }
}

/// <summary>What the live pipeline reports about itself. Written by its threads, read by the web server.</summary>
public sealed class PipelineStats
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private long _blocks, _ifSamples, _resyncs;
    private double _lateSeconds, _lostSeconds;
    private long _windowStart = Stopwatch.GetTimestamp(), _windowBlocks;
    private double _clockRate = 1.0;

    public StageTimer Producer { get; } = new();
    public StageTimer Tuner { get; } = new();

    public long Blocks => Interlocked.Read(ref _blocks);
    public long IfSamples => Interlocked.Read(ref _ifSamples);
    public long Resyncs => Interlocked.Read(ref _resyncs);
    public double UptimeSeconds => (double)(Stopwatch.GetTimestamp() - _started) / Stopwatch.Frequency;

    /// <summary>How far behind its schedule the mux clock is right now (0 when it keeps up).</summary>
    public double LateMilliseconds => Volatile.Read(ref _lateSeconds) * 1000.0;

    /// <summary>Signal time given up in clock resyncs: the machine could not keep up for that long in total.</summary>
    public double LostSeconds => Volatile.Read(ref _lostSeconds);

    /// <summary>Signal seconds produced per wall second over the last couple of seconds; 1.00 = real time.</summary>
    public double ClockRate => Volatile.Read(ref _clockRate);

    internal void CountBlock()
    {
        Interlocked.Increment(ref _blocks);
        _windowBlocks++;
        long now = Stopwatch.GetTimestamp();
        double window = (double)(now - _windowStart) / Stopwatch.Frequency;
        if (window < 2.0) return;
        Volatile.Write(ref _clockRate, _windowBlocks * LivePipeline.BlockSeconds / window);
        _windowStart = now;
        _windowBlocks = 0;
    }

    internal void CountIfSamples(int count) => Interlocked.Add(ref _ifSamples, count);

    internal void ReportLateness(double seconds) => Volatile.Write(ref _lateSeconds, seconds);

    internal void ReportResync(double lostSeconds)
    {
        Interlocked.Increment(ref _resyncs);
        Volatile.Write(ref _lostSeconds, Volatile.Read(ref _lostSeconds) + lostSeconds);
        Volatile.Write(ref _lateSeconds, 0);
    }
}
