using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RabbitEars.Dsp;
using RabbitEars.Pal;

namespace RabbitEars.Rf;

/// <summary>One transmitter: CVBS source → VSB modulator → slot placer. Owned and driven by the mux.</summary>
public sealed class MuxChannel
{
    private const int SubBlock = 2048;                                    // low-rate samples per pass: stays in L1/L2
    private readonly VsbModulator _modulator = new();
    private readonly SlotPlacer _placer;
    private readonly float[] _cvbs = new float[SubBlock];
    private readonly float[] _re = new float[SubBlock];
    private readonly float[] _im = new float[SubBlock];
    private readonly int _k;
    private double _levelDb;
    private double _appliedGain = double.NaN;

    internal MuxChannel(ChannelPlan plan, int slot, ICvbsSource source, string name, double levelDb)
    {
        _placer = new SlotPlacer(plan, slot);
        _k = plan.K;
        Slot = slot;
        Source = source;
        Name = name;
        CarrierHz = plan.CarrierHz(slot);
        _levelDb = levelDb;
    }

    public int Slot { get; }
    public string Name { get; }
    public double CarrierHz { get; }
    public ICvbsSource Source { get; }
    internal float[] Buffer { get; set; } = Array.Empty<float>();

    /// <summary>Transmit level relative to the mux reference carrier. Safe to set from any thread; applied per block.</summary>
    public double LevelDb
    {
        get => _levelDb;
        set => _levelDb = value;
    }

    internal void Produce(int lowRateSamples, Span<float> wideband, double referenceAmplitude)
    {
        double gain = referenceAmplitude * Math.Pow(10.0, _levelDb / 20.0);
        if (gain != _appliedGain)
        {
            _placer.SetGain(gain);
            _appliedGain = gain;
        }
        for (int done = 0; done < lowRateSamples; done += SubBlock)
        {
            int n = Math.Min(SubBlock, lowRateSamples - done);
            Source.Read(_cvbs.AsSpan(0, n));
            _modulator.Process(_cvbs.AsSpan(0, n), _re, _im);
            _placer.Process(_re.AsSpan(0, n), _im.AsSpan(0, n), wideband.Slice(done * _k, n * _k));
        }
    }
}

/// <summary>
/// Frequency-division multiplexer: N channels → one real wideband stream at K × 20 MS/s. Channels are
/// generated in parallel (one task each), then summed. Reference carrier amplitude is 1/headroom so that
/// many full carriers cannot clip ±1. Process is not re-entrant; channel add/remove happens between calls.
/// </summary>
public sealed class WidebandMux
{
    private const int MixChunk = 8192;
    private readonly List<MuxChannel> _channels = new();
    private readonly ParallelOptions _parallel;

    /// <param name="headroomChannels">Carriers the sum must hold without clipping (0 = the plan's maximum).</param>
    /// <param name="maxThreads">Worker threads for channel generation and mixing (0 = all cores).</param>
    public WidebandMux(ChannelPlan plan, int headroomChannels = 0, int maxThreads = 0)
    {
        Plan = plan;
        HeadroomChannels = headroomChannels > 0 ? headroomChannels : plan.MaxChannels;
        _parallel = new ParallelOptions { MaxDegreeOfParallelism = maxThreads > 0 ? maxThreads : Environment.ProcessorCount };
    }

    public ChannelPlan Plan { get; }
    public int HeadroomChannels { get; }

    /// <summary>Sync-tip amplitude of a 0 dB channel in the wideband signal.</summary>
    public double ReferenceAmplitude => 1.0 / HeadroomChannels;

    public IReadOnlyList<MuxChannel> Channels => _channels;

    public MuxChannel AddChannel(int slot, ICvbsSource source, string name, double levelDb = 0)
    {
        if (_channels.Any(c => c.Slot == slot)) throw new InvalidOperationException($"slot {slot} is already in use");
        var channel = new MuxChannel(Plan, slot, source, name, levelDb);
        _channels.Add(channel);
        return channel;
    }

    public bool RemoveChannel(int slot) => _channels.RemoveAll(c => c.Slot == slot) > 0;

    /// <summary>
    /// Advances every channel by <paramref name="lowRateSamples"/> (at 20 MS/s) and writes K × that many
    /// wideband samples to <paramref name="output"/>. Any block size; results do not depend on it.
    /// </summary>
    public void Process(float[] output, int lowRateSamples)
    {
        int count = lowRateSamples * Plan.K;
        if (output.Length < count) throw new ArgumentException("output too short");
        MuxChannel[] channels = _channels.ToArray();
        if (channels.Length == 0)
        {
            Array.Clear(output, 0, count);
            return;
        }
        for (int i = 1; i < channels.Length; i++)
            if (channels[i].Buffer.Length < count) channels[i].Buffer = new float[count];

        double reference = ReferenceAmplitude;
        Parallel.For(0, channels.Length, _parallel, i =>
            channels[i].Produce(lowRateSamples, (i == 0 ? output : channels[i].Buffer).AsSpan(0, count), reference));

        if (channels.Length == 1) return;
        int chunks = (count + MixChunk - 1) / MixChunk;
        Parallel.For(0, chunks, _parallel, chunk =>
        {
            int start = chunk * MixChunk, length = Math.Min(MixChunk, count - start);
            Span<float> acc = output.AsSpan(start, length);
            for (int i = 1; i < channels.Length; i++) Kernels.Accumulate(channels[i].Buffer.AsSpan(start, length), acc);
        });
    }
}
