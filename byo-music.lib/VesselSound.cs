using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Brutal.FmodApi;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.ByoMusicLib;

/// <summary>Owns a stream and channel; uses KSA's listener frame and SFX bus.</summary>
public sealed class VesselSound : IDisposable
{
    private Sound _sound;
    private Channel _channel;
    private readonly Stopwatch _loading = Stopwatch.StartNew();
    private readonly RepeatPlayback _repeat = new();
    private bool _ready;
    private bool _hasChannel;
    public Vehicle Target { get; }
    public string FileName { get; }
    public bool Repeat;
    public float GapSeconds;
    public float Volume = 0.5f;
    public float RangeMetres = 1000;
    public bool Finished { get; private set; }
    public string Status { get; private set; } = "Loading…";

    public VesselSound(Vehicle target, string fileName, bool repeat, float gap, float volume, float range)
    {
        if (target.IsDisposed) throw new InvalidOperationException("That vessel is no longer available.");
        string path = SoundLibrary.Files.FullPath(fileName);
        if (!SoundLibrary.Files.Supports(path) || !File.Exists(path)) throw new IOException("Choose an available OGG, WAV or MP3 file.");
        if (GameAudio.System.IsNull()) throw new InvalidOperationException("The game's audio system is not ready.");
        Target = target;
        FileName = fileName;
        Repeat = repeat;
        GapSeconds = gap;
        Volume = volume;
        RangeMetres = range;
        byte[] name = Encoding.UTF8.GetBytes(path + "\0");
        Check(GameAudio.System.TryCreateStream(name.AsSpan(), Mode._3d | Mode.NonBlocking | Mode.LoopOff,
            new CreateSoundExInfo(), out _sound), "open audio file");
    }

    public void Update(double dt)
    {
        if (Finished) return;
        try
        {
            if (Target.IsDisposed || VehicleProvider.FindVehicle(Target.Id) != Target)
            { Stop("Target no longer available"); return; }
            Volume = FiniteClamp(Volume, 0, 1, .5f);
            RangeMetres = FiniteClamp(RangeMetres, 1, 100000, 1000);
            GapSeconds = FiniteClamp(GapSeconds, 0, 3600, 0);
            if (!_ready)
            {
                Check(_sound.TryGetOpenState(out var state, out _, out _, out _), "decode audio file");
                if (state == OpenState.Error) throw new InvalidOperationException("The file could not be decoded. Use PCM WAV, MP3, or Ogg Vorbis.");
                if (state != OpenState.Ready)
                {
                    if (_loading.Elapsed.TotalSeconds > 15) throw new TimeoutException("Audio loading timed out.");
                    return;
                }
                _ready = true;
            }
            bool playing = false;
            if (_hasChannel)
            {
                Result result = _channel.TryIsPlaying(out var isPlaying);
                playing = result == Result.Ok && isPlaying;
                if (result != Result.Ok && result != Result.ErrInvalidHandle && result != Result.ErrChannelStolen)
                    Check(result, "check playback");
            }
            switch (_repeat.Advance(dt, playing, Repeat, GapSeconds))
            {
                case PlaybackAction.Play:
                    Start();
                    playing = true;
                    break;
                case PlaybackAction.Finish:
                    Stop("Finished");
                    return;
            }
            if (playing)
            {
                ApplySettings();
                Status = Repeat && GapSeconds == 0 ? "Looping" : "Playing";
            }
            else Status = $"Next play in {_repeat.Remaining:0.0}s";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"byo-music: {FileName}: {ex}");
            Stop($"Error: {ex.Message}");
        }
    }

    private void Start()
    {
        var group = ModLibrary.Get<ChannelGroupReference>("Sfx").ChannelGroup;
        Check(GameAudio.System.TryPlaySound(_sound, group, true, out _channel), "play sound");
        _hasChannel = true;
        ApplySettings(); // Configure position and gain while paused: no one-frame sound at the origin.
        Check(_channel.TrySetPaused(false), "start sound");
    }

    private void ApplySettings()
    {
        bool continuous = Repeat && GapSeconds == 0;
        Check(_channel.TrySetMode(Mode._3d | Mode._3dLinearRolloff | (continuous ? Mode.LoopNormal : Mode.LoopOff)), "set repeat");
        Check(_channel.TrySetLoopCount(continuous ? -1 : 0), "set repeat count");
        Check(_channel.TrySetVolume(Volume), "set volume");
        Check(_channel.TrySet3dMinMaxDistance(Math.Min(10, RangeMetres * .1f), RangeMetres), "set audible range");
        // Match stock SpatialAudio's camera-relative positions. Keep music pitch stable in warp.
        var spatial = new SpatialAudio(Target);
        Check(_channel.TrySet3dAttributes(spatial.PositionView(), spatial.VelocityView()), "follow vessel");
        Check(_channel.TrySet3dSpread(0), "position stereo files as a point source");
        Check(_channel.TrySet3dDopplerLevel(0), "keep playback pitch stable");
    }

    private static float FiniteClamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
    private static void Check(Result result, string action)
    {
        if (result != Result.Ok) throw new InvalidOperationException($"Could not {action} ({result}).");
    }

    public void Stop(string status = "Stopped")
    {
        if (_hasChannel) { _channel.TryStop(); _hasChannel = false; }
        if (!_sound.IsNull()) { _sound.TryRelease(); _sound = default; }
        Finished = true;
        Status = status;
    }
    public void Dispose() => Stop();
}
