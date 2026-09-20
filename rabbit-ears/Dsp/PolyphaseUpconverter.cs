using System;

namespace RabbitEars.Dsp;

/// <summary>
/// ×K polyphase interpolator that lands a complex low-rate signal on a real carrier in one step:
/// y[K·n + p] = Re Σ_t w[n − t] · g_p[t], where g is a real low-pass prototype shifted to the target
/// centre frequency (so no per-sample LO runs at the high rate). Streaming and block-size invariant.
/// </summary>
public sealed unsafe class PolyphaseUpconverter
{
    private readonly int _factor;
    private readonly int _tapsPerPhase;
    private readonly double[] _protoRe;
    private readonly double[] _protoIm;
    private readonly float[] _tapsRe;       // [phase][tapsPerPhase], correlation order
    private readonly float[] _tapsImNeg;    // negated imaginary part: Re{w·g} = wr·gr − wi·gi
    private float[] _histRe;
    private float[] _histIm;

    /// <param name="factor">Interpolation factor K.</param>
    /// <param name="prototype">Real low-pass at the high rate, length K·T with T a multiple of 4, unity DC gain.</param>
    /// <param name="centreHz">Where the low-rate signal's 0 Hz lands at the high rate.</param>
    public PolyphaseUpconverter(int factor, double[] prototype, double centreHz, double highRate)
    {
        if (prototype.Length % factor != 0 || (prototype.Length / factor) % 4 != 0)
            throw new ArgumentException("prototype length must be K·T with T a multiple of 4");
        _factor = factor;
        _tapsPerPhase = prototype.Length / factor;
        (_protoRe, _protoIm) = FirDesign.Modulate(prototype, centreHz, highRate);
        _tapsRe = new float[prototype.Length];
        _tapsImNeg = new float[prototype.Length];
        _histRe = new float[_tapsPerPhase - 1 + 4096];
        _histIm = new float[_tapsPerPhase - 1 + 4096];
        SetGain(1.0);
    }

    public int Factor => _factor;
    public int TapsPerPhase => _tapsPerPhase;

    /// <summary>Output amplitude for a unit-magnitude input. Takes effect from the next block.</summary>
    public void SetGain(double gain)
    {
        int t = _tapsPerPhase;
        double scale = gain * _factor;                                   // zero-stuffing loses 1/K
        for (int p = 0; p < _factor; p++)
            for (int i = 0; i < t; i++)
            {
                int proto = p + _factor * (t - 1 - i);
                _tapsRe[p * t + i] = (float)(_protoRe[proto] * scale);
                _tapsImNeg[p * t + i] = (float)(-_protoIm[proto] * scale);
            }
    }

    /// <summary>Produces factor × input.Length real samples.</summary>
    public void Process(ReadOnlySpan<float> inRe, ReadOnlySpan<float> inIm, Span<float> output)
    {
        int n = inRe.Length, hist = _tapsPerPhase - 1, k = _factor, t = _tapsPerPhase;
        if (inIm.Length != n || output.Length < n * k) throw new ArgumentException("bad block sizes");
        if (_histRe.Length < hist + n)
        {
            Array.Resize(ref _histRe, hist + n);
            Array.Resize(ref _histIm, hist + n);
        }
        inRe.CopyTo(_histRe.AsSpan(hist));
        inIm.CopyTo(_histIm.AsSpan(hist));
        fixed (float* wr = _histRe, wi = _histIm, gr = _tapsRe, gi = _tapsImNeg, y = output)
        {
            for (int i = 0; i < n; i++)
            {
                float* o = y + i * k;
                for (int p = 0; p < k; p++)
                    o[p] = Kernels.DotDual(wr + i, gr + p * t, wi + i, gi + p * t, t);
            }
        }
        _histRe.AsSpan(n, hist).CopyTo(_histRe);
        _histIm.AsSpan(n, hist).CopyTo(_histIm);
    }
}
