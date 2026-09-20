using System;
using RabbitEars.Dsp;

namespace RabbitEars.Pal;

/// <summary>What the encoder transmits beyond the picture itself. Immutable: swap the whole object to change it.</summary>
public sealed record PalSignalParams
{
    public static PalSignalParams Default { get; } = new();

    /// <summary>Chroma amplitude relative to burst (source saturation).</summary>
    public float ChromaGain { get; init; } = 1f;

    /// <summary>Burst amplitude scale; 0 removes the burst.</summary>
    public float BurstGain { get; init; } = 1f;

    /// <summary>Volts added to the active picture (black level / brightness).</summary>
    public float Pedestal { get; init; }

    /// <summary>Chroma rotation against the burst, degrees.</summary>
    public float ChromaPhaseDegrees { get; init; }

    /// <summary>Whole-signal gain, sync included.</summary>
    public float Level { get; init; } = 1f;

    /// <summary>White noise added to the composite signal, volts rms.</summary>
    public float NoiseVolts { get; init; }

    /// <summary>Amplitude of a delayed positive echo.</summary>
    public float Ghost { get; init; }

    public float GhostMicroseconds { get; init; } = 1.6f;

    /// <summary>50 Hz hum amplitude in volts, rolling slowly against the field rate.</summary>
    public float HumVolts { get; init; }

    public bool IsClean => Level == 1f && NoiseVolts <= 0 && Ghost <= 0 && HumVolts <= 0;
}

/// <summary>Baseband impairments applied per line after encoding: ghost, level, hum, noise (in that order).</summary>
public sealed class PalImpairments
{
    private const double HumHz = 50.2;                                    // 0.2 Hz off the field rate: the bar rolls
    private readonly TableNoise _noise;
    private readonly float[] _previousClean = new float[PalTiming.SamplesPerLine];
    private readonly float[] _clean = new float[PalTiming.SamplesPerLine];
    private bool _havePrevious;

    public PalImpairments(ulong seed) => _noise = TableNoise.CreateGaussian(seed);

    public void Apply(float[] line, long globalLine, PalSignalParams p)
    {
        int n = line.Length;
        if (p.Ghost > 0)
        {
            line.CopyTo(_clean, 0);
            int delay = Math.Clamp(PalTiming.Microseconds(p.GhostMicroseconds), 1, n);
            for (int i = 0; i < n; i++)
            {
                int from = i - delay;
                float echo = from >= 0 ? _clean[from] : (_havePrevious ? _previousClean[from + n] : 0f);
                line[i] += p.Ghost * echo;
            }
            _clean.CopyTo(_previousClean, 0);
        }
        _havePrevious = p.Ghost > 0;
        if (p.IsClean) return;

        if (p.Level != 1f)
            for (int i = 0; i < n; i++) line[i] *= p.Level;
        if (p.HumVolts > 0)
        {
            double t = globalLine * 64e-6;
            float hum = (float)(p.HumVolts * Math.Sin(2.0 * Math.PI * HumHz * t));
            for (int i = 0; i < n; i++) line[i] += hum;
        }
        if (p.NoiseVolts > 0) _noise.AddTo(line, p.NoiseVolts);
    }
}
