using System;
using System.Threading;
using RabbitEars.Dsp;

namespace RabbitEars.Rf;

/// <summary>
/// Software tuner: real wideband in, real 40 MS/s IF out with the tuned vision carrier at 8 MHz.
/// out[i] = Re{ e^{−jΘ} · Σ_j g[j]·w[D·i − j] }, g[j] = 2·h[j]·e^{j(θ+φ)j}: the LO is folded into one-sided
/// complex taps so the filter runs at the output rate, and Θ (the LO phase) runs on unbroken through retunes.
/// h is deliberately gentle (±4.5 MHz pass, ±14.5 MHz stop about the slot centre): adjacent channels stay in
/// for the television's own IF filter to reject. Receiver noise and the aerial attenuator act here.
/// Process runs on one thread; the tuning/level properties may be set from any other.
/// </summary>
public sealed unsafe class Tuner
{
    public const double PassHz = 4.5e6;
    public const double StopHz = 14.5e6;
    public const double DefaultOutputCarrier = 0.25;                      // of full scale: room for neighbours + noise

    private readonly ChannelPlan _plan;
    private readonly int _decimation;
    private readonly int _taps;
    private readonly double[] _prototype;
    private readonly float[] _tapsRe;                                     // correlation order
    private readonly float[] _tapsIm;
    private readonly Nco _lo = new();
    private readonly TableNoise _noise;
    private float[] _work;
    private float[] _re = new float[4096], _im = new float[4096], _cos = new float[4096], _sin = new float[4096];
    private int _skip;                                                    // inputs to pass before the next output
    private double _frequencyHz;
    private double _gain = 1.0, _attenuationDb, _noiseRms;
    private int _dirty = 1;

    public Tuner(ChannelPlan plan, ulong noiseSeed = 7)
    {
        _plan = plan;
        _decimation = plan.Decimation;
        _taps = 6 * plan.K;
        _prototype = FirDesign.LowPass(_taps, PassHz, StopHz, plan.WidebandRate);
        _tapsRe = new float[_taps];
        _tapsIm = new float[_taps];
        _work = new float[_taps - 1 + 4096];
        _noise = TableNoise.CreateGaussian(noiseSeed);
        _frequencyHz = plan.CarrierHz(0);
    }

    public int Taps => _taps;
    public int Decimation => _decimation;

    /// <summary>The wideband frequency that lands on the 8 MHz IF carrier. Continuous; applies from the next block.</summary>
    public double FrequencyHz
    {
        get => Volatile.Read(ref _frequencyHz);
        set { Volatile.Write(ref _frequencyHz, value); Interlocked.Exchange(ref _dirty, 1); }
    }

    /// <summary>Linear gain from wideband amplitude to IF amplitude (before noise).</summary>
    public double Gain
    {
        get => Volatile.Read(ref _gain);
        set { Volatile.Write(ref _gain, value); Interlocked.Exchange(ref _dirty, 1); }
    }

    /// <summary>The "aerial" attenuator: signal only, the receiver noise stays put.</summary>
    public double AttenuationDb
    {
        get => Volatile.Read(ref _attenuationDb);
        set { Volatile.Write(ref _attenuationDb, value); Interlocked.Exchange(ref _dirty, 1); }
    }

    /// <summary>Receiver noise added to the IF, rms in output units over the full 20 MHz IF bandwidth; 0 = none.</summary>
    public double NoiseRms
    {
        get => Volatile.Read(ref _noiseRms);
        set => Volatile.Write(ref _noiseRms, Math.Max(0.0, value));
    }

    public void TuneToSlot(int slot) => FrequencyHz = _plan.CarrierHz(slot);

    /// <summary>Sets <see cref="Gain"/> so a 0 dB carrier of the given wideband amplitude comes out at <paramref name="outputCarrier"/>.</summary>
    public void SetReferenceCarrier(double widebandAmplitude, double outputCarrier = DefaultOutputCarrier) =>
        Gain = outputCarrier / widebandAmplitude;

    /// <summary>Noise level as dB below a reference carrier of <paramref name="outputCarrier"/> amplitude (sync tip).</summary>
    public void SetNoiseDbBelowCarrier(double? db, double outputCarrier = DefaultOutputCarrier) =>
        NoiseRms = db is null ? 0.0 : outputCarrier * Math.Pow(10.0, -db.Value / 20.0);

    public int MaxOutputFor(int inputCount) => inputCount / _decimation + 1;

    /// <summary>Consumes any number of wideband samples; returns how many IF samples were written.</summary>
    public int Process(ReadOnlySpan<float> wideband, Span<float> ifOut)
    {
        if (Interlocked.Exchange(ref _dirty, 0) != 0) Retune();
        int n = wideband.Length, hist = _taps - 1, d = _decimation;
        int count = n > _skip ? (n - _skip + d - 1) / d : 0;
        if (ifOut.Length < count) throw new ArgumentException("IF output too short");
        if (_work.Length < hist + n) Array.Resize(ref _work, hist + n);
        wideband.CopyTo(_work.AsSpan(hist));
        EnsureScratch(count);

        fixed (float* x = _work, tr = _tapsRe, ti = _tapsIm, re = _re, im = _im)
        {
            float* p = x + _skip;
            for (int i = 0; i < count; i++, p += d) Kernels.DotPair(p, tr, ti, _taps, out re[i], out im[i]);
        }
        _lo.Next(_cos.AsSpan(0, count), _sin.AsSpan(0, count));
        for (int i = 0; i < count; i++) ifOut[i] = _re[i] * _cos[i] + _im[i] * _sin[i];

        double noise = NoiseRms;
        if (noise > 0) _noise.AddTo(ifOut.Slice(0, count), (float)noise);

        _skip = _skip + count * d - n;
        _work.AsSpan(n, hist).CopyTo(_work);
        return count;
    }

    private void Retune()
    {
        double f = FrequencyHz, rate = _plan.WidebandRate;
        double shiftHz = f - ChannelPlan.IfCarrierHz;                                     // θ
        double centreHz = shiftHz + ChannelPlan.IfCarrierHz + ChannelPlan.SlotCentreOffsetHz;   // θ + φ
        double omega = 2.0 * Math.PI * centreHz / rate;
        double scale = 2.0 * Gain * Math.Pow(10.0, -AttenuationDb / 20.0);
        for (int j = 0; j < _taps; j++)
        {
            int at = _taps - 1 - j;
            _tapsRe[at] = (float)(scale * _prototype[j] * Math.Cos(omega * j));
            _tapsIm[at] = (float)(scale * _prototype[j] * Math.Sin(omega * j));
        }
        _lo.SetCyclesPerStep(shiftHz / ChannelPlan.IfRate);                               // θ·D per output sample
    }

    private void EnsureScratch(int count)
    {
        if (_re.Length >= count) return;
        _re = new float[count];
        _im = new float[count];
        _cos = new float[count];
        _sin = new float[count];
    }
}
