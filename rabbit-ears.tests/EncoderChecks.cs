using System;
using RabbitEars.Pal;
using RabbitEars.Sources;

namespace RabbitEars.Tests;

/// <summary>PAL encoder: levels, line/frame structure, burst amplitude and unbroken subcarrier phase, block-size invariance.</summary>
public static class EncoderChecks
{
    private const int Spl = PalTiming.SamplesPerLine;

    public static void Run()
    {
        Check.Section("PAL encoder");
        Check.That(Spl == 1280 && PalTiming.Lines == 625 && PalTiming.SamplesPerFrame == 800_000, "1280 samples/line, 625 lines, 800000 samples/frame");

        var white = new SolidSource(255, 255, 255);
        float[] cvbs = Encode(new PalEncoder(white), 3 * PalTiming.SamplesPerFrame);
        Check.That(white.FramesServed == 3, $"one frame pulled per 800000 samples (pulled {white.FramesServed})");

        float min = float.MaxValue, max = float.MinValue;
        foreach (float sample in cvbs)
        {
            min = Math.Min(min, sample);
            max = Math.Max(max, sample);
        }
        Check.Near(min, -0.3, 1e-4, "sync tip volts");
        Check.Near(max, 0.7, 1e-4, "white volts");
        Check.Near(Mean(cvbs, 99 * Spl + 20, 60), -0.3, 1e-5, "line 100 sync tip");
        Check.Near(Mean(cvbs, 99 * Spl + 170, 30), 0.0, 1e-5, "line 100 back porch is blanking");
        Check.Near(Mean(cvbs, 99 * Spl + 400, 600), 0.7, 1e-4, "line 100 active picture is white");
        Check.Near(Mean(cvbs, 9 * Spl + 400, 600), 0.0, 1e-5, "line 10 (vertical blanking) carries no picture");
        Check.Near(Mean(cvbs, 100, 400), -0.3, 1e-5, "line 1 first half is a broad pulse");
        Check.Near(Mean(cvbs, 3 * Spl + 10, 30), -0.3, 1e-5, "line 4 starts with an equalising pulse");
        Check.Near(Mean(cvbs, 3 * Spl + 60, 500), 0.0, 1e-5, "... which is short");
        Check.Near(Mean(cvbs, 3 * Spl + 650, 30), -0.3, 1e-5, "line 4 has a second pulse at the half line");
        Check.Near(Mean(cvbs, 22 * Spl + 300, 300), 0.0, 1e-5, "line 23 first half is blanked");
        Check.Near(Mean(cvbs, 22 * Spl + 900, 200), 0.7, 1e-4, "line 23 second half is picture");

        // Burst: amplitude 0.15 V, and its phase against one reference running unbroken from sample 0 must be
        // +135° / −135° relative to the −U axis convention, alternating with the global line count, across frame boundaries.
        double worstAmplitude = 0, worstPhase = 0;
        int linesChecked = 0;
        for (long line = 0; line < 3 * PalTiming.Lines; line++)
        {
            if (!PalLinePlan.Instance.HasBurst[line % PalTiming.Lines]) continue;
            (double amplitude, double degrees) = BurstPhasor(cvbs, line * Spl);
            double expected = (line & 1) == 0 ? 45.0 : 135.0;             // −sin + cos = √2·cos(θ + 45°); −sin − cos = √2·cos(θ + 135°)
            worstAmplitude = Math.Max(worstAmplitude, Math.Abs(amplitude - 0.15));
            worstPhase = Math.Max(worstPhase, Math.Abs(WrapDegrees(degrees - expected)));
            linesChecked++;
        }
        Check.That(linesChecked > 1700, $"burst present on {linesChecked} lines of 3 frames");
        Check.AtMost(worstAmplitude, 0.004, "worst burst amplitude error (V) vs 0.15");
        Check.AtMost(worstPhase, 1.5, "worst burst phase error (deg) vs one continuous 4.43361875 MHz reference");

        // Chroma amplitude and phase on a saturated red field: U = 0.493(B−Y), V = 0.877(R−Y).
        float[] red = Encode(new PalEncoder(new SolidSource(255, 0, 0)), 200 * Spl);
        (double chromaAmp, double chromaDeg) = Phasor(red, 100L * Spl + 500, 451);
        double u = 0.493 * (0 - 0.299), v = 0.877 * (1 - 0.299);
        Check.Near(chromaAmp, 0.7 * Math.Sqrt(u * u + v * v), 0.004, "red chroma amplitude (V)");
        double expectedDeg = Math.Atan2(-u, v) * 180 / Math.PI;           // line 100 (even): U·sin + V·cos = A·cos(θ − atan2(U, V))
        Check.Near(WrapDegrees(chromaDeg - expectedDeg), 0, 1.0, "red chroma phase on a +V line (deg)");
        Check.Near(Mean(red, 100 * Spl + 400, 451 * 1), 0.7 * 0.299, 0.002, "red luma (V)");

        BlockInvariance();
        Resampling();
    }

    private static void BlockInvariance()
    {
        int total = PalTiming.SamplesPerFrame + 50_000;
        float[] reference = Encode(new PalEncoder(new CardSource("bars", 1, "T", "T", TimeSpan.Zero)), total);
        foreach (int block in new[] { 1, 777, 1280, 4096, 160_000 })
        {
            if (block == 1 && total > 200_000)
            {
                float[] shortRef = reference.AsSpan(0, 20_000).ToArray();
                Check.That(shortRef.AsSpan().SequenceEqual(Encode(new PalEncoder(new CardSource("bars", 1, "T", "T", TimeSpan.Zero)), 20_000, 1)),
                    "encoder block size 1 is bit-identical");
                continue;
            }
            float[] other = Encode(new PalEncoder(new CardSource("bars", 1, "T", "T", TimeSpan.Zero)), total, block);
            Check.That(reference.AsSpan().SequenceEqual(other), $"encoder block size {block} is bit-identical");
        }
    }

    private static void Resampling()
    {
        float[] native = Encode(new PalEncoder(new SolidSource(40, 160, 220)), 120 * Spl);
        float[] scaled = Encode(new PalEncoder(new SolidSource(40, 160, 220, 320, 240)), 120 * Spl);
        double worst = 0;
        for (int i = 0; i < native.Length; i++) worst = Math.Max(worst, Math.Abs(native[i] - scaled[i]));
        Check.AtMost(worst, 1e-5, "a 320x240 frame resamples to the same signal as a native 1040x576 one (flat colour)");
    }

    public static float[] Encode(PalEncoder encoder, int samples, int block = 160_000)
    {
        var all = new float[samples];
        for (int done = 0; done < samples; done += block)
            encoder.Read(all.AsSpan(done, Math.Min(block, samples - done)));
        return all;
    }

    private static double Mean(float[] x, long start, int count)
    {
        double sum = 0;
        for (int i = 0; i < count; i++) sum += x[start + i];
        return sum / count;
    }

    /// <summary>Amplitude and phase (deg, cosine reference locked to global sample 0) of the burst on the line at <paramref name="lineStart"/>.</summary>
    private static (double Amplitude, double Degrees) BurstPhasor(float[] x, long lineStart) =>
        Phasor(x, lineStart + PalTiming.BurstStart + 6, 9 * 4 - 3);

    /// <summary>Single-bin DFT at the subcarrier over a window (DC removed; Hann-weighted so partial cycles do not leak).</summary>
    private static (double Amplitude, double Degrees) Phasor(float[] x, long start, int count)
    {
        double mean = Mean(x, start, count), re = 0, im = 0, weight = 0;
        double omega = 2.0 * Math.PI * PalTiming.SubcarrierHz / PalTiming.SampleRate;
        for (int i = 0; i < count; i++)
        {
            double w = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * (i + 0.5) / count);
            double angle = omega * ((start + i) % (4L * PalTiming.SamplesPerFrame));
            re += w * (x[start + i] - mean) * Math.Cos(angle);
            im -= w * (x[start + i] - mean) * Math.Sin(angle);
            weight += w;
        }
        return (2.0 * Math.Sqrt(re * re + im * im) / weight, Math.Atan2(im, re) * 180.0 / Math.PI);
    }

    private static double WrapDegrees(double d)
    {
        d %= 360.0;
        if (d > 180) d -= 360;
        if (d < -180) d += 360;
        return d;
    }
}
