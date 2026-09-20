using System;
using System.Diagnostics;
using System.Threading;
using RabbitEars.Net;
using RabbitEars.Rf;
using RabbitEars.Tv;

namespace RabbitEars.Live;

/// <summary>
/// Everything one running `live` or `play` command owns: producer → pipeline → tuner → television → frame hub,
/// plus the receiver settings the web UI changes. Dispose stops the threads and kills the child processes.
/// </summary>
public sealed class LiveSession : IDisposable
{
    public const double DefaultNoiseDb = 38.0;
    private readonly object _cpuGate = new();
    private long _cpuSampledAt;
    private TimeSpan _cpuProcessTime;
    private double _decoderCpu, _processCpu;
    private double? _noiseDb = DefaultNoiseDb;
    private int _viewers;

    public LiveSession(string mode, IWidebandProducer producer, string palindromeBinary, SenderServer? senders = null)
    {
        Mode = mode;
        Producer = producer;
        Senders = senders;
        Tuner = new Tuner(producer.Plan);
        Tuner.SetReferenceCarrier(producer.ReferenceAmplitude);
        Tuner.SetNoiseDbBelowCarrier(_noiseDb);
        int firstSlot = producer.Channels.Count > 0 ? producer.Channels[0].Slot : 0;
        Tuner.TuneToSlot(firstSlot);
        Television = new Television(palindromeBinary, Hub);
        Pipeline = new LivePipeline(producer, Tuner, Television, Spectrum);
    }

    public string Mode { get; }
    public IWidebandProducer Producer { get; }
    public MuxProducer? Mux => Producer as MuxProducer;
    public SenderServer? Senders { get; }
    public ChannelPlan Plan => Producer.Plan;
    public Tuner Tuner { get; }
    public FrameHub Hub { get; } = new();
    public SpectrumMonitor Spectrum { get; } = new();
    public Television Television { get; }
    public LivePipeline Pipeline { get; }

    /// <summary>Viewers connected to the video socket (kept by the web server).</summary>
    public int Viewers => Volatile.Read(ref _viewers);

    public void ViewerChanged(int delta) => Interlocked.Add(ref _viewers, delta);

    /// <summary>Receiver noise in dB below a 0 dB channel's sync-tip carrier; null = a noiseless receiver.</summary>
    public double? NoiseDb
    {
        get => _noiseDb;
        set
        {
            _noiseDb = value is { } db ? Math.Clamp(db, 0, 80) : null;
            Tuner.SetNoiseDbBelowCarrier(_noiseDb);
        }
    }

    public void Start()
    {
        Television.Launch(seamless: false);
        Pipeline.Start();
    }

    /// <summary>Tunes anywhere from 0 Hz to the wideband Nyquist frequency.</summary>
    public void Tune(double frequencyHz) => Tuner.FrequencyHz = Math.Clamp(frequencyHz, 0, Plan.WidebandRate / 2);

    /// <summary>(decoder processes, this process) CPU in percent of one core; sampled at most once a second.</summary>
    public (double Decoder, double Process) CpuPercent()
    {
        lock (_cpuGate)
        {
            long now = Stopwatch.GetTimestamp();
            double elapsed = (double)(now - _cpuSampledAt) / Stopwatch.Frequency;
            if (elapsed < 1.0) return (_decoderCpu, _processCpu);
            using var self = Process.GetCurrentProcess();
            TimeSpan total = self.TotalProcessorTime;
            if (_cpuSampledAt != 0) _processCpu = (total - _cpuProcessTime).TotalSeconds / elapsed * 100.0;
            _cpuProcessTime = total;
            _cpuSampledAt = now;
            _decoderCpu = ProcessCpu.Percent(Television.DecoderPids);
            return (_decoderCpu, _processCpu);
        }
    }

    public void Dispose()
    {
        Pipeline.Dispose();
        Television.Dispose();
        Senders?.Dispose();
        (Producer as IDisposable)?.Dispose();
    }
}
