using System;

namespace RabbitEars.Dsp;

/// <summary>
/// Streaming FIR with complex taps over a real input (two real convolutions sharing one window sweep).
/// State carries across calls; output is bit-identical for any block partitioning.
/// </summary>
public sealed unsafe class ComplexFir
{
    private readonly float[] _tapsRe;      // correlation order (reversed)
    private readonly float[] _tapsIm;
    private readonly int _taps;
    private float[] _work;

    public ComplexFir(double[] tapsRe, double[] tapsIm)
    {
        if (tapsRe.Length != tapsIm.Length) throw new ArgumentException("tap arrays differ in length");
        _taps = tapsRe.Length;
        _tapsRe = new float[_taps];
        _tapsIm = new float[_taps];
        for (int i = 0; i < _taps; i++)
        {
            _tapsRe[i] = (float)tapsRe[_taps - 1 - i];
            _tapsIm[i] = (float)tapsIm[_taps - 1 - i];
        }
        _work = new float[_taps - 1 + 4096];
    }

    public int Taps => _taps;

    public void Process(ReadOnlySpan<float> input, Span<float> outRe, Span<float> outIm)
    {
        int n = input.Length, hist = _taps - 1;
        if (outRe.Length < n || outIm.Length < n) throw new ArgumentException("output too short");
        if (_work.Length < hist + n) Array.Resize(ref _work, hist + n);
        input.CopyTo(_work.AsSpan(hist));
        fixed (float* x = _work, tr = _tapsRe, ti = _tapsIm, pr = outRe, pi = outIm)
            Kernels.FirPair(x, tr, ti, _taps, pr, pi, n);
        _work.AsSpan(n, hist).CopyTo(_work);
    }
}
