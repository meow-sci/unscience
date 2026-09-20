using System;
using System.Diagnostics;
using RabbitEars.Pal;
using RabbitEars.Rf;
using RabbitEars.Sources;

namespace RabbitEars.Commands;

/// <summary>bench: throughput of each stage, as MS/s and as a multiple of that stage's real-time rate.</summary>
public static class BenchCommand
{
    public const string Usage = "bench [--k 6] [--channels n] [--seconds 2] [--threads n]";

    public static int Run(string[] argv)
    {
        var args = new Args(argv, new[] { "k", "channels", "seconds", "threads" }, Array.Empty<string>());
        var plan = new ChannelPlan(args.Int("k", 6));
        int channels = Math.Clamp(args.Int("channels", plan.MaxChannels), 1, plan.MaxChannels);
        double seconds = args.Double("seconds", 2.0);
        int threads = args.Int("threads", 0);
        int blockLow = 125 * PalTiming.SamplesPerLine, frames = Math.Max(1, (int)(seconds * 25));
        long lowTotal = (long)frames * PalTiming.SamplesPerFrame;

        Console.WriteLine($"K = {plan.K} ({plan.WidebandRate / 1e6:0} MS/s), {channels} channel(s), {Environment.ProcessorCount} logical cores, " +
                          $"{seconds:0.#} s of signal per stage, block {blockLow} low-rate samples");
        Console.WriteLine($"{"stage",-44}{"threads",8}{"MS/s",10}{"x real time",13}");

        var cvbs = new float[blockLow];
        var re = new float[blockLow];
        var im = new float[blockLow];
        var wide = new float[blockLow * plan.K];

        var card = new CardSource("bars", 1, "BENCH", "BENCH");
        Report("card render (frames -> 25 fps = 1.0x)", 1, frames, 25e-6, () =>
        {
            for (int f = 0; f < frames; f++) card.GetFrame(f);
        });

        var encoder = new PalEncoder(new CardSource("bars", 1, "BENCH", "BENCH"));
        encoder.Read(cvbs);
        Report("PAL encoder incl. card (20 MS/s)", 1, lowTotal, 20, () => Loop(lowTotal, blockLow, n => encoder.Read(cvbs.AsSpan(0, n))));

        var vsb = new VsbModulator();
        Report($"VSB modulator, {vsb.Taps} complex taps (20 MS/s)", 1, lowTotal, 20, () => Loop(lowTotal, blockLow, n => vsb.Process(cvbs.AsSpan(0, n), re, im)));

        var placer = new SlotPlacer(plan, channels - 1);
        placer.SetGain(1.0 / channels);
        Report($"interp x{plan.K} + place (in 20 MS/s)", 1, lowTotal, 20, () =>
            Loop(lowTotal, blockLow, n => placer.Process(re.AsSpan(0, n), im.AsSpan(0, n), wide)));

        var single = new WidebandMux(plan, 1, 1);
        single.AddChannel(0, new PalEncoder(new CardSource("bars", 1, "BENCH", "BENCH")), "bench");
        Report("one whole channel: encode+VSB+place", 1, lowTotal * plan.K, plan.WidebandRate / 1e6, () => Loop(lowTotal, blockLow, n => single.Process(wide, n)));

        var mux = new WidebandMux(plan, channels, threads);
        for (int i = 0; i < channels; i++)
            mux.AddChannel(i, new PalEncoder(new CardSource(TestCards.Styles[i % TestCards.Styles.Length], i + 1, "BENCH", "BENCH")), "bench");
        mux.Process(wide, blockLow);
        Report($"mux total, {channels} channels", threads > 0 ? threads : Environment.ProcessorCount, lowTotal * plan.K, plan.WidebandRate / 1e6,
            () => Loop(lowTotal, blockLow, n => mux.Process(wide, n)));

        var codec = new SampleCodec();
        var bytes = new byte[wide.Length];
        Report("quantise u8 + TPDF dither", 1, lowTotal * plan.K, plan.WidebandRate / 1e6, () =>
            Loop(lowTotal, blockLow, n => codec.Encode(wide.AsSpan(0, n * plan.K), SampleFormat.U8, bytes)));
        Report("dequantise u8", 1, lowTotal * plan.K, plan.WidebandRate / 1e6, () =>
            Loop(lowTotal, blockLow, n => SampleCodec.Decode(bytes.AsSpan(0, n * plan.K), SampleFormat.U8, wide.AsSpan(0, n * plan.K))));

        var tuner = new Tuner(plan);
        tuner.SetReferenceCarrier(1.0 / channels);
        var ifOut = new float[tuner.MaxOutputFor(wide.Length)];
        Report($"tuner, {tuner.Taps} taps (input rate)", 1, lowTotal * plan.K, plan.WidebandRate / 1e6, () =>
            Loop(lowTotal, blockLow, n => tuner.Process(wide.AsSpan(0, n * plan.K), ifOut)));
        tuner.SetNoiseDbBelowCarrier(30);
        Report("tuner + receiver noise (input rate)", 1, lowTotal * plan.K, plan.WidebandRate / 1e6, () =>
            Loop(lowTotal, blockLow, n => tuner.Process(wide.AsSpan(0, n * plan.K), ifOut)));
        var words = new short[ifOut.Length];
        Report("IF float -> s16 (40 MS/s)", 1, lowTotal * 2, 40, () =>
            Loop(lowTotal, blockLow, n => SampleCodec.ToS16(ifOut.AsSpan(0, n * 2), words)));
        return 0;
    }

    private static void Loop(long total, int block, Action<int> body)
    {
        for (long done = 0; done < total; done += block) body((int)Math.Min(block, total - done));
    }

    private static void Report(string stage, int threads, long samples, double realTimeMsps, Action run)
    {
        var clock = Stopwatch.StartNew();
        run();
        double elapsed = clock.Elapsed.TotalSeconds, msps = samples / 1e6 / elapsed;
        Console.WriteLine($"{stage,-44}{threads,8}{msps,10:0.0}{msps / realTimeMsps,12:0.00}x");
    }
}
