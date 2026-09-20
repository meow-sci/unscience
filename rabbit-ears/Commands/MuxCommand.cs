using System;
using System.Collections.Generic;
using System.Diagnostics;
using RabbitEars.Pal;
using RabbitEars.Rf;
using RabbitEars.Sources;

namespace RabbitEars.Commands;

/// <summary>mux: N sources → PAL encoders → one wideband file (offline, as fast as the machine goes).</summary>
public static class MuxCommand
{
    public const string Usage =
        "mux --channel <source> [--channel <source> ...] --out <file> [--seconds 4] [--k 6] [--format u8|s16]\n" +
        "      [--slots 0,1,..] [--levels dB,dB,..] [--block-lines 125] [--threads n] [--no-dither]\n" +
        "      source = " + SourceFactory.Help;

    public static int Run(string[] argv)
    {
        var args = new Args(argv, new[] { "channel", "out", "seconds", "k", "format", "slots", "levels", "block-lines", "threads" }, new[] { "no-dither" });
        var plan = new ChannelPlan(args.Int("k", 6));
        IReadOnlyList<string> specs = args.All("channel");
        if (specs.Count == 0) throw new ArgumentException("at least one --channel is required");
        if (specs.Count > plan.MaxChannels) throw new ArgumentException($"K={plan.K} holds at most {plan.MaxChannels} channels");
        string outPath = args.Require("out");
        double seconds = args.Double("seconds", 4.0);
        SampleFormat format = SampleCodec.ParseFormat(args.Get("format") ?? "u8");
        double[] slots = args.DoubleList("slots"), levels = args.DoubleList("levels");
        int blockLines = Math.Max(1, args.Int("block-lines", 125));

        var mux = new WidebandMux(plan, specs.Count, args.Int("threads", 0));
        var info = new WidebandFileInfo { SampleRate = plan.WidebandRate, K = plan.K, ReferenceCarrierAmplitude = mux.ReferenceAmplitude };
        var sources = new List<IFrameSource>();
        TimeSpan startOfDay = DateTime.Now.TimeOfDay;
        try
        {
            for (int i = 0; i < specs.Count; i++)
            {
                int slot = i < slots.Length ? (int)slots[i] : i;
                double level = i < levels.Length ? levels[i] : 0.0;
                double carrier = plan.CarrierHz(slot);
                IFrameSource source = SourceFactory.Create(specs[i], slot + 1, $"{carrier / 1e6:0.00} MHZ", paced: false, startOfDay);
                sources.Add(source);
                mux.AddChannel(slot, new PalEncoder(source, noiseSeed: (ulong)(slot + 1)), source.Name, level);
                info.Channels.Add(new WidebandChannelInfo { Slot = slot, CarrierHz = carrier, LevelDb = level, Name = source.Name, Source = specs[i] });
                Console.WriteLine($"slot {slot}: {carrier / 1e6:0.00} MHz  {level,5:0.0} dB  {specs[i]}");
            }

            long totalLow = (long)Math.Round(seconds * ChannelPlan.ChannelRate);
            int blockLow = blockLines * PalTiming.SamplesPerLine;
            var block = new float[blockLow * plan.K];
            var clock = Stopwatch.StartNew();
            using (var writer = new WidebandWriter(outPath, format, info, dither: !args.Has("no-dither")))
            {
                for (long done = 0; done < totalLow; done += blockLow)
                {
                    int n = (int)Math.Min(blockLow, totalLow - done);
                    mux.Process(block, n);
                    writer.Write(block.AsSpan(0, n * plan.K));
                }
            }
            double elapsed = clock.Elapsed.TotalSeconds;
            Console.WriteLine($"wrote {outPath}: {seconds:0.###} s, {info.SampleCount / 1e6:0.0} MS {info.Format} at {plan.WidebandRate / 1e6:0} MS/s " +
                              $"in {elapsed:0.00} s ({seconds / elapsed:0.00}x real time)");
        }
        finally
        {
            foreach (IFrameSource source in sources) source.Dispose();
        }
        return 0;
    }
}
