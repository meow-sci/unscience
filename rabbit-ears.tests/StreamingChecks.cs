using System;
using System.IO;
using RabbitEars.Dsp;
using RabbitEars.Pal;
using RabbitEars.Rf;
using RabbitEars.Sources;

namespace RabbitEars.Tests;

/// <summary>Block-size invariance of modulator, mux and tuner; u8/s16 file round trips; the NCO.</summary>
public static class StreamingChecks
{
    public static void Run()
    {
        Modulator();
        Mux();
        TunerBlocks();
        Files();
        Oscillator();
    }

    private static void Modulator()
    {
        Check.Section("block-size invariance: VSB modulator + slot placer");
        const int total = 50_000;
        float[] cvbs = EncoderChecks.Encode(new PalEncoder(new CardSource("hue", 2, "T", "T", TimeSpan.Zero)), total);
        var plan = new ChannelPlan(6);
        float[] reference = ModulateAndPlace(plan, cvbs, total);
        foreach (int block in new[] { 1, 7, 999, 4096 })
            Check.AtMost(MaxDifference(reference, ModulateAndPlace(plan, cvbs, block)), 0, $"block {block}: max difference vs one block");
    }

    private static float[] ModulateAndPlace(ChannelPlan plan, float[] cvbs, int block)
    {
        var modulator = new VsbModulator();
        var placer = new SlotPlacer(plan, 3);
        var output = new float[cvbs.Length * plan.K];
        var re = new float[block];
        var im = new float[block];
        for (int done = 0; done < cvbs.Length; done += block)
        {
            int n = Math.Min(block, cvbs.Length - done);
            modulator.Process(cvbs.AsSpan(done, n), re, im);
            placer.Process(re.AsSpan(0, n), im.AsSpan(0, n), output.AsSpan(done * plan.K, n * plan.K));
        }
        return output;
    }

    private static void Mux()
    {
        Check.Section("block-size invariance: 3-channel mux");
        float[] reference = RunMux(64_000);
        foreach (int block in new[] { 1280, 3333, 20_000 })
            Check.AtMost(MaxDifference(reference, RunMux(block)), 1e-7, $"block {block}: max difference vs one block");
    }

    private static float[] RunMux(int block)
    {
        const int total = 64_000;
        var plan = new ChannelPlan(6);
        var mux = new WidebandMux(plan, 3);
        string[] styles = { "bars", "grid", "rays" };
        for (int i = 0; i < 3; i++)
            mux.AddChannel(i * 2, new PalEncoder(new CardSource(styles[i], i + 1, "T", "T", TimeSpan.Zero)), styles[i], -3.0 * i);
        var output = new float[total * plan.K];
        var scratch = new float[block * plan.K];
        for (int done = 0; done < total; done += block)
        {
            int n = Math.Min(block, total - done);
            mux.Process(scratch, n);
            scratch.AsSpan(0, n * plan.K).CopyTo(output.AsSpan(done * plan.K));
        }
        return output;
    }

    private static void TunerBlocks()
    {
        Check.Section("block-size invariance: tuner (with noise, odd block sizes)");
        float[] wide = RunMux(64_000);
        float[] reference = RunTuner(wide, wide.Length);
        foreach (int block in new[] { 1, 2, 1000, 4097, 65_536 })
        {
            float[] other = RunTuner(wide, block);
            Check.That(other.Length == reference.Length && MaxDifference(reference, other) == 0,
                $"block {block}: {other.Length} outputs, bit-identical");
        }
    }

    private static float[] RunTuner(float[] wide, int block)
    {
        var tuner = new Tuner(new ChannelPlan(6)) { FrequencyHz = 22.3e6 };
        tuner.SetReferenceCarrier(1.0 / 3);
        tuner.SetNoiseDbBelowCarrier(25);
        var output = new float[tuner.MaxOutputFor(wide.Length) + 8];
        int written = 0;
        for (int done = 0; done < wide.Length; done += block)
            written += tuner.Process(wide.AsSpan(done, Math.Min(block, wide.Length - done)), output.AsSpan(written));
        return output.AsSpan(0, written).ToArray();
    }

    private static void Files()
    {
        Check.Section("wideband file round trip");
        string dir = Path.Combine(Path.GetTempPath(), "rabbit-ears-tests-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            var rng = new FastRng(42);
            var samples = new float[100_003];
            for (int i = 0; i < samples.Length; i++) samples[i] = (float)(rng.NextDouble() * 2 - 1);
            samples[0] = 1f;
            samples[1] = -1f;
            samples[2] = 1.7f;                                              // clips

            foreach ((SampleFormat format, double lsb) in new[] { (SampleFormat.U8, 1 / 127.0), (SampleFormat.S16, 1 / 32767.0) })
                foreach (bool dither in new[] { false, true })
                {
                    string path = Path.Combine(dir, $"rt_{format}_{dither}.bin");
                    var info = new WidebandFileInfo { SampleRate = 120e6, K = 6, ReferenceCarrierAmplitude = 1.0 / 6 };
                    info.Channels.Add(new WidebandChannelInfo { Slot = 2, CarrierHz = 22e6, LevelDb = -3, Name = "n", Source = "card:bars" });
                    using (var writer = new WidebandWriter(path, format, info, dither))
                    {
                        writer.Write(samples.AsSpan(0, 40_000));
                        writer.Write(samples.AsSpan(40_000));
                    }
                    using var reader = new WidebandReader(path);
                    var back = new float[samples.Length + 10];
                    int got = reader.Read(back.AsSpan(0, 70_000));
                    got += reader.Read(back.AsSpan(got));
                    double worst = 0, bias = 0;
                    for (int i = 3; i < samples.Length; i++)
                    {
                        worst = Math.Max(worst, Math.Abs(back[i] - samples[i]));
                        bias += back[i] - samples[i];
                    }
                    string label = $"{format} {(dither ? "dithered" : "plain")}";
                    Check.That(got == samples.Length && reader.Info.SampleCount == samples.Length, $"{label}: {got} samples back, sidecar agrees");
                    Check.AtMost(worst / lsb, dither ? 1.51 : 0.51, $"{label}: worst error (LSB)");
                    Check.AtMost(Math.Abs(bias / samples.Length) / lsb, 0.02, $"{label}: mean error (LSB)");
                    Check.Near(back[2], 1.0, 2 * lsb, $"{label}: over-range input clips to full scale");
                    Check.That(new FileInfo(path).Length == samples.Length * SampleCodec.BytesPerSample(format), $"{label}: file size");
                    Check.That(reader.Info.K == 6 && reader.Info.Channels.Count == 1 && reader.Info.Channels[0].CarrierHz == 22e6
                               && reader.Info.Channels[0].LevelDb == -3 && Math.Abs(reader.Info.ReferenceCarrierAmplitude - 1.0 / 6) < 1e-12,
                        $"{label}: sidecar fields survive");
                }

            string stem = Path.Combine(dir, "if_out");
            using (var sigmf = new SigMfWriter(stem, 40e6, "test \"quoted\""))
                sigmf.Write(new[] { 0f, 0.5f, -0.5f, 2f, -2f });
            byte[] data = File.ReadAllBytes(stem + ".sigmf-data");
            Check.That(data.Length == 10 && BitConverter.ToInt16(data, 2) == 16384 && BitConverter.ToInt16(data, 6) == 32767 && BitConverter.ToInt16(data, 8) == -32768,
                "SigMF data is clipped int16 LE");
            using var meta = System.Text.Json.JsonDocument.Parse(File.ReadAllText(stem + ".sigmf-meta"));
            var global = meta.RootElement.GetProperty("global");
            Check.That(global.GetProperty("core:datatype").GetString() == "ri16_le" && global.GetProperty("core:sample_rate").GetDouble() == 40e6,
                "SigMF meta is valid JSON with ri16_le at 40 MS/s");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void Oscillator()
    {
        Check.Section("NCO");
        var nco = new Nco();
        nco.SetCyclesPerStep(-0.05);
        var cos = new float[1000];
        var sin = new float[1000];
        nco.Next(cos, sin);
        nco.Next(cos, sin);
        double worst = 0;
        for (int i = 0; i < 1000; i++)
        {
            double angle = -2 * Math.PI * 0.05 * (1000 + i);
            worst = Math.Max(worst, Math.Max(Math.Abs(cos[i] - Math.Cos(angle)), Math.Abs(sin[i] - Math.Sin(angle))));
        }
        Check.AtMost(worst, 2e-6, "cos/sin error of a -2 MHz LO after 2000 steps");
    }

    private static double MaxDifference(float[] a, float[] b)
    {
        if (a.Length != b.Length) return double.PositiveInfinity;
        double worst = 0;
        for (int i = 0; i < a.Length; i++) worst = Math.Max(worst, Math.Abs(a[i] - b[i]));
        return worst;
    }
}
