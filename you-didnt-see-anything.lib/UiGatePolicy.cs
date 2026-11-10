using System;
using System.Collections.Generic;

namespace MeowSci.YouDidntSeeAnythingLib;

/// <summary>Pure decisions used by the UI hooks.</summary>
public static class UiGatePolicy
{
    private static readonly HashSet<string> StockGauges = new(StringComparer.Ordinal)
    {
        "RendezvousControl", "BurnControl", "Burn", "EngineControl", "KittenFlightControl",
        "Telemetry", "Radiation", "AutopilotSettings", "Altitude", "Resources", "GameTime",
        "PitchYawRoll", "Sequence", "CrewPortraits", "Navball"
    };

    public static bool DrawUi(bool gameUi, bool enabled, bool patchReady) => gameUi || (enabled && patchReady);
    public static bool HiddenPass(bool gameUi, bool enabled, bool patchReady) => !gameUi && enabled && patchReady;
    public static bool DrawGauge(bool hiddenPass, bool hideGauges, string id) =>
        !hiddenPass || !hideGauges || !StockGauges.Contains(id);
}

/// <summary>Records whether this frame actually dispatched normal hidden UI callbacks.</summary>
public sealed class UiDispatchTracker
{
    private bool _dispatched;
    public bool Active { get; private set; }
    public bool FrameDraw { get; private set; }
    public bool BeginFrame(bool gameUi, bool enabled, bool patchReady)
    {
        bool draw = UiGatePolicy.DrawUi(gameUi, enabled, patchReady);
        FrameDraw = draw;
        _dispatched = !gameUi && draw;
        Active = _dispatched;
        return draw;
    }
    public bool Consume()
    {
        bool result = _dispatched;
        _dispatched = false;
        return result;
    }
    public void Reset() { _dispatched = false; Active = false; FrameDraw = false; }
}
