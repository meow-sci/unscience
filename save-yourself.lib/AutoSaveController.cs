using System;

namespace MeowSci.SaveYourselfLib;

/// <summary>
/// Timed auto-save engine. Counts real (wall-clock) seconds fed through <see cref="Tick"/> and, when the
/// interval elapses, asks the host to write a save named from the settings' prefix and the current time.
/// Game access is injected so the controller can be checked without KSA.
/// </summary>
public sealed class AutoSaveController
{
    private readonly Func<bool> _canSave;
    private readonly Func<string, bool> _makeSave;
    private readonly Func<DateTime> _now;
    private double _elapsedSeconds;

    /// <param name="canSave">True when the game would accept a save right now (world loaded, editor closed).</param>
    /// <param name="makeSave">Writes a save with the given name through the game's own path; true on success.</param>
    /// <param name="now">Local clock used for the timestamp suffix; defaults to <see cref="DateTime.Now"/>.</param>
    public AutoSaveController(Func<bool> canSave, Func<string, bool> makeSave, Func<DateTime>? now = null)
    {
        _canSave = canSave;
        _makeSave = makeSave;
        _now = now ?? (() => DateTime.Now);
    }

    public AutoSaveSettings Settings { get; private set; } = AutoSaveSettings.Default;

    /// <summary>Seconds left before the next timed save; zero while due or disabled.</summary>
    public double SecondsUntilNextSave => Settings.Enabled ? Math.Max(0, Settings.IntervalSeconds - _elapsedSeconds) : 0;

    /// <summary>True when a timed save is due but the game cannot accept one yet; it fires as soon as it can.</summary>
    public bool IsWaitingForGame { get; private set; }

    public string? LastSaveName { get; private set; }
    public DateTime? LastSaveTime { get; private set; }
    public string? LastError { get; private set; }
    public int SaveCount { get; private set; }

    /// <summary>The name the next save would get if written now.</summary>
    public string PreviewName() => AutoSaveNaming.BuildName(Settings.EffectivePrefix, _now());

    /// <summary>Applies normalised settings. Turning auto save on restarts the countdown.</summary>
    public void Configure(AutoSaveSettings settings)
    {
        var normalized = settings.Normalized();
        if (normalized.Enabled && !Settings.Enabled) _elapsedSeconds = 0;
        Settings = normalized;
    }

    /// <summary>Returns to defaults (disabled) and forgets the countdown and history. Used on scene teardown.</summary>
    public void Reset()
    {
        Settings = AutoSaveSettings.Default;
        _elapsedSeconds = 0;
        IsWaitingForGame = false;
        LastSaveName = null;
        LastSaveTime = null;
        LastError = null;
        SaveCount = 0;
    }

    /// <summary>Advances the countdown by real elapsed seconds and writes a save when it is due.</summary>
    public void Tick(double elapsedRealSeconds)
    {
        if (!Settings.Enabled)
        {
            _elapsedSeconds = 0;
            IsWaitingForGame = false;
            return;
        }
        if (elapsedRealSeconds > 0) _elapsedSeconds += elapsedRealSeconds;
        if (_elapsedSeconds < Settings.IntervalSeconds) return;
        if (!_canSave())
        {
            IsWaitingForGame = true;
            return;
        }
        IsWaitingForGame = false;
        _elapsedSeconds = 0;
        SaveNow();
    }

    /// <summary>Writes a save immediately, using the same naming as timed saves. Does not touch the countdown.</summary>
    public bool SaveNow()
    {
        string name = PreviewName();
        if (!_canSave())
        {
            LastError = "No world is loaded or the vehicle editor is open.";
            return false;
        }
        bool written;
        try
        {
            written = _makeSave(name);
            if (!written) LastError = $"KSA did not write '{name}'; see the game log.";
        }
        catch (Exception ex)
        {
            written = false;
            LastError = $"Saving '{name}' failed: {ex.Message}";
        }
        if (!written) return false;
        LastSaveName = name;
        LastSaveTime = _now();
        LastError = null;
        SaveCount++;
        return true;
    }
}
