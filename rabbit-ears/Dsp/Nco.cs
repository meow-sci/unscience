using System;

namespace RabbitEars.Dsp;

/// <summary>
/// Numerically controlled oscillator: a 64-bit phase accumulator read through an interpolated sine table.
/// Drift-free, exactly reproducible, and phase-continuous across frequency changes.
/// </summary>
public sealed class Nco
{
    private const int TableBits = 12;
    private const int TableSize = 1 << TableBits;
    private const int FracBits = 64 - TableBits;
    private const double FracScale = 1.0 / (1UL << FracBits);
    private const double TwoPow64 = 18446744073709551616.0;
    private static readonly float[] SinTable = BuildTable();

    private ulong _phase;
    private ulong _step;

    private static float[] BuildTable()
    {
        var t = new float[TableSize + 1];
        for (int i = 0; i <= TableSize; i++) t[i] = (float)Math.Sin(2.0 * Math.PI * i / TableSize);
        return t;
    }

    /// <summary>Phase advance per output step, in cycles (any sign; wraps).</summary>
    public void SetCyclesPerStep(double cycles)
    {
        double frac = cycles - Math.Floor(cycles);
        _step = (ulong)(frac * TwoPow64);
    }

    public double PhaseCycles => _phase / TwoPow64;

    /// <summary>Fills cos/sin of the current phase for each step, then advances.</summary>
    public void Next(Span<float> cos, Span<float> sin)
    {
        ulong phase = _phase, step = _step;
        float[] table = SinTable;
        for (int i = 0; i < cos.Length; i++)
        {
            sin[i] = Lookup(table, phase);
            cos[i] = Lookup(table, phase + (1UL << 62));               // + quarter cycle
            phase += step;
        }
        _phase = phase;
    }

    private static float Lookup(float[] table, ulong phase)
    {
        int index = (int)(phase >> FracBits);
        float frac = (float)((phase & ((1UL << FracBits) - 1)) * FracScale);
        float a = table[index];
        return a + (table[index + 1] - a) * frac;
    }
}
