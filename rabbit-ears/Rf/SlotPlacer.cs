using System;
using System.Runtime.Intrinsics;
using RabbitEars.Dsp;

namespace RabbitEars.Rf;

/// <summary>
/// Interpolate ×K and place a channel on its carrier. The complex baseband (carrier at 0 Hz) is rotated by
/// f_k at the channel rate — a short periodic table, since f_k/Fc is rational — and a polyphase band-pass
/// centred on the slot (f_k + 2.75 MHz) picks the one image that belongs there. Output is real.
/// </summary>
public sealed unsafe class SlotPlacer
{
    public const int TapsPerPhase = 8;
    public const double PassHz = 4.3e6;                                   // about the slot centre

    private readonly PolyphaseUpconverter _upconverter;
    private readonly float[] _cos;                                        // one period + 3 wrap-around entries
    private readonly float[] _sin;
    private readonly int _period;
    private int _phase;
    private float[] _rotRe = new float[4096];
    private float[] _rotIm = new float[4096];

    public SlotPlacer(ChannelPlan plan, int slot)
    {
        double carrier = plan.CarrierHz(slot);
        double[] prototype = FirDesign.LowPass(plan.K * TapsPerPhase, PassHz, ChannelPlan.ChannelRate - PassHz, plan.WidebandRate);
        _upconverter = new PolyphaseUpconverter(plan.K, prototype, plan.SlotCentreHz(slot), plan.WidebandRate);

        long num = (long)Math.Round(carrier), den = (long)ChannelPlan.ChannelRate;
        _period = (int)(den / Gcd(num, den));
        _cos = new float[_period + 3];
        _sin = new float[_period + 3];
        for (int i = 0; i < _cos.Length; i++)
        {
            double angle = 2.0 * Math.PI * ((num * (i % _period)) % den) / den;
            _cos[i] = (float)Math.Cos(angle);
            _sin[i] = (float)Math.Sin(angle);
        }
    }

    private static long Gcd(long a, long b) => b == 0 ? Math.Abs(a) : Gcd(b, a % b);

    /// <summary>Carrier amplitude at the output for a unit-magnitude input (the sync tip).</summary>
    public void SetGain(double gain) => _upconverter.SetGain(gain);

    /// <summary>Produces K × input.Length wideband samples.</summary>
    public void Process(ReadOnlySpan<float> inRe, ReadOnlySpan<float> inIm, Span<float> wideband)
    {
        int n = inRe.Length;
        if (_rotRe.Length < n)
        {
            _rotRe = new float[n];
            _rotIm = new float[n];
        }
        fixed (float* ur = inRe, ui = inIm, wr = _rotRe, wi = _rotIm, ct = _cos, st = _sin)
        {
            int i = 0, phase = _phase;
            for (; i + 4 <= n; i += 4)
            {
                Vector128<float> c = Vector128.Load(ct + phase), s = Vector128.Load(st + phase);
                Vector128<float> re = Vector128.Load(ur + i), im = Vector128.Load(ui + i);
                (re * c - im * s).Store(wr + i);
                (re * s + im * c).Store(wi + i);
                phase = (phase + 4) % _period;
            }
            for (; i < n; i++)
            {
                wr[i] = ur[i] * ct[phase] - ui[i] * st[phase];
                wi[i] = ur[i] * st[phase] + ui[i] * ct[phase];
                phase = (phase + 1) % _period;
            }
            _phase = phase;
        }
        _upconverter.Process(_rotRe.AsSpan(0, n), _rotIm.AsSpan(0, n), wideband);
    }
}
