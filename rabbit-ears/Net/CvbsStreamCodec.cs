using System;
using System.Runtime.InteropServices;

namespace RabbitEars.Net;

/// <summary>Sample formats a sender may use on the wire (the handshake's format byte).</summary>
public enum CvbsFormat : byte
{
    U8 = 0,
    S16 = 1,
}

/// <summary>
/// Composite video volts ↔ wire samples, exactly as the sender contract defines them:
/// u8 = round((v + 0.42) · 182), s16 = round(v · 24000), little-endian. The server keeps its FIFOs in the
/// s16 scale, so there is also a direct wire → s16 path.
/// </summary>
public static class CvbsStreamCodec
{
    public const float U8Scale = 182f, U8OffsetVolts = 0.42f, S16Scale = 24000f;
    private static readonly short[] U8ToS16 = BuildU8ToS16();

    public static int BytesPerSample(CvbsFormat format) => format == CvbsFormat.U8 ? 1 : 2;

    public static CvbsFormat ParseFormat(string text) => text.ToLowerInvariant() switch
    {
        "u8" => CvbsFormat.U8,
        "s16" => CvbsFormat.S16,
        _ => throw new ArgumentException($"unknown CVBS stream format '{text}' (u8 | s16)"),
    };

    private static short[] BuildU8ToS16()
    {
        var table = new short[256];
        for (int i = 0; i < 256; i++) table[i] = (short)MathF.Round((i / U8Scale - U8OffsetVolts) * S16Scale);
        return table;
    }

    /// <summary>Volts → wire bytes (samples.Length × bytes-per-sample), clipped to the format's range.</summary>
    public static void Encode(ReadOnlySpan<float> volts, CvbsFormat format, Span<byte> wire)
    {
        if (format == CvbsFormat.U8)
        {
            for (int i = 0; i < volts.Length; i++)
                wire[i] = (byte)Math.Clamp(MathF.Round((volts[i] + U8OffsetVolts) * U8Scale), 0f, 255f);
            return;
        }
        Span<short> words = MemoryMarshal.Cast<byte, short>(wire);
        for (int i = 0; i < volts.Length; i++)
            words[i] = (short)Math.Clamp(MathF.Round(volts[i] * S16Scale), -32768f, 32767f);
    }

    /// <summary>Wire bytes → volts.</summary>
    public static void Decode(ReadOnlySpan<byte> wire, CvbsFormat format, Span<float> volts)
    {
        if (format == CvbsFormat.U8)
        {
            for (int i = 0; i < volts.Length; i++) volts[i] = wire[i] / U8Scale - U8OffsetVolts;
            return;
        }
        ReadOnlySpan<short> words = MemoryMarshal.Cast<byte, short>(wire);
        for (int i = 0; i < volts.Length; i++) volts[i] = words[i] / S16Scale;
    }

    /// <summary>Wire bytes → the s16 scale (volts × 24000) used by the server's FIFOs.</summary>
    public static void DecodeToS16(ReadOnlySpan<byte> wire, CvbsFormat format, Span<short> output)
    {
        if (format == CvbsFormat.S16)
        {
            MemoryMarshal.Cast<byte, short>(wire).Slice(0, output.Length).CopyTo(output);
            return;
        }
        short[] table = U8ToS16;
        for (int i = 0; i < output.Length; i++) output[i] = table[wire[i]];
    }

    /// <summary>The s16 scale → volts.</summary>
    public static void S16ToVolts(ReadOnlySpan<short> samples, Span<float> volts)
    {
        const float inv = 1f / S16Scale;
        for (int i = 0; i < samples.Length; i++) volts[i] = samples[i] * inv;
    }
}
