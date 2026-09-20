using System;
using RabbitEars.Pal;
using RabbitEars.Sources;

namespace RabbitEars.Tests;

/// <summary>Tiny assertion helper: counts failures, prints each check, never throws.</summary>
public static class Check
{
    public static int Failures { get; private set; }
    public static int Passed { get; private set; }

    public static void That(bool condition, string what)
    {
        if (condition)
        {
            Passed++;
            Console.WriteLine($"  ok    {what}");
            return;
        }
        Failures++;
        Console.WriteLine($"  FAIL  {what}");
    }

    public static void Near(double actual, double expected, double tolerance, string what) =>
        That(Math.Abs(actual - expected) <= tolerance, $"{what}: {actual:0.#####} (expected {expected:0.#####} ± {tolerance:0.#####})");

    public static void AtMost(double actual, double limit, string what) =>
        That(actual <= limit, $"{what}: {actual:0.###} (limit {limit:0.###})");

    public static void Section(string name) => Console.WriteLine($"\n== {name}");
}

/// <summary>A flat colour field.</summary>
public sealed class SolidSource : IFrameSource
{
    private readonly byte[] _rgb;
    private readonly int _width, _height;

    public SolidSource(byte r, byte g, byte b, int width = PalTiming.ActiveSamples, int height = PalTiming.ActiveRows)
    {
        _width = width;
        _height = height;
        _rgb = new byte[width * height * 3];
        for (int i = 0; i < _rgb.Length; i += 3)
        {
            _rgb[i] = r;
            _rgb[i + 1] = g;
            _rgb[i + 2] = b;
        }
    }

    public string Name => "solid";
    public int FramesServed { get; private set; }

    public VideoFrame GetFrame(long frameIndex)
    {
        FramesServed++;
        return new VideoFrame(_rgb, _width, _height);
    }

    public void Dispose()
    {
    }
}

/// <summary>Sync-less white-noise "video" (0 … 0.7 V): a flat spectrum for filter measurements.</summary>
public sealed class NoiseCvbsSource : ICvbsSource
{
    private Dsp.FastRng _rng;

    public NoiseCvbsSource(ulong seed) => _rng = new Dsp.FastRng(seed);

    public void Read(Span<float> cvbs)
    {
        for (int i = 0; i < cvbs.Length; i++) cvbs[i] = (float)(_rng.NextDouble() * 0.7);
    }
}
