using System;
using System.Collections.Generic;
using RabbitEars.Dsp;
using RabbitEars.Pal;
using RabbitEars.Rf;

namespace RabbitEars.Tests;

/// <summary>
/// Tuner behaviour: for every slot of a full mux, a simple synchronous-free envelope detector on the 40 MS/s IF
/// must find the transmitted line structure, levels and the right channel's picture; plus tone/retune/noise checks.
/// </summary>
public static class TunerChecks
{
    private const int IfLine = PalTiming.SamplesPerLine * 2;              // 2560 samples per line at 40 MS/s

    public static void Run()
    {
        foreach (int k in new[] { 4, 6, 8 }) EverySlot(k);
        ToneAndRetune();
        NoiseLevel();
    }

    private static void EverySlot(int k)
    {
        Check.Section($"tuner recovers every slot, K={k}");
        var plan = new ChannelPlan(k);
        int n = plan.MaxChannels, lines = 64;
        var mux = new WidebandMux(plan, n);
        for (int slot = 0; slot < n; slot++)
            mux.AddChannel(slot, new PalEncoder(new SolidSource(Grey(slot, n), Grey(slot, n), Grey(slot, n))), $"grey{slot}");
        var wide = new float[lines * PalTiming.SamplesPerLine * k];
        mux.Process(wide, lines * PalTiming.SamplesPerLine);
        float peak = 0;
        foreach (float v in wide) peak = Math.Max(peak, Math.Abs(v));
        Check.AtMost(peak, 1.0, $"{n} full carriers stay inside ±1 (peak)");

        for (int slot = 0; slot < n; slot++)
        {
            var tuner = new Tuner(plan);
            tuner.TuneToSlot(slot);
            tuner.SetReferenceCarrier(mux.ReferenceAmplitude, 0.25);
            var ifOut = new float[tuner.MaxOutputFor(wide.Length)];
            int count = tuner.Process(wide, ifOut);
            float[] envelope = Envelope(ifOut.AsSpan(0, count));

            double tip = Mean(envelope, 9 * IfLine + 160, 100);          // the chain delays the signal by ~110 IF samples
            List<int> edges = RisingEdges(envelope, 0.88 * tip, 0.82 * tip, 7 * IfLine, 60 * IfLine);
            int good = 0;
            for (int i = 1; i < edges.Count; i++)
                if (Math.Abs(edges[i] - edges[i - 1] - IfLine) <= 2) good++;
            Check.That(edges.Count >= 50 && good == edges.Count - 1, $"slot {slot}: {edges.Count} line syncs, {good} periods of 2560 ± 2 samples");
            Check.Near(tip, 0.25, 0.01, $"slot {slot}: sync-tip envelope at the requested output level");
            Check.Near(Mean(envelope, 9 * IfLine + 1400, 1000) / tip, 0.76, 0.015, $"slot {slot}: blanking / tip");
            double grey = Grey(slot, n) / 255.0;
            Check.Near(Mean(envelope, 40 * IfLine + 1000, 1200) / tip, 0.76 - 0.8 * 0.7 * grey, 0.015, $"slot {slot}: picture level is this slot's grey ({grey:0.00})");
        }
    }

    private static byte Grey(int slot, int n) => (byte)(255 * (slot + 1) / (n + 1));

    private static void ToneAndRetune()
    {
        Check.Section("tuner tone, gain and retune");
        var plan = new ChannelPlan(6);
        const double toneHz = 22e6;
        const int size = 1 << 14;
        var wide = new float[size * 3 * 8];
        for (int i = 0; i < wide.Length; i++) wide[i] = 0.2f * (float)Math.Cos(2 * Math.PI * toneHz / plan.WidebandRate * i);
        var tuner = new Tuner(plan) { FrequencyHz = toneHz };
        var ifOut = new float[tuner.MaxOutputFor(wide.Length)];
        int half = wide.Length / 2;
        int first = tuner.Process(wide.AsSpan(0, half), ifOut);
        tuner.FrequencyHz = toneHz + 1e6;                                   // the tone should drop to 7 MHz
        int second = tuner.Process(wide.AsSpan(half), ifOut.AsSpan(first));

        double binHz = ChannelPlan.IfRate / size;
        double[] before = Fft.PowerSpectrumDb(ifOut.AsSpan(1000, size * 2), size);
        double[] after = Fft.PowerSpectrumDb(ifOut.AsSpan(first + 1000, size * 2), size);
        Check.Near(ArgMax(before) * binHz, 8e6, binHz, "tuned tone lands at 8 MHz");
        Check.Near(before[ArgMax(before)], 20 * Math.Log10(0.2), 0.1, "unity gain through the tuner (dB)");
        Check.Near(ArgMax(after) * binHz, 7e6, binHz, "after retuning +1 MHz it lands at 7 MHz");

        float jump = 0;
        for (int i = first - 50; i < first + 50; i++) jump = Math.Max(jump, Math.Abs(ifOut[i + 1] - ifOut[i]));
        Check.AtMost(jump, 0.2 * 2 * Math.Sin(Math.PI * 8e6 / 40e6) * 1.15, "no discontinuity at the retune (max sample step)");
        Check.That(first + second == wide.Length / plan.Decimation, "output count = input / D");
    }

    private static void NoiseLevel()
    {
        Check.Section("receiver noise");
        var tuner = new Tuner(new ChannelPlan(6));
        tuner.SetNoiseDbBelowCarrier(20, 0.25);
        var silence = new float[300_000];
        var ifOut = new float[tuner.MaxOutputFor(silence.Length)];
        int count = tuner.Process(silence, ifOut);
        double sum = 0, sum4 = 0;
        for (int i = 0; i < count; i++)
        {
            sum += ifOut[i] * (double)ifOut[i];
            sum4 += Math.Pow(ifOut[i], 4);
        }
        double rms = Math.Sqrt(sum / count);
        Check.Near(rms, 0.025, 0.001, "noise rms for 20 dB below a 0.25 carrier");
        Check.Near(sum4 / count / Math.Pow(rms, 4), 3.0, 0.15, "noise kurtosis is Gaussian");
    }

    /// <summary>Quadrature envelope detector: mix the 8 MHz carrier to 0 Hz, 20-sample moving average (nulls every 2 MHz), magnitude.</summary>
    public static float[] Envelope(ReadOnlySpan<float> ifSamples)
    {
        const int window = 20;
        int n = ifSamples.Length;
        var i = new double[n + 1];
        var q = new double[n + 1];
        for (int s = 0; s < n; s++)
        {
            double angle = 2 * Math.PI * (s % 5) / 5.0;                   // 8 MHz at 40 MS/s
            i[s + 1] = i[s] + ifSamples[s] * Math.Cos(angle);
            q[s + 1] = q[s] - ifSamples[s] * Math.Sin(angle);
        }
        var envelope = new float[n];
        for (int s = window; s <= n; s++)
        {
            double re = (i[s] - i[s - window]) / window, im = (q[s] - q[s - window]) / window;
            envelope[s - window / 2 - 1] = (float)(2 * Math.Sqrt(re * re + im * im));
        }
        return envelope;
    }

    /// <summary>Sync-separator style slicer with hysteresis: an edge counts once the signal has been below <paramref name="rearm"/>.</summary>
    private static List<int> RisingEdges(float[] x, double threshold, double rearm, int from, int to)
    {
        var edges = new List<int>();
        bool armed = false;
        for (int s = Math.Max(1, from); s < Math.Min(to, x.Length); s++)
        {
            if (x[s] < rearm) armed = true;
            if (!armed || x[s] < threshold) continue;
            edges.Add(s);
            armed = false;
        }
        return edges;
    }

    private static double Mean(float[] x, int start, int count)
    {
        double sum = 0;
        for (int s = 0; s < count; s++) sum += x[start + s];
        return sum / count;
    }

    private static int ArgMax(double[] x)
    {
        int best = 1;
        for (int b = 1; b < x.Length; b++)
            if (x[b] > x[best]) best = b;
        return best;
    }
}
