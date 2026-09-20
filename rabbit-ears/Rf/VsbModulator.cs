using System;
using RabbitEars.Dsp;

namespace RabbitEars.Rf;

/// <summary>
/// Negative AM + vestigial-sideband shaping at the channel rate. CVBS volts become the carrier envelope
/// e = 0.76 − 0.8·v (sync tip 1.0, blanking 0.76, white 0.20, floor 0.05), then a complex-tap FIR keeps
/// −0.75 … +5.5 MHz about the carrier, which sits at 0 Hz in the complex output.
/// </summary>
public sealed class VsbModulator
{
    public const double BlankingEnvelope = 0.76;
    public const double EnvelopePerVolt = -0.8;
    public const float MinEnvelope = 0.05f;
    public const float MaxEnvelope = 1.05f;

    public const int DefaultTaps = 96;
    public const double LowerPassHz = -0.75e6;
    public const double UpperPassHz = 5.5e6;
    public const double TransitionHz = 0.62e6;

    private readonly ComplexFir _filter;
    private float[] _envelope = new float[4096];

    public VsbModulator(int taps = DefaultTaps)
    {
        (double[] re, double[] im) = DesignTaps(taps);
        _filter = new ComplexFir(re, im);
    }

    public int Taps => _filter.Taps;

    /// <summary>The VSB filter: a Kaiser low-pass shifted to the middle of the kept band, unity gain at the carrier.</summary>
    public static (double[] Re, double[] Im) DesignTaps(int taps = DefaultTaps)
    {
        double centre = (LowerPassHz + UpperPassHz) / 2, halfWidth = (UpperPassHz - LowerPassHz) / 2;
        double[] prototype = FirDesign.LowPass(taps, halfWidth, halfWidth + TransitionHz, ChannelPlan.ChannelRate);
        (double[] re, double[] im) = FirDesign.Modulate(prototype, centre, ChannelPlan.ChannelRate);
        double atCarrier = 0;
        for (int i = 0; i < taps; i++) atCarrier += re[i];               // the imaginary parts cancel by symmetry
        for (int i = 0; i < taps; i++)
        {
            re[i] /= atCarrier;
            im[i] /= atCarrier;
        }
        return (re, im);
    }

    /// <summary>CVBS volts in, complex baseband (carrier at 0 Hz, sync-tip magnitude 1.0) out, all at 20 MS/s.</summary>
    public void Process(ReadOnlySpan<float> cvbs, Span<float> outRe, Span<float> outIm)
    {
        if (_envelope.Length < cvbs.Length) _envelope = new float[cvbs.Length];
        Span<float> envelope = _envelope.AsSpan(0, cvbs.Length);
        Kernels.ScaleOffset(cvbs, (float)EnvelopePerVolt, (float)BlankingEnvelope, envelope);
        Kernels.Clamp(envelope, MinEnvelope, MaxEnvelope);
        _filter.Process(envelope, outRe, outIm);
    }
}
