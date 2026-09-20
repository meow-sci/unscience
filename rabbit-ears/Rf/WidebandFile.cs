using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RabbitEars.Rf;

/// <summary>The JSON sidecar (&lt;data file&gt;.json) that describes a raw wideband capture.</summary>
public sealed class WidebandFileInfo
{
    [JsonPropertyName("format")] public string Format { get; set; } = "u8";
    [JsonPropertyName("sample_rate")] public double SampleRate { get; set; }
    [JsonPropertyName("k")] public int K { get; set; }
    [JsonPropertyName("reference_carrier_amplitude")] public double ReferenceCarrierAmplitude { get; set; }
    [JsonPropertyName("sample_count")] public long SampleCount { get; set; }
    [JsonPropertyName("channels")] public List<WidebandChannelInfo> Channels { get; set; } = new();

    public static string SidecarPath(string dataPath) => dataPath + ".json";

    public void Save(string dataPath) =>
        File.WriteAllText(SidecarPath(dataPath), JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));

    public static WidebandFileInfo Load(string dataPath)
    {
        string path = SidecarPath(dataPath);
        if (!File.Exists(path)) throw new FileNotFoundException($"wideband sidecar not found: {path}");
        return JsonSerializer.Deserialize<WidebandFileInfo>(File.ReadAllText(path))
               ?? throw new InvalidDataException($"empty sidecar: {path}");
    }
}

public sealed class WidebandChannelInfo
{
    [JsonPropertyName("slot")] public int Slot { get; set; }
    [JsonPropertyName("carrier_hz")] public double CarrierHz { get; set; }
    [JsonPropertyName("level_db")] public double LevelDb { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
}

/// <summary>Writes float wideband blocks as raw u8/s16 (TPDF-dithered); Dispose writes the sidecar.</summary>
public sealed class WidebandWriter : IDisposable
{
    private readonly FileStream _stream;
    private readonly SampleCodec _codec;
    private readonly SampleFormat _format;
    private readonly string _path;
    private byte[] _bytes = Array.Empty<byte>();

    public WidebandWriter(string path, SampleFormat format, WidebandFileInfo info, bool dither = true)
    {
        _path = path;
        _format = format;
        _codec = new SampleCodec(dither);
        Info = info;
        Info.Format = format == SampleFormat.U8 ? "u8" : "s16";
        _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);
    }

    public WidebandFileInfo Info { get; }

    public void Write(ReadOnlySpan<float> samples)
    {
        int byteCount = samples.Length * SampleCodec.BytesPerSample(_format);
        if (_bytes.Length < byteCount) _bytes = new byte[byteCount];
        _codec.Encode(samples, _format, _bytes.AsSpan(0, byteCount));
        _stream.Write(_bytes, 0, byteCount);
        Info.SampleCount += samples.Length;
    }

    public void Dispose()
    {
        _stream.Dispose();
        Info.Save(_path);
    }
}

/// <summary>Reads a raw wideband capture back as float blocks.</summary>
public sealed class WidebandReader : IDisposable
{
    private readonly FileStream _stream;
    private readonly SampleFormat _format;
    private byte[] _bytes = Array.Empty<byte>();

    public WidebandReader(string path)
    {
        Info = WidebandFileInfo.Load(path);
        _format = SampleCodec.ParseFormat(Info.Format);
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
    }

    public WidebandFileInfo Info { get; }

    /// <summary>Fills as much of <paramref name="samples"/> as the file still holds; returns the count (0 at the end).</summary>
    public int Read(Span<float> samples)
    {
        int size = SampleCodec.BytesPerSample(_format), want = samples.Length * size, got = 0;
        if (_bytes.Length < want) _bytes = new byte[want];
        while (got < want)
        {
            int n = _stream.Read(_bytes, got, want - got);
            if (n <= 0) break;
            got += n;
        }
        int count = got / size;
        SampleCodec.Decode(_bytes.AsSpan(0, count * size), _format, samples.Slice(0, count));
        return count;
    }

    /// <summary>Back to the first sample (looped playback).</summary>
    public void Rewind() => _stream.Seek(0, SeekOrigin.Begin);

    public void Dispose() => _stream.Dispose();
}
