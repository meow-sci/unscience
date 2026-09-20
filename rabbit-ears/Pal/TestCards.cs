using System;

namespace RabbitEars.Pal;

/// <summary>Procedural test cards. Each style is unmistakable at a glance, and every card carries a big channel number.</summary>
public static class TestCards
{
    public static readonly string[] Styles = { "bars", "grid", "hue", "checker", "steps", "rays" };

    private static readonly Rgb[] Bars75 =
    {
        new(191, 191, 191), new(191, 191, 0), new(0, 191, 191), new(0, 191, 0),
        new(191, 0, 191), new(191, 0, 0), new(0, 0, 191), new(0, 0, 0),
    };

    private static readonly Rgb[] Bars100 =
    {
        new(255, 255, 255), new(255, 255, 0), new(0, 255, 255), new(0, 255, 0),
        new(255, 0, 255), new(255, 0, 0), new(0, 0, 255), new(0, 0, 0),
    };

    public static bool IsStyle(string style) => Array.IndexOf(Styles, style) >= 0;

    /// <summary>Renders the static part of a card (moving parts are added per frame by the card source).</summary>
    public static Canvas Render(string style, int channelNumber, string name, string caption)
    {
        var c = new Canvas();
        switch (style)
        {
            case "bars": DrawBars(c); break;
            case "grid": DrawGrid(c); break;
            case "hue": DrawHue(c); break;
            case "checker": DrawChecker(c, channelNumber); break;
            case "steps": DrawSteps(c); break;
            case "rays": DrawRays(c); break;
            default: throw new ArgumentException($"unknown card style '{style}' (known: {string.Join(", ", Styles)})");
        }
        DrawIdent(c, channelNumber, name, caption);
        return c;
    }

    private static void DrawIdent(Canvas c, int channelNumber, string name, string caption)
    {
        const double cx = Canvas.VirtualWidth / 2.0;
        c.FillRect(cx - 200, 196, 400, 236, Rgb.White);
        c.FillRect(cx - 196, 200, 392, 228, Rgb.Black);
        c.TextCentred(cx, 212, 18, channelNumber.ToString(), Rgb.White);
        c.TextCentred(cx, 352, 4, Truncate(name, 15), new Rgb(255, 220, 60));
        c.TextCentred(cx, 392, 3, Truncate(caption, 20), new Rgb(120, 255, 160));
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

    private static void DrawBars(Canvas c)
    {
        double w = Canvas.VirtualWidth / 8.0;
        for (int i = 0; i < 8; i++)
        {
            c.FillRect(i * w, 0, w, 440, Bars75[i]);
            c.FillRect(i * w, 440, w, 56, Bars100[i]);
        }
        for (int i = 0; i < 11; i++)
            c.FillRect(i * Canvas.VirtualWidth / 11.0, 496, Canvas.VirtualWidth / 11.0, 80, Rgb.Grey((int)Math.Round(i * 25.5)));
    }

    private static void DrawGrid(Canvas c)
    {
        c.FillRect(0, 0, Canvas.VirtualWidth, Canvas.VirtualHeight, new Rgb(24, 28, 60));
        for (int x = 0; x <= Canvas.VirtualWidth; x += 48) c.FillRect(x - 1, 0, 2, Canvas.VirtualHeight, Rgb.Grey(200));
        for (int y = 0; y <= Canvas.VirtualHeight; y += 48) c.FillRect(0, y - 1, Canvas.VirtualWidth, 2, Rgb.Grey(200));
        c.Ring(Canvas.VirtualWidth / 2.0, Canvas.VirtualHeight / 2.0, 262, 266, Rgb.White);
        double w = (Canvas.VirtualWidth - 96) / 8.0;
        for (int i = 0; i < 8; i++) c.FillRect(48 + i * w, 48, w, 96, Bars75[i]);
    }

    private static void DrawHue(Canvas c) =>
        c.ShadeRect(0, 0, Canvas.VirtualWidth, Canvas.VirtualHeight, (u, v) => Rgb.FromHsl(u * 360, 0.9, 0.75 - 0.5 * v));

    private static void DrawChecker(Canvas c, int channelNumber)
    {
        Rgb a = Rgb.FromHsl(channelNumber * 67 + 20, 0.85, 0.5), b = Rgb.FromHsl(channelNumber * 67 + 200, 0.85, 0.35);
        for (int y = 0; y < Canvas.VirtualHeight; y += 64)
            for (int x = 0; x < Canvas.VirtualWidth; x += 64)
                c.FillRect(x, y, 64, 64, ((x + y) / 64 & 1) == 0 ? a : b);
    }

    private static void DrawSteps(Canvas c)
    {
        for (int i = 0; i < 11; i++)
            c.FillRect(i * Canvas.VirtualWidth / 11.0, 0, Canvas.VirtualWidth / 11.0, 288, Rgb.Grey((int)Math.Round(i * 25.5)));
        double[] megahertz = { 0.5, 1.0, 2.0, 3.0, 4.0, 5.0 };
        double segment = Canvas.VirtualWidth / (double)megahertz.Length;
        for (int i = 0; i < megahertz.Length; i++)
        {
            double cycles = megahertz[i] * 1e6 * segment / Canvas.VirtualWidth * 52e-6;    // cycles across the segment
            c.ShadeRect(i * segment, 288, segment, 288, (u, _) => Rgb.Grey((int)Math.Round(127.5 + 100 * Math.Sin(2 * Math.PI * cycles * u))));
        }
    }

    private static void DrawRays(Canvas c) =>
        c.ShadeRect(0, 0, Canvas.VirtualWidth, Canvas.VirtualHeight, (u, v) =>
        {
            double angle = Math.Atan2((v - 0.5) * Canvas.VirtualHeight, (u - 0.5) * Canvas.VirtualWidth);
            int sector = (int)Math.Floor((angle + Math.PI) / (2 * Math.PI) * 24);
            return (sector & 1) == 0 ? Rgb.FromHsl(sector * 15, 0.9, 0.5) : Rgb.Grey(32);
        });
}
