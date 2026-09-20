using System;

namespace RabbitEars.Dsp;

/// <summary>Kaiser-windowed FIR design helpers. All frequencies are in Hz against an explicit sample rate.</summary>
public static class FirDesign
{
    /// <summary>Zeroth-order modified Bessel function of the first kind (power series).</summary>
    public static double BesselI0(double x)
    {
        double sum = 1.0, term = 1.0, half = x * 0.5;
        for (int k = 1; k < 64; k++)
        {
            term *= (half / k) * (half / k);
            sum += term;
            if (term < sum * 1e-17) break;
        }
        return sum;
    }

    /// <summary>Kaiser beta for a stop-band attenuation in dB (Kaiser's empirical formula).</summary>
    public static double KaiserBeta(double attenuationDb)
    {
        if (attenuationDb > 50.0) return 0.1102 * (attenuationDb - 8.7);
        if (attenuationDb >= 21.0) return 0.5842 * Math.Pow(attenuationDb - 21.0, 0.4) + 0.07886 * (attenuationDb - 21.0);
        return 0.0;
    }

    /// <summary>Stop-band attenuation (dB) a Kaiser design of this length reaches over the given transition width.</summary>
    public static double KaiserAttenuation(int taps, double transitionHz, double sampleRate)
    {
        double deltaOmega = 2.0 * Math.PI * transitionHz / sampleRate;
        return 8.0 + 2.285 * deltaOmega * (taps - 1);
    }

    public static double[] KaiserWindow(int taps, double beta)
    {
        var w = new double[taps];
        double denom = BesselI0(beta);
        double mid = (taps - 1) * 0.5;
        for (int i = 0; i < taps; i++)
        {
            double r = mid == 0 ? 0 : (i - mid) / mid;
            w[i] = BesselI0(beta * Math.Sqrt(Math.Max(0.0, 1.0 - r * r))) / denom;
        }
        return w;
    }

    /// <summary>
    /// Real linear-phase low-pass: pass to <paramref name="passHz"/>, stop from <paramref name="stopHz"/>.
    /// The Kaiser beta follows from the length and transition width; DC gain is normalised to 1.
    /// </summary>
    public static double[] LowPass(int taps, double passHz, double stopHz, double sampleRate)
    {
        if (taps < 2) throw new ArgumentOutOfRangeException(nameof(taps));
        if (!(stopHz > passHz) || passHz < 0) throw new ArgumentException("need 0 <= pass < stop");
        double cutoff = 0.5 * (passHz + stopHz) / sampleRate;            // cycles per sample, the -6 dB point
        double beta = KaiserBeta(KaiserAttenuation(taps, stopHz - passHz, sampleRate));
        double[] window = KaiserWindow(taps, beta);
        var h = new double[taps];
        double mid = (taps - 1) * 0.5, sum = 0.0;
        for (int i = 0; i < taps; i++)
        {
            double t = i - mid;
            double sinc = Math.Abs(t) < 1e-12 ? 2.0 * cutoff : Math.Sin(2.0 * Math.PI * cutoff * t) / (Math.PI * t);
            h[i] = sinc * window[i];
            sum += h[i];
        }
        for (int i = 0; i < taps; i++) h[i] /= sum;
        return h;
    }

    /// <summary>
    /// Shifts a real prototype to a centre frequency: g[i] = h[i]·exp(j·2π·fc·(i − mid)/fs), returned as (re, im).
    /// Modulating about the filter's middle keeps the result linear-phase with a real response at fc.
    /// </summary>
    public static (double[] Re, double[] Im) Modulate(double[] prototype, double centreHz, double sampleRate)
    {
        int n = prototype.Length;
        var re = new double[n];
        var im = new double[n];
        double mid = (n - 1) * 0.5, omega = 2.0 * Math.PI * centreHz / sampleRate;
        for (int i = 0; i < n; i++)
        {
            re[i] = prototype[i] * Math.Cos(omega * (i - mid));
            im[i] = prototype[i] * Math.Sin(omega * (i - mid));
        }
        return (re, im);
    }

    /// <summary>Magnitude response in dB of complex taps at one frequency.</summary>
    public static double ResponseDb(double[] re, double[]? im, double freqHz, double sampleRate)
    {
        double omega = 2.0 * Math.PI * freqHz / sampleRate, sr = 0, si = 0;
        for (int i = 0; i < re.Length; i++)
        {
            double c = Math.Cos(omega * i), s = Math.Sin(omega * i);
            double a = re[i], b = im?[i] ?? 0.0;
            sr += a * c + b * s;                                        // (a + jb)·exp(−jωi)
            si += b * c - a * s;
        }
        return 10.0 * Math.Log10(Math.Max(sr * sr + si * si, 1e-30));
    }

    public static float[] ToFloat(double[] taps, double gain = 1.0)
    {
        var f = new float[taps.Length];
        for (int i = 0; i < f.Length; i++) f[i] = (float)(taps[i] * gain);
        return f;
    }
}
