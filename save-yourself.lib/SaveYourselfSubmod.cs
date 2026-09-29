using System;
using System.Diagnostics;
using MeowSci.KsaAbstractions;

namespace MeowSci.SaveYourselfLib;

/// <summary>
/// Submod for save-yourself: timed auto saves through KSA's built-in save path, named
/// <c>&lt;prefix&gt;_YYYYMMDDTHHMMSS</c>. Wall-clock timing, independent of time warp and pause.
/// </summary>
public sealed partial class SaveYourselfSubmod : ISubmod
{
    public string Name => "Save Yourself";
    public string Tooltip => "Auto save: every N seconds write a new KSA save named <prefix>_YYYYMMDDTHHMMSS through the game's own save path.";

    public static SaveYourselfSubmod? Instance { get; private set; }

    /// <summary>Auto-save engine; other mods may configure or trigger it.</summary>
    public AutoSaveController Controller { get; } = new(
        () => GameSaveProvider.CanSaveNow,
        name => WriteSave(name));

    private readonly Stopwatch _clock = new();

    public void Initialize()
    {
        Instance = this;
        _clock.Restart();
    }

    public void Update(double dt)
    {
        // Real seconds since the previous frame, so warp, pause and hitches do not skew the interval.
        double elapsed = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        Controller.Tick(elapsed);
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##save_yourself_content");
        RenderAutoSavePanel();
        SubmodUI.EndContentArea();
    }

    public void Dispose()
    {
        Controller.Reset();
        if (Instance == this) Instance = null;
    }

    private static bool WriteSave(string name)
    {
        bool written = GameSaveProvider.TryMakeSave(name, out string accepted);
        Console.WriteLine(written
            ? $"save-yourself: wrote save '{accepted}'."
            : $"save-yourself: KSA did not write save '{name}'.");
        return written;
    }
}
