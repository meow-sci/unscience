using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using RabbitEars.Pal;

namespace RabbitEars.Sources;

public enum FfmpegInputKind { File, Lavfi, Image, Camera }

/// <summary>
/// Raw RGB frames from an ffmpeg child process, already scaled to the encoder grid (4:3, letter-boxed).
/// Sequential mode hands out consecutive frames (offline muxing at any speed); paced mode lets ffmpeg run in
/// real time (-re) and always returns the newest frame (live muxing).
/// </summary>
public sealed class FfmpegSource : IFrameSource
{
    private const int Width = PalTiming.ActiveSamples, Height = PalTiming.ActiveRows, FrameBytes = Width * Height * 3;
    private readonly Process _process;
    private readonly Stream _stdout;
    private readonly bool _paced;
    private readonly object _gate = new();
    private byte[] _current = new byte[FrameBytes];
    private byte[] _spare = new byte[FrameBytes];
    private byte[] _latest = new byte[FrameBytes];
    private bool _latestFresh;
    private volatile bool _ended;

    public FfmpegSource(FfmpegInputKind kind, string spec, bool paced, string? name = null)
    {
        Name = name ?? $"{kind.ToString().ToLowerInvariant()}:{Path.GetFileName(spec)}";
        _paced = paced;
        var info = new ProcessStartInfo(FindFfmpeg())
        {
            RedirectStandardOutput = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
        };
        foreach (string arg in BuildArguments(kind, spec, paced)) info.ArgumentList.Add(arg);
        _process = Process.Start(info) ?? throw new InvalidOperationException("could not start ffmpeg");
        _stdout = _process.StandardOutput.BaseStream;
        if (paced) new Thread(Pump) { IsBackground = true, Name = "ffmpeg-pump" }.Start();
    }

    public string Name { get; }

    public static string FindFfmpeg()
    {
        string? overridePath = Environment.GetEnvironmentVariable("RABBIT_EARS_FFMPEG");
        if (!string.IsNullOrEmpty(overridePath)) return overridePath;
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Length == 0) continue;
            string candidate = Path.Combine(dir, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
            if (File.Exists(candidate)) return candidate;
        }
        return "/opt/homebrew/bin/ffmpeg";
    }

    private static List<string> BuildArguments(FfmpegInputKind kind, string spec, bool paced)
    {
        var args = new List<string> { "-nostdin", "-loglevel", "error" };
        if (paced && kind != FfmpegInputKind.Camera) args.Add("-re");
        switch (kind)
        {
            case FfmpegInputKind.File:
                args.AddRange(new[] { "-stream_loop", "-1", "-i", spec });
                break;
            case FfmpegInputKind.Image:
                args.AddRange(new[] { "-loop", "1", "-framerate", "25", "-i", spec });
                break;
            case FfmpegInputKind.Lavfi:
                string sized = spec.Contains("size=") ? spec : spec + (spec.Contains('=') ? ":" : "=") + "size=768x576:rate=25";
                args.AddRange(new[] { "-f", "lavfi", "-i", sized });
                break;
            case FfmpegInputKind.Camera:
                args.AddRange(new[] { "-f", "avfoundation", "-framerate", "30", "-i", spec + ":none" });
                break;
        }
        string filter = "fps=25,scale=768:576:force_original_aspect_ratio=decrease," +
                        $"pad=768:576:(ow-iw)/2:(oh-ih)/2:black,scale={Width}:{Height}";
        args.AddRange(new[] { "-an", "-vf", filter, "-pix_fmt", "rgb24", "-f", "rawvideo", "-" });
        return args;
    }

    public VideoFrame GetFrame(long frameIndex)
    {
        if (_paced)
        {
            lock (_gate)
            {
                if (_latestFresh)
                {
                    (_current, _latest) = (_latest, _current);
                    _latestFresh = false;
                }
            }
        }
        else if (!_ended && !ReadFrame(_current))
        {
            _ended = true;
            Console.WriteLine($"rabbit-ears: ffmpeg source '{Name}' ended; holding the last frame");
        }
        return new VideoFrame(_current, Width, Height);
    }

    private void Pump()
    {
        while (ReadFrame(_spare))
        {
            lock (_gate)
            {
                (_spare, _latest) = (_latest, _spare);
                _latestFresh = true;
            }
        }
        _ended = true;
        Console.WriteLine($"rabbit-ears: ffmpeg source '{Name}' ended");
    }

    private bool ReadFrame(byte[] into)
    {
        int got = 0;
        try
        {
            while (got < FrameBytes)
            {
                int n = _stdout.Read(into, got, FrameBytes - got);
                if (n <= 0) return false;
                got += n;
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            return false;
        }
        return true;
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited) _process.Kill();
        }
        catch (InvalidOperationException)
        {
        }
        _process.Dispose();
    }
}
