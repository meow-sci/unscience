using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using RabbitEars.Dsp;

namespace RabbitEars.Rf;

public enum SampleFormat { U8, S16 }

/// <summary>
/// Float (±1 full scale) ↔ integer samples. u8 is offset-binary (128 = zero, 127 per unit); s16 is
/// little-endian, 32767 per unit. Quantising adds TPDF dither of ±1 LSB so the error is signal-independent noise.
/// </summary>
public sealed unsafe class SampleCodec
{
    private const int Chunk = 4096;
    private static readonly float[] U8ToFloat = BuildU8Table();
    private readonly TableNoise? _dither;
    private readonly float[] _scratch = new float[Chunk];

    public SampleCodec(bool dither = true, ulong seed = 99) => _dither = dither ? TableNoise.CreateTpdf(seed) : null;

    public static int BytesPerSample(SampleFormat format) => format == SampleFormat.U8 ? 1 : 2;

    public static SampleFormat ParseFormat(string text) => text.ToLowerInvariant() switch
    {
        "u8" => SampleFormat.U8,
        "s16" => SampleFormat.S16,
        _ => throw new ArgumentException($"unknown sample format '{text}' (u8 | s16)"),
    };

    private static float[] BuildU8Table()
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++) t[i] = (i - 128) / 127f;
        return t;
    }

    /// <summary>Quantises into <paramref name="bytes"/> (samples.Length × bytes-per-sample long).</summary>
    public void Encode(ReadOnlySpan<float> samples, SampleFormat format, Span<byte> bytes)
    {
        bool u8 = format == SampleFormat.U8;
        float scale = u8 ? 127f : 32767f, offset = u8 ? 128.5f : 32768.5f;          // +0.5: round by truncation
        float max = u8 ? 255f : 65535f;
        Span<short> words = u8 ? default : MemoryMarshal.Cast<byte, short>(bytes);
        for (int start = 0; start < samples.Length; start += Chunk)
        {
            int n = Math.Min(Chunk, samples.Length - start);
            Span<float> scratch = _scratch.AsSpan(0, n);
            Kernels.ScaleOffset(samples.Slice(start, n), scale, offset, scratch);
            _dither?.AddTo(scratch, 1f);
            Kernels.Clamp(scratch, 0f, max);
            if (u8)
                for (int i = 0; i < n; i++) bytes[start + i] = (byte)(int)scratch[i];
            else
                for (int i = 0; i < n; i++) words[start + i] = (short)((int)scratch[i] - 32768);
        }
    }

    /// <summary>Dequantises samples.Length samples from <paramref name="bytes"/>.</summary>
    public static void Decode(ReadOnlySpan<byte> bytes, SampleFormat format, Span<float> samples)
    {
        if (format == SampleFormat.U8)
        {
            float[] table = U8ToFloat;
            for (int i = 0; i < samples.Length; i++) samples[i] = table[bytes[i]];
            return;
        }
        ReadOnlySpan<short> words = MemoryMarshal.Cast<byte, short>(bytes);
        const float inv = 1f / 32767f;
        for (int i = 0; i < samples.Length; i++) samples[i] = words[i] * inv;
    }

    /// <summary>Plain (undithered) float → s16 with clipping, for the tuner's IF output.</summary>
    public static void ToS16(ReadOnlySpan<float> samples, Span<short> output)
    {
        int i = 0, n = samples.Length;
        Vector128<float> scale = Vector128.Create(32767f), lo = Vector128.Create(-32768f), hi = Vector128.Create(32767f);
        fixed (float* x = samples)
        fixed (short* y = output)
        {
            for (; i + 8 <= n; i += 8)
            {
                Vector128<int> a = Vector128.ConvertToInt32(Vector128.Round(Vector128.Min(Vector128.Max(Vector128.Load(x + i) * scale, lo), hi)));
                Vector128<int> b = Vector128.ConvertToInt32(Vector128.Round(Vector128.Min(Vector128.Max(Vector128.Load(x + i + 4) * scale, lo), hi)));
                Vector128.Narrow(a, b).Store(y + i);
            }
            for (; i < n; i++) y[i] = (short)MathF.Round(Math.Clamp(x[i] * 32767f, -32768f, 32767f));
        }
    }
}
