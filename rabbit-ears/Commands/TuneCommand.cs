using System;
using System.Diagnostics;
using RabbitEars.Rf;

namespace RabbitEars.Commands;

/// <summary>tune: wideband file → 40 MS/s real IF (vision carrier at 8 MHz) as a SigMF pair for PALindrome.</summary>
public static class TuneCommand
{
    public const string Usage =
        "tune --in <wideband file> (--slot n | --freq MHz) --out <stem> [--sweep-to MHz] [--noise-db dB] [--atten-db dB]\n" +
        "      [--start s] [--seconds s]      (noise: rms, dB below a 0 dB carrier's sync tip, over the 20 MHz IF)";

    public static int Run(string[] argv)
    {
        var args = new Args(argv, new[] { "in", "slot", "freq", "out", "sweep-to", "noise-db", "atten-db", "start", "seconds" }, Array.Empty<string>());
        string inPath = args.Require("in"), stem = args.Require("out");
        using var reader = new WidebandReader(inPath);
        WidebandFileInfo info = reader.Info;
        var plan = new ChannelPlan(info.K);

        double startHz = args.Has("slot") ? plan.CarrierHz(args.Int("slot", 0))
            : args.Has("freq") ? args.Double("freq", 0) * 1e6
            : throw new ArgumentException("--slot or --freq is required");
        double endHz = args.OptionalDouble("sweep-to") is { } sweep ? sweep * 1e6 : startHz;

        var tuner = new Tuner(plan) { FrequencyHz = startHz, AttenuationDb = args.Double("atten-db", 0) };
        tuner.SetReferenceCarrier(info.ReferenceCarrierAmplitude);
        tuner.SetNoiseDbBelowCarrier(args.OptionalDouble("noise-db"));

        long skip = (long)(args.Double("start", 0) * plan.WidebandRate);
        long total = Math.Max(0, info.SampleCount - skip);
        if (args.OptionalDouble("seconds") is { } seconds) total = Math.Min(total, (long)(seconds * plan.WidebandRate));

        const int block = 1 << 18;
        var wide = new float[block];
        var ifOut = new float[tuner.MaxOutputFor(block)];
        var clock = Stopwatch.StartNew();
        string description = $"rabbit-ears IF from {System.IO.Path.GetFileName(inPath)}, tuned {startHz / 1e6:0.###} MHz, vision carrier at 8 MHz";
        long written = 0;
        using (var writer = new SigMfWriter(stem, ChannelPlan.IfRate, description))
        {
            for (long left = skip; left > 0;)                                       // discard up to --start
            {
                int n = reader.Read(wide.AsSpan(0, (int)Math.Min(block, left)));
                if (n == 0) break;
                left -= n;
            }
            for (long done = 0; done < total;)
            {
                int n = reader.Read(wide.AsSpan(0, (int)Math.Min(block, total - done)));
                if (n == 0) break;
                if (endHz != startHz) tuner.FrequencyHz = startHz + (endHz - startHz) * done / Math.Max(1, total);
                int produced = tuner.Process(wide.AsSpan(0, n), ifOut);
                writer.Write(ifOut.AsSpan(0, produced));
                written += produced;
                done += n;
            }
        }
        double duration = written / ChannelPlan.IfRate;
        string tuned = endHz == startHz ? $"{startHz / 1e6:0.###} MHz" : $"{startHz / 1e6:0.###} -> {endHz / 1e6:0.###} MHz";
        Console.WriteLine($"wrote {stem}.sigmf-data/.sigmf-meta: {tuned}, {duration:0.###} s of IF in {clock.Elapsed.TotalSeconds:0.00} s " +
                          $"({duration / clock.Elapsed.TotalSeconds:0.0}x real time)");
        return 0;
    }
}
