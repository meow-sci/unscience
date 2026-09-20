using System;
using System.Diagnostics;
using System.Threading;
using RabbitEars.Live;
using RabbitEars.Net;
using RabbitEars.Pal;
using RabbitEars.Rf;
using RabbitEars.Sources;
using RabbitEars.Tv;

namespace RabbitEars.Tests;

/// <summary>Live pipeline smoke test without PALindrome: paced mux → tuner → a counting sink, then a clean stop.</summary>
public static class LiveChecks
{
    private sealed class CountingSink : IIfSink
    {
        public long Bytes;
        public double SumOfSquares;
        public int Blocks;

        public void Feed(ReadOnlySpan<byte> s16)
        {
            Bytes += s16.Length;
            Blocks++;
            var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, short>(s16);
            for (int i = 0; i < words.Length; i += 64) SumOfSquares += (double)words[i] * words[i] / (32768.0 * 32768.0) * 64;
        }
    }

    public static void Run()
    {
        Check.Section("live pipeline: paced mux → tuner for 0.5 s, bounded queues, clean shutdown");
        var plan = new ChannelPlan(4);
        using var mux = new MuxProducer(plan);
        mux.AddLocal(0, "card:bars", new CardSource("bars", 1, "ONE", "TEST"));
        mux.AddLocal(1, "card:grid", new CardSource("grid", 2, "TWO", "TEST"), levelDb: -6);
        var tuner = new Tuner(plan);
        tuner.SetReferenceCarrier(mux.ReferenceAmplitude);
        tuner.TuneToSlot(1);
        var sink = new CountingSink();
        var spectrum = new SpectrumMonitor();
        var pipeline = new LivePipeline(mux, tuner, sink, spectrum);

        int threadsBefore = Process.GetCurrentProcess().Threads.Count, worstQueue = 0;
        var clock = Stopwatch.StartNew();
        pipeline.Start();
        LiveChannel? late = null;
        while (clock.ElapsedMilliseconds < 500)
        {
            worstQueue = Math.Max(worstQueue, pipeline.QueuedBlocks);
            if (late is null && clock.ElapsedMilliseconds > 150) late = mux.AddSender(new SenderSlot(3));   // a sender joins mid-run
            Thread.Sleep(2);
        }
        double ran = clock.Elapsed.TotalSeconds;
        var stopClock = Stopwatch.StartNew();
        pipeline.Dispose();
        double stopSeconds = stopClock.Elapsed.TotalSeconds;

        PipelineStats stats = pipeline.Stats;
        double produced = stats.Blocks * LivePipeline.BlockSeconds;
        Check.That(pipeline.Failure is null, "no pipeline thread failed");
        Check.That(produced <= ran + 0.06, $"never runs ahead of the wall clock: {produced:0.000} s of signal in {ran:0.000} s");
        Check.That(produced >= 0.5 * ran, $"keeps producing (at least half real time even on a busy machine): {produced:0.000} s");
        Check.That(stats.Resyncs == 0 || produced < 0.95 * ran, $"lateness is reported, not hidden: resyncs {stats.Resyncs}, late {stats.LateMilliseconds:0.0} ms");
        Check.That(worstQueue <= 6, $"producer → tuner queue stays bounded: worst {worstQueue} blocks");
        Check.That(sink.Bytes == stats.IfSamples * 2 && Math.Abs(stats.IfSamples - stats.Blocks * 160_000L * 2) <= 2 * 320_000,
            $"every block reached the sink as 40 MS/s s16: {stats.IfSamples} IF samples for {stats.Blocks} blocks");
        double rms = Math.Sqrt(sink.SumOfSquares / Math.Max(1, stats.IfSamples));
        Check.That(rms > 0.05 && rms < 0.3, $"IF carries the tuned channel at the expected level: rms {rms:0.000} of full scale");
        Check.That(late?.Transmitter is not null && mux.Channels.Count == 3, "a channel added while running was picked up between two blocks");
        Check.That(spectrum.GetDb().Length == SpectrumMonitor.FftSize / 2 + 1, "spectrum snapshot available");
        Check.That(stats.Producer.RealTimeFactor > 0 && stats.Tuner.RealTimeFactor > 0, $"real-time factors: mux {stats.Producer.RealTimeFactor:0.0}x, tuner {stats.Tuner.RealTimeFactor:0.0}x");
        Check.That(stopSeconds < 2.5, $"shutdown in {stopSeconds:0.00} s");
        Thread.Sleep(100);
        Check.That(Process.GetCurrentProcess().Threads.Count <= threadsBefore + Environment.ProcessorCount + 4, "pipeline threads ended");
    }
}
