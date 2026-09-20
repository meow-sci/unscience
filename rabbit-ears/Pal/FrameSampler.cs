using System;
using RabbitEars.Dsp;
using RabbitEars.Sources;

namespace RabbitEars.Pal;

/// <summary>
/// Turns one source row into the encoder's line grid: Y, U, V as floats over 1040 active samples, with U and V
/// already weighted (0.493, 0.877) and low-passed to PAL chroma bandwidth. Frames of any size are resampled
/// bilinearly; a 1040×576 frame takes the direct path.
/// </summary>
public sealed unsafe class FrameSampler
{
    private const int Width = PalTiming.ActiveSamples;
    private const int ChromaTaps = 23;
    private const int Pad = ChromaTaps / 2;
    private static readonly float[] ByteToUnit = BuildByteTable();
    private static readonly float[] ChromaLowPass =
        FirDesign.ToFloat(FirDesign.LowPass(ChromaTaps, 1.1e6, 3.0e6, PalTiming.SampleRate));

    private readonly float[] _r = new float[Width], _g = new float[Width], _b = new float[Width];
    private readonly float[] _uPadded = new float[Width + 2 * Pad], _vPadded = new float[Width + 2 * Pad];
    private int[] _x0 = Array.Empty<int>();
    private float[] _wx = Array.Empty<float>();
    private int _mappedWidth = -1;

    private static float[] BuildByteTable()
    {
        var t = new float[256];
        for (int i = 0; i < 256; i++) t[i] = i / 255f;
        return t;
    }

    /// <summary>Fills y/u/v (each 1040 long) for picture row 0..575 of the frame.</summary>
    public void SampleRow(in VideoFrame frame, int row, float chromaGain, Span<float> y, Span<float> u, Span<float> v)
    {
        LoadRgb(frame, row);
        Span<float> up = _uPadded, vp = _vPadded;
        float ku = 0.493f * chromaGain, kv = 0.877f * chromaGain;
        for (int i = 0; i < Width; i++)
        {
            float r = _r[i], g = _g[i], b = _b[i];
            float luma = 0.299f * r + 0.587f * g + 0.114f * b;
            y[i] = luma;
            up[Pad + i] = ku * (b - luma);
            vp[Pad + i] = kv * (r - luma);
        }
        fixed (float* pu = _uPadded, pv = _vPadded, taps = ChromaLowPass, ou = u, ov = v)
        {
            Kernels.Fir(pu, taps, ChromaTaps, ou, Width);
            Kernels.Fir(pv, taps, ChromaTaps, ov, Width);
        }
    }

    private void LoadRgb(in VideoFrame frame, int row)
    {
        float[] lut = ByteToUnit;
        byte[] rgb = frame.Rgb;
        if (frame.Width == Width && frame.Height == PalTiming.ActiveRows)
        {
            int at = row * Width * 3;
            for (int i = 0; i < Width; i++, at += 3)
            {
                _r[i] = lut[rgb[at]];
                _g[i] = lut[rgb[at + 1]];
                _b[i] = lut[rgb[at + 2]];
            }
            return;
        }

        MapColumns(frame.Width);
        double sy = (row + 0.5) * frame.Height / PalTiming.ActiveRows - 0.5;
        int y0 = Math.Clamp((int)Math.Floor(sy), 0, frame.Height - 1);
        int y1 = Math.Min(y0 + 1, frame.Height - 1);
        float wy = (float)Math.Clamp(sy - y0, 0.0, 1.0);
        int rowA = y0 * frame.Width * 3, rowB = y1 * frame.Width * 3;
        int last = frame.Width - 1;
        for (int i = 0; i < Width; i++)
        {
            int a = _x0[i] * 3, b = Math.Min(_x0[i] + 1, last) * 3;
            float wx = _wx[i];
            for (int c = 0; c < 3; c++)
            {
                float top = lut[rgb[rowA + a + c]] + (lut[rgb[rowA + b + c]] - lut[rgb[rowA + a + c]]) * wx;
                float bottom = lut[rgb[rowB + a + c]] + (lut[rgb[rowB + b + c]] - lut[rgb[rowB + a + c]]) * wx;
                float value = top + (bottom - top) * wy;
                if (c == 0) _r[i] = value;
                else if (c == 1) _g[i] = value;
                else _b[i] = value;
            }
        }
    }

    private void MapColumns(int sourceWidth)
    {
        if (_mappedWidth == sourceWidth) return;
        _mappedWidth = sourceWidth;
        _x0 = new int[Width];
        _wx = new float[Width];
        for (int i = 0; i < Width; i++)
        {
            double sx = (i + 0.5) * sourceWidth / Width - 0.5;
            int x0 = Math.Clamp((int)Math.Floor(sx), 0, sourceWidth - 1);
            _x0[i] = x0;
            _wx[i] = (float)Math.Clamp(sx - x0, 0.0, 1.0);
        }
    }
}
