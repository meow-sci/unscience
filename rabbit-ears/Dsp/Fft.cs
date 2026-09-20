using System;

namespace RabbitEars.Dsp;

/// <summary>In-place radix-2 complex FFT (double precision) and a Welch power-spectrum helper.</summary>
public static class Fft
{
    public static void Forward(double[] re, double[] im)
    {
        int n = re.Length;
        if (n != im.Length || (n & (n - 1)) != 0) throw new ArgumentException("length must be a power of two");
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double angle = -2.0 * Math.PI / len;
            double wr = Math.Cos(angle), wi = Math.Sin(angle);
            int half = len >> 1;
            for (int start = 0; start < n; start += len)
            {
                double cr = 1.0, ci = 0.0;
                for (int k = 0; k < half; k++)
                {
                    int a = start + k, b = a + half;
                    double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr;
                    im[b] = im[a] - xi;
                    re[a] += xr;
                    im[a] += xi;
                    double t = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = t;
                }
            }
        }
    }

    /// <summary>
    /// Welch-averaged power spectrum of a real signal, Blackman-Harris window, bins 0..size/2.
    /// Scaled so a sine of amplitude A reads 20·log10(A) dB at its bin.
    /// </summary>
    public static double[] PowerSpectrumDb(ReadOnlySpan<float> signal, int size)
    {
        if (signal.Length < size) throw new ArgumentException("signal shorter than one FFT");
        var window = new double[size];
        double windowSum = 0;
        for (int i = 0; i < size; i++)
        {
            double x = 2.0 * Math.PI * i / size;
            window[i] = 0.35875 - 0.48829 * Math.Cos(x) + 0.14128 * Math.Cos(2 * x) - 0.01168 * Math.Cos(3 * x);
            windowSum += window[i];
        }
        var power = new double[size / 2 + 1];
        var re = new double[size];
        var im = new double[size];
        int segments = 0;
        for (int start = 0; start + size <= signal.Length; start += size / 2, segments++)
        {
            for (int i = 0; i < size; i++)
            {
                re[i] = signal[start + i] * window[i];
                im[i] = 0;
            }
            Forward(re, im);
            for (int k = 0; k < power.Length; k++) power[k] += re[k] * re[k] + im[k] * im[k];
        }
        double scale = 4.0 / (windowSum * windowSum * segments);          // amplitude-normalised, single-sided
        for (int k = 0; k < power.Length; k++) power[k] = 10.0 * Math.Log10(Math.Max(power[k] * scale, 1e-30));
        return power;
    }
}
