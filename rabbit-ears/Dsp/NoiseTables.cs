using System;
using System.Runtime.Intrinsics;

namespace RabbitEars.Dsp;

/// <summary>Small fast PRNG (xoshiro256**), deterministic from its seed.</summary>
public struct FastRng
{
    private ulong _a, _b, _c, _d;

    public FastRng(ulong seed)
    {
        ulong z = seed;
        _a = SplitMix(ref z);
        _b = SplitMix(ref z);
        _c = SplitMix(ref z);
        _d = SplitMix(ref z);
    }

    private static ulong SplitMix(ref ulong state)
    {
        ulong z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public ulong NextUInt64()
    {
        ulong s = _b * 5;
        ulong result = ((s << 7) | (s >> 57)) * 9;
        ulong t = _b << 17;
        _c ^= _a;
        _d ^= _b;
        _b ^= _c;
        _a ^= _d;
        _c ^= t;
        _d = (_d << 45) | (_d >> 19);
        return result;
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    public int NextInt(int exclusiveMax) => (int)(((NextUInt64() >> 32) * (ulong)exclusiveMax) >> 32);
}

/// <summary>
/// Table-driven noise at hundreds of MS/s: every 4096-sample chunk adds two slices of a shared random table
/// at fresh random offsets. Chunking follows the absolute sample count, so output is block-size invariant.
/// </summary>
public sealed unsafe class TableNoise
{
    private const int TableLength = 1 << 18;
    private const int Chunk = 4096;
    private static readonly float[] Gaussian = Build(gaussian: true);
    private static readonly float[] Uniform = Build(gaussian: false);

    private readonly float[] _table;
    private readonly float _scale;
    private FastRng _rng;
    private int _offsetA, _offsetB, _left;

    private TableNoise(float[] table, float scale, ulong seed)
    {
        _table = table;
        _scale = scale;
        _rng = new FastRng(seed);
    }

    /// <summary>Unit-variance Gaussian (sum of two table Gaussians / √2).</summary>
    public static TableNoise CreateGaussian(ulong seed) => new(Gaussian, (float)(1.0 / Math.Sqrt(2.0)), seed);

    /// <summary>Triangular PDF on (−1, 1): the sum of two uniforms on (−0.5, 0.5). Multiply by one LSB.</summary>
    public static TableNoise CreateTpdf(ulong seed) => new(Uniform, 1f, seed);

    private static float[] Build(bool gaussian)
    {
        var rng = new FastRng(gaussian ? 0x5EEDUL : 0xD17BE5UL);
        var t = new float[TableLength + Chunk];
        for (int i = 0; i < TableLength; i++)
        {
            if (!gaussian)
            {
                t[i] = (float)(rng.NextDouble() - 0.5);
                continue;
            }
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            t[i] = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
        Array.Copy(t, 0, t, TableLength, Chunk);
        return t;
    }

    /// <summary>target[i] += amplitude · noise.</summary>
    public void AddTo(Span<float> target, float amplitude)
    {
        float gain = amplitude * _scale;
        Vector128<float> vg = Vector128.Create(gain);
        int done = 0;
        fixed (float* table = _table, y = target)
        {
            while (done < target.Length)
            {
                if (_left == 0)
                {
                    _offsetA = _rng.NextInt(TableLength);
                    _offsetB = _rng.NextInt(TableLength);
                    _left = Chunk;
                }
                int run = Math.Min(_left, target.Length - done);
                float* a = table + _offsetA + (Chunk - _left), b = table + _offsetB + (Chunk - _left), o = y + done;
                int i = 0;
                for (; i + 4 <= run; i += 4)
                    Vector128.FusedMultiplyAdd(Vector128.Load(a + i) + Vector128.Load(b + i), vg, Vector128.Load(o + i)).Store(o + i);
                for (; i < run; i++) o[i] = MathF.FusedMultiplyAdd(a[i] + b[i], gain, o[i]);
                done += run;
                _left -= run;
            }
        }
    }
}
