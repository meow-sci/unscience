using System;
namespace MeowSci.ByoMusicLib;

internal enum PlaybackAction { None, Play, Finish }

/// <summary>Real-time gaps start when a completed play is observed, not when it began.</summary>
internal sealed class RepeatPlayback
{
    private bool _started;
    private bool _waiting;
    private double _lastGap;
    public double Remaining { get; private set; }
    public PlaybackAction Advance(double dt, bool playing, bool repeat, double gap)
    {
        if (!_started) { _started = true; return PlaybackAction.Play; }
        if (playing) { _waiting = false; return PlaybackAction.None; }
        if (!repeat) return PlaybackAction.Finish;
        if (!_waiting) { _waiting = true; Remaining = Math.Max(0, gap); }
        else
        {
            // Editing the gap while waiting keeps elapsed silence rather than restarting it.
            Remaining += gap - _lastGap;
            if (double.IsFinite(dt) && dt > 0) Remaining -= dt;
        }
        _lastGap = gap;
        if (Remaining > 0) return PlaybackAction.None;
        _waiting = false;
        return PlaybackAction.Play;
    }
}
