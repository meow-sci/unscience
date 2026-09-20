using System;

namespace RabbitEars.Pal;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(255, 255, 255);

    public static Rgb Grey(int level) => new((byte)level, (byte)level, (byte)level);

    /// <summary>Hue in degrees, saturation and lightness 0..1.</summary>
    public static Rgb FromHsl(double hue, double saturation, double lightness)
    {
        double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double h = ((hue % 360) + 360) % 360 / 60.0;
        double x = c * (1 - Math.Abs(h % 2 - 1));
        (double r, double g, double b) = (int)h switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        double m = lightness - c / 2;
        return new Rgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v * 255), 0, 255);
}

/// <summary>
/// RGB24 drawing surface on the encoder's native 1040×576 grid. Callers draw in a 768×576 square-pixel
/// space (what the viewer sees on a 4:3 screen); x is stretched to the native grid, so circles stay round.
/// </summary>
public sealed class Canvas
{
    public const int VirtualWidth = 768;
    public const int VirtualHeight = 576;
    public const int Width = PalTiming.ActiveSamples;
    public const int Height = PalTiming.ActiveRows;
    private const double XScale = (double)Width / VirtualWidth;

    public Canvas() => Pixels = new byte[Width * Height * 3];

    public byte[] Pixels { get; }

    public void CopyFrom(Canvas other) => Buffer.BlockCopy(other.Pixels, 0, Pixels, 0, Pixels.Length);

    private static int MapX(double x) => (int)Math.Round(x * XScale);

    public void FillRect(double x, double y, double width, double height, Rgb colour)
    {
        int x0 = Math.Clamp(MapX(x), 0, Width), x1 = Math.Clamp(MapX(x + width), 0, Width);
        int y0 = Math.Clamp((int)Math.Round(y), 0, Height), y1 = Math.Clamp((int)Math.Round(y + height), 0, Height);
        for (int row = y0; row < y1; row++)
        {
            int at = (row * Width + x0) * 3;
            for (int col = x0; col < x1; col++, at += 3)
            {
                Pixels[at] = colour.R;
                Pixels[at + 1] = colour.G;
                Pixels[at + 2] = colour.B;
            }
        }
    }

    /// <summary>Per-pixel fill of a virtual-space rectangle: <paramref name="shade"/> gets (u, v) in 0..1.</summary>
    public void ShadeRect(double x, double y, double width, double height, Func<double, double, Rgb> shade)
    {
        int x0 = Math.Clamp(MapX(x), 0, Width), x1 = Math.Clamp(MapX(x + width), 0, Width);
        int y0 = Math.Clamp((int)Math.Round(y), 0, Height), y1 = Math.Clamp((int)Math.Round(y + height), 0, Height);
        for (int row = y0; row < y1; row++)
            for (int col = x0; col < x1; col++)
            {
                Rgb c = shade((col - x0 + 0.5) / Math.Max(1, x1 - x0), (row - y0 + 0.5) / Math.Max(1, y1 - y0));
                int at = (row * Width + col) * 3;
                Pixels[at] = c.R;
                Pixels[at + 1] = c.G;
                Pixels[at + 2] = c.B;
            }
    }

    public void FillEllipse(double cx, double cy, double radius, Rgb colour) => Ring(cx, cy, 0, radius, colour);

    /// <summary>Filled annulus between two radii (virtual pixels); inner 0 gives a disc.</summary>
    public void Ring(double cx, double cy, double inner, double outer, Rgb colour)
    {
        int y0 = Math.Clamp((int)Math.Floor(cy - outer), 0, Height), y1 = Math.Clamp((int)Math.Ceiling(cy + outer) + 1, 0, Height);
        int x0 = Math.Clamp(MapX(cx - outer) - 1, 0, Width), x1 = Math.Clamp(MapX(cx + outer) + 2, 0, Width);
        double in2 = inner * inner, out2 = outer * outer;
        for (int row = y0; row < y1; row++)
            for (int col = x0; col < x1; col++)
            {
                double dx = col / XScale - cx, dy = row - cy, d2 = dx * dx + dy * dy;
                if (d2 > out2 || d2 < in2) continue;
                int at = (row * Width + col) * 3;
                Pixels[at] = colour.R;
                Pixels[at + 1] = colour.G;
                Pixels[at + 2] = colour.B;
            }
    }

    /// <summary>Draws text with its top-left at (x, y); each font dot is a scale×scale virtual square.</summary>
    public void Text(double x, double y, int scale, string text, Rgb colour)
    {
        for (int i = 0; i < text.Length; i++)
            for (int col = 0; col < BitmapFont.GlyphWidth; col++)
                for (int row = 0; row < BitmapFont.GlyphHeight; row++)
                    if (BitmapFont.IsSet(text[i], col, row))
                        FillRect(x + (i * BitmapFont.Advance + col) * scale, y + row * scale, scale, scale, colour);
    }

    public void TextCentred(double cx, double y, int scale, string text, Rgb colour) =>
        Text(cx - BitmapFont.MeasureWidth(text, scale) / 2.0, y, scale, text, colour);
}
