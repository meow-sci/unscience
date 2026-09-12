using System;
namespace MeowSci.PyroLib;

/// <summary>Session-only on/off gating, sampled from KSA's simulation clock.</summary>
public sealed class PlumeCycle
{
    public float OnSeconds = 1;
    public float OffSeconds = 1;
    public bool Running { get; private set; }
    public bool IsOn { get; private set; } = true;
    public double RemainingSeconds { get; private set; }
    private double _startTime;
    private double _lastTime;

    public void Restart(double simulationTime)
    {
        Sanitize();
        if (!double.IsFinite(simulationTime)) return;
        Running = true;
        IsOn = true;
        _startTime = _lastTime = simulationTime;
        RemainingSeconds = OnSeconds;
    }
    public void Stop() { Running = false; IsOn = true; RemainingSeconds = 0; }

    /// <summary>Resume a saved cycle at the reconstructed simulation time without replaying elapsed cycles.</summary>
    public void RestorePhase(double simulationTime, bool isOn, double remainingSeconds)
    {
        Sanitize();
        if (!double.IsFinite(simulationTime) || !double.IsFinite(remainingSeconds)) return;
        double duration = isOn ? OnSeconds : OffSeconds;
        double phase = (isOn ? 0 : OnSeconds) + duration - Math.Clamp(remainingSeconds, 0, duration);
        Running = true;
        _startTime = simulationTime - phase;
        _lastTime = simulationTime;
        Update(simulationTime);
    }

    public void Update(double simulationTime)
    {
        if (!Running || !double.IsFinite(simulationTime)) return;
        Sanitize();
        if (simulationTime < _lastTime) { Restart(simulationTime); return; }
        _lastTime = simulationTime;
        // Modulo handles a long frame or time warp without an unbounded transition loop.
        double phase = (simulationTime - _startTime) % ((double)OnSeconds + OffSeconds);
        IsOn = phase < OnSeconds;
        RemainingSeconds = IsOn ? OnSeconds - phase : OnSeconds + (double)OffSeconds - phase;
    }

    private void Sanitize()
    {
        OnSeconds = float.IsFinite(OnSeconds) ? Math.Clamp(OnSeconds, .05f, 3600) : 1;
        OffSeconds = float.IsFinite(OffSeconds) ? Math.Clamp(OffSeconds, .05f, 3600) : 1;
    }
}
