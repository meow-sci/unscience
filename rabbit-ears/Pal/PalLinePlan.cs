using System;
using System.Collections.Generic;

namespace RabbitEars.Pal;

/// <summary>
/// The picture-independent part of a 625-line frame: sync pulses (line, equalising and broad, with
/// Hann-smoothed monotonic edges) de-duplicated into a handful of line templates, plus the burst envelope.
/// Shared by every encoder.
/// </summary>
public sealed class PalLinePlan
{
    public static PalLinePlan Instance { get; } = new();

    /// <summary>Unique sync line shapes in volts (0 … −0.3).</summary>
    public float[][] SyncTemplates { get; }

    /// <summary>Index into <see cref="SyncTemplates"/> for each 0-based frame line.</summary>
    public int[] TemplateOfLine { get; } = new int[PalTiming.Lines];

    public bool[] HasBurst { get; } = new bool[PalTiming.Lines];

    /// <summary>Burst gate, 0..1 with smoothed edges; zero outside [BurstFirst, BurstLast).</summary>
    public float[] BurstEnvelope { get; } = new float[PalTiming.SamplesPerLine];

    public int BurstFirst { get; }
    public int BurstLast { get; }

    private PalLinePlan()
    {
        const int spl = PalTiming.SamplesPerLine, half = spl / 2;
        var pulses = new List<(int Start, int Width)>[PalTiming.Lines];
        var vertical = new bool[PalTiming.Lines];
        for (int i = 0; i < pulses.Length; i++) pulses[i] = new List<(int, int)> { (0, PalTiming.SyncSamples) };

        void Half(int width, params (int Line, bool Second)[] slots)
        {
            foreach ((int line, bool second) in slots)
            {
                int index = line - 1;
                if (!vertical[index])
                {
                    vertical[index] = true;
                    pulses[index].Clear();
                }
                pulses[index].Add((second ? half : 0, width));
            }
        }

        int broad = PalTiming.BroadSamples, eq = PalTiming.EqualisingSamples;
        Half(broad, (1, false), (1, true), (2, false), (2, true), (3, false));
        Half(eq, (3, true), (4, false), (4, true), (5, false), (5, true));
        Half(eq, (311, false), (311, true), (312, false), (312, true), (313, false));
        Half(broad, (313, true), (314, false), (314, true), (315, false), (315, true));
        Half(eq, (316, false), (316, true), (317, false), (317, true), (318, false));
        pulses[622].Add((half, eq));                                      // line 623: line sync + first pre-equaliser
        Half(eq, (624, false), (624, true), (625, false), (625, true));

        var frame = new double[PalTiming.SamplesPerFrame];
        for (int line = 0; line < PalTiming.Lines; line++)
        {
            foreach ((int start, int width) in pulses[line])
                Array.Fill(frame, 1.0, line * spl + start, width);
            HasBurst[line] = !vertical[line];
        }
        double[] smooth = SmoothCircular(frame, HannKernel(7));

        var unique = new List<float[]>();
        for (int line = 0; line < PalTiming.Lines; line++)
        {
            var shape = new float[spl];
            for (int s = 0; s < spl; s++) shape[s] = (float)(PalTiming.SyncVolts * smooth[line * spl + s]);
            int found = unique.FindIndex(u => u.AsSpan().SequenceEqual(shape));
            if (found < 0)
            {
                unique.Add(shape);
                found = unique.Count - 1;
            }
            TemplateOfLine[line] = found;
        }
        SyncTemplates = unique.ToArray();

        var gate = new double[spl];
        Array.Fill(gate, 1.0, PalTiming.BurstStart, PalTiming.BurstSamples);
        double[] gateSmooth = SmoothCircular(gate, HannKernel(9));
        BurstFirst = spl;
        for (int s = 0; s < spl; s++)
        {
            BurstEnvelope[s] = (float)gateSmooth[s];
            if (gateSmooth[s] <= 0) continue;
            BurstFirst = Math.Min(BurstFirst, s);
            BurstLast = s + 1;
        }
    }

    /// <summary>Inner taps of a Hann window (end zeros dropped), normalised to unit sum.</summary>
    private static double[] HannKernel(int innerTaps)
    {
        var k = new double[innerTaps];
        double sum = 0;
        for (int i = 0; i < innerTaps; i++)
        {
            k[i] = 0.5 - 0.5 * Math.Cos(2.0 * Math.PI * (i + 1) / (innerTaps + 1));
            sum += k[i];
        }
        for (int i = 0; i < innerTaps; i++) k[i] /= sum;
        return k;
    }

    private static double[] SmoothCircular(double[] x, double[] kernel)
    {
        int n = x.Length, mid = kernel.Length / 2;
        var y = new double[n];
        for (int i = 0; i < n; i++)
        {
            double acc = 0;
            for (int j = 0; j < kernel.Length; j++) acc += kernel[j] * x[((i + j - mid) % n + n) % n];
            y[i] = acc;
        }
        return y;
    }
}
