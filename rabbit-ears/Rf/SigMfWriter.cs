using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace RabbitEars.Rf;

/// <summary>
/// Writes the tuner's IF as a SigMF pair: &lt;stem&gt;.sigmf-data (real int16 LE) + &lt;stem&gt;.sigmf-meta.
/// PALindrome derives the pair from the stem, which therefore must not contain a dot.
/// </summary>
public sealed class SigMfWriter : IDisposable
{
    private readonly FileStream _data;
    private readonly string _stem;
    private readonly double _sampleRate;
    private readonly string _description;
    private short[] _words = Array.Empty<short>();
    private long _count;

    public SigMfWriter(string stem, double sampleRate, string description)
    {
        if (Path.GetFileName(stem).Contains('.')) throw new ArgumentException("the SigMF stem must not contain a dot");
        _stem = stem;
        _sampleRate = sampleRate;
        _description = description;
        _data = new FileStream(stem + ".sigmf-data", FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        if (_words.Length < samples.Length) _words = new short[samples.Length];
        Span<short> words = _words.AsSpan(0, samples.Length);
        SampleCodec.ToS16(samples, words);
        _data.Write(MemoryMarshal.AsBytes(words));
        _count += samples.Length;
    }

    public void Dispose()
    {
        _data.Dispose();
        string rate = _sampleRate.ToString("0", CultureInfo.InvariantCulture);
        string description = _description.Replace("\\", "\\\\").Replace("\"", "\\\"");
        File.WriteAllText(_stem + ".sigmf-meta",
            "{\n  \"global\": {\n    \"core:datatype\": \"ri16_le\",\n" +
            $"    \"core:sample_rate\": {rate},\n    \"core:version\": \"1.2.0\",\n" +
            $"    \"core:description\": \"{description}\",\n    \"core:recorder\": \"rabbit-ears tune\"\n  }},\n" +
            "  \"captures\": [ { \"core:sample_start\": 0 } ],\n" +
            $"  \"annotations\": [ {{ \"core:sample_start\": 0, \"core:sample_count\": {_count} }} ]\n}}\n");
    }
}
