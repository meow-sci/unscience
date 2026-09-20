using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace RabbitEars.Dsp;

/// <summary>
/// SIMD inner loops (Vector128: NEON on Apple silicon, SSE on x64). Every kernel accumulates each output
/// in a fixed tap order with fused multiply-adds, in both the vector body and the scalar tail, so results
/// do not depend on how a stream is cut into blocks.
/// </summary>
public static unsafe class Kernels
{
    /// <summary>
    /// Complex-tap FIR over a real input, vectorised across outputs:
    /// outR[n] = Σ_j x[n + j]·tapsR[j], outI[n] likewise. Taps are in correlation order (already reversed);
    /// <paramref name="x"/> must hold count + taps − 1 samples.
    /// </summary>
    public static void FirPair(float* x, float* tapsR, float* tapsI, int taps, float* outR, float* outI, int count)
    {
        int n = 0;
        for (; n + 8 <= count; n += 8)
        {
            Vector128<float> r0 = Vector128<float>.Zero, r1 = r0, i0 = r0, i1 = r0;
            float* p = x + n;
            for (int j = 0; j < taps; j++)
            {
                Vector128<float> a = Vector128.Load(p + j), b = Vector128.Load(p + j + 4);
                Vector128<float> tr = Vector128.Create(tapsR[j]), ti = Vector128.Create(tapsI[j]);
                r0 = Vector128.FusedMultiplyAdd(a, tr, r0);
                r1 = Vector128.FusedMultiplyAdd(b, tr, r1);
                i0 = Vector128.FusedMultiplyAdd(a, ti, i0);
                i1 = Vector128.FusedMultiplyAdd(b, ti, i1);
            }
            r0.Store(outR + n);
            r1.Store(outR + n + 4);
            i0.Store(outI + n);
            i1.Store(outI + n + 4);
        }
        for (; n < count; n++)
        {
            float r = 0f, i = 0f;
            float* p = x + n;
            for (int j = 0; j < taps; j++)
            {
                r = MathF.FusedMultiplyAdd(p[j], tapsR[j], r);
                i = MathF.FusedMultiplyAdd(p[j], tapsI[j], i);
            }
            outR[n] = r;
            outI[n] = i;
        }
    }

    /// <summary>Real FIR, vectorised across outputs: y[n] = Σ_j x[n + j]·taps[j].</summary>
    public static void Fir(float* x, float* taps, int tapCount, float* y, int count)
    {
        int n = 0;
        for (; n + 8 <= count; n += 8)
        {
            Vector128<float> a0 = Vector128<float>.Zero, a1 = a0;
            float* p = x + n;
            for (int j = 0; j < tapCount; j++)
            {
                Vector128<float> t = Vector128.Create(taps[j]);
                a0 = Vector128.FusedMultiplyAdd(Vector128.Load(p + j), t, a0);
                a1 = Vector128.FusedMultiplyAdd(Vector128.Load(p + j + 4), t, a1);
            }
            a0.Store(y + n);
            a1.Store(y + n + 4);
        }
        for (; n < count; n++)
        {
            float acc = 0f;
            for (int j = 0; j < tapCount; j++) acc = MathF.FusedMultiplyAdd(x[n + j], taps[j], acc);
            y[n] = acc;
        }
    }

    /// <summary>Σ a[i]·ta[i] + b[i]·tb[i] over a tap count that is a multiple of 4.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DotDual(float* a, float* ta, float* b, float* tb, int taps)
    {
        Vector128<float> s0 = Vector128<float>.Zero, s1 = s0;
        for (int j = 0; j < taps; j += 4)
        {
            s0 = Vector128.FusedMultiplyAdd(Vector128.Load(a + j), Vector128.Load(ta + j), s0);
            s1 = Vector128.FusedMultiplyAdd(Vector128.Load(b + j), Vector128.Load(tb + j), s1);
        }
        return Vector128.Sum(s0 + s1);
    }

    /// <summary>(Σ x[i]·tr[i], Σ x[i]·ti[i]) over a tap count that is a multiple of 4.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DotPair(float* x, float* tr, float* ti, int taps, out float re, out float im)
    {
        Vector128<float> s0 = Vector128<float>.Zero, s1 = s0;
        for (int j = 0; j < taps; j += 4)
        {
            Vector128<float> v = Vector128.Load(x + j);
            s0 = Vector128.FusedMultiplyAdd(v, Vector128.Load(tr + j), s0);
            s1 = Vector128.FusedMultiplyAdd(v, Vector128.Load(ti + j), s1);
        }
        re = Vector128.Sum(s0);
        im = Vector128.Sum(s1);
    }

    /// <summary>y[i] = a[i]·s + c.</summary>
    public static void ScaleOffset(ReadOnlySpan<float> a, float s, float c, Span<float> y)
    {
        int i = 0, n = a.Length;
        Vector128<float> vs = Vector128.Create(s), vc = Vector128.Create(c);
        fixed (float* pa = a, py = y)
        {
            for (; i + 4 <= n; i += 4) Vector128.FusedMultiplyAdd(Vector128.Load(pa + i), vs, vc).Store(py + i);
            for (; i < n; i++) py[i] = MathF.FusedMultiplyAdd(pa[i], s, c);
        }
    }

    /// <summary>acc[i] += a[i].</summary>
    public static void Accumulate(ReadOnlySpan<float> a, Span<float> acc)
    {
        int i = 0, n = a.Length;
        fixed (float* pa = a, pacc = acc)
        {
            for (; i + 8 <= n; i += 8)
            {
                (Vector128.Load(pacc + i) + Vector128.Load(pa + i)).Store(pacc + i);
                (Vector128.Load(pacc + i + 4) + Vector128.Load(pa + i + 4)).Store(pacc + i + 4);
            }
            for (; i < n; i++) pacc[i] += pa[i];
        }
    }

    /// <summary>y[i] = clamp(a[i], lo, hi).</summary>
    public static void Clamp(Span<float> a, float lo, float hi)
    {
        int i = 0, n = a.Length;
        Vector128<float> vlo = Vector128.Create(lo), vhi = Vector128.Create(hi);
        fixed (float* pa = a)
        {
            for (; i + 4 <= n; i += 4) Vector128.Min(Vector128.Max(Vector128.Load(pa + i), vlo), vhi).Store(pa + i);
            for (; i < n; i++) pa[i] = MathF.Min(MathF.Max(pa[i], lo), hi);
        }
    }
}
