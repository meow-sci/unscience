using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using RabbitEars.Pal;
using RabbitEars.Rf;
using RabbitEars.Tv;

namespace RabbitEars.Live;

/// <summary>
/// The real-time chain: producer thread (the mux clock: one block every 8 ms of wall time) → bounded queue →
/// tuner thread → IF sink (the television). Buffers come from a fixed pool, so a slow stage holds the one before
/// it back instead of growing memory; when the producer cannot keep its schedule that is counted and reported
/// (lateness, and a clock resync with the lost time once it exceeds <see cref="ResyncSeconds"/>), never hidden.
/// </summary>
public sealed class LivePipeline : IDisposable
{
    public const int BlockLines = 125;                                    // 8 ms: retunes and knob changes land within a block
    public const int BlockLowRateSamples = BlockLines * PalTiming.SamplesPerLine;
    public const double BlockSeconds = BlockLowRateSamples / ChannelPlan.ChannelRate;
    public const double ResyncSeconds = 0.5;
    private const int QueueBlocks = 6;

    private readonly IWidebandProducer _producer;
    private readonly Tuner _tuner;
    private readonly IIfSink _sink;
    private readonly SpectrumMonitor? _spectrum;
    private readonly BlockingCollection<float[]> _free = new();
    private readonly BlockingCollection<float[]> _ready = new(QueueBlocks);
    private readonly CancellationTokenSource _stop = new();
    private readonly int _blockSamples;
    private Thread? _producerThread, _tunerThread;

    public LivePipeline(IWidebandProducer producer, Tuner tuner, IIfSink sink, SpectrumMonitor? spectrum = null)
    {
        _producer = producer;
        _tuner = tuner;
        _sink = sink;
        _spectrum = spectrum;
        _blockSamples = BlockLowRateSamples * producer.Plan.K;
        for (int i = 0; i < QueueBlocks + 2; i++) _free.Add(new float[_blockSamples]);
    }

    public PipelineStats Stats { get; } = new();

    /// <summary>Blocks waiting between the producer and the tuner.</summary>
    public int QueuedBlocks => _ready.Count;

    /// <summary>Set when a pipeline thread died of an unexpected error.</summary>
    public Exception? Failure { get; private set; }

    public void Start()
    {
        _producerThread = new Thread(() => Guard(ProduceLoop)) { IsBackground = true, Name = "mux-clock", Priority = ThreadPriority.AboveNormal };
        _tunerThread = new Thread(() => Guard(TuneLoop)) { IsBackground = true, Name = "tuner" };
        _producerThread.Start();
        _tunerThread.Start();
    }

    private void Guard(Action loop)
    {
        try
        {
            loop();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Failure = e;
            Console.WriteLine($"rabbit-ears: live pipeline stopped: {e.Message}");
            _stop.Cancel();
        }
    }

    private void ProduceLoop()
    {
        CancellationToken token = _stop.Token;
        long origin = Stopwatch.GetTimestamp(), blocks = 0;
        while (!token.IsCancellationRequested)
        {
            float[] block = _free.Take(token);
            long before = Stopwatch.GetTimestamp();
            _producer.Produce(block, BlockLowRateSamples);
            Stats.Producer.Add(Seconds(Stopwatch.GetTimestamp() - before));
            _ready.Add(block, token);
            blocks++;

            double ahead = blocks * BlockSeconds - Seconds(Stopwatch.GetTimestamp() - origin);
            if (ahead > 0)
            {
                Stats.ReportLateness(0);
                Thread.Sleep(TimeSpan.FromSeconds(ahead));
            }
            else if (-ahead > ResyncSeconds)
            {
                Stats.ReportResync(-ahead);
                origin = Stopwatch.GetTimestamp() - (long)(blocks * BlockSeconds * Stopwatch.Frequency);
            }
            else
            {
                Stats.ReportLateness(-ahead);
            }
            Stats.CountBlock();
        }
    }

    private void TuneLoop()
    {
        CancellationToken token = _stop.Token;
        var ifFloat = new float[_tuner.MaxOutputFor(_blockSamples)];
        var ifWords = new short[ifFloat.Length];
        while (!token.IsCancellationRequested)
        {
            float[] block = _ready.Take(token);
            long before = Stopwatch.GetTimestamp();
            _spectrum?.Offer(block);
            int count = _tuner.Process(block, ifFloat);
            SampleCodec.ToS16(ifFloat.AsSpan(0, count), ifWords);
            Stats.Tuner.Add(Seconds(Stopwatch.GetTimestamp() - before));
            _free.Add(block, token);
            _sink.Feed(MemoryMarshal.AsBytes(ifWords.AsSpan(0, count)));
            Stats.CountIfSamples(count);
        }
    }

    private static double Seconds(long ticks) => (double)ticks / Stopwatch.Frequency;

    public void Dispose()
    {
        _stop.Cancel();
        _producerThread?.Join(2000);
        _tunerThread?.Join(2000);
    }
}
