using System;
using System.Collections.Generic;
using System.Globalization;
using RabbitEars.Pal;
using RabbitEars.Rf;

namespace RabbitEars.Tv;

/// <summary>Turns a knob set into the argument list of one `palindrome render --live --input rf` process.</summary>
public static class DecoderCommandLine
{
    /// <summary>The raster the decoder defaults were calibrated on; contrast is rescaled for any other.</summary>
    private const double ReferencePixels = 720.0 * 576.0;

    public static (int Width, int Height) Raster(IReadOnlyDictionary<string, string> knobs)
    {
        string[] parts = knobs["raster"].Split('x');
        return (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture));
    }

    public static int Channels(IReadOnlyDictionary<string, string> knobs) => knobs["colour"] == "1" ? 3 : 1;

    public static int Stride(IReadOnlyDictionary<string, string> knobs) => knobs["stride"] == SetKnobs.StrideEveryField ? 1 : 2;

    public static List<string> Build(IReadOnlyDictionary<string, string> knobs)
    {
        (int width, int height) = Raster(knobs);
        double gamma = SetKnobs.ByName["gamma"].Number(knobs["gamma"]);
        // Light per pixel scales with pixel area and the gun is a power law, hence this contrast rule.
        double rasterCalibration = Math.Pow(width * height / ReferencePixels, 1.0 / gamma);

        var args = new List<string>
        {
            "render", "--live", "--input", "rf",
            "--sample-rate", Invariant(ChannelPlan.IfRate), "--sample-format", "s16",
            "--carrier", Invariant(ChannelPlan.IfCarrierHz), "--decimate", "2",
            "--frame-fd", "1", "--frame-stride", Invariant(Stride(knobs)),
            "--width", Invariant(width), "--height", Invariant(height), "--deposit-threads", "2",
        };
        foreach (Knob knob in SetKnobs.All)
        {
            string value = knobs[knob.Name];
            switch (knob.Name)
            {
                case "contrast":
                    args.Add("--contrast");
                    args.Add((knob.Number(value) * rasterCalibration).ToString("0.####", CultureInfo.InvariantCulture));
                    break;
                case "colour":
                    if (value == "1") args.Add("--colour");
                    break;
                case "killer":
                    if (value == "0") args.Add("--no-killer");
                    break;
                case "crystal_offset":
                    if (knob.Number(value) != 0)
                    {
                        args.Add("--subcarrier");
                        args.Add((PalTiming.SubcarrierHz + knob.Number(value)).ToString("0.00", CultureInfo.InvariantCulture));
                    }
                    break;
                case "beam_sigma_x":
                    if (knobs["round_spot"] == "0") AddFlag(args, knob, value);
                    break;
                default:
                    if (knob.Flag is not null && !(knob.OnlyWhenChanged && value == knob.Default)) AddFlag(args, knob, value);
                    break;
            }
        }
        return args;
    }

    private static void AddFlag(List<string> args, Knob knob, string value)
    {
        args.Add(knob.Flag!);
        args.Add(value);
    }

    private static string Invariant(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}
