using System;
using System.Text.Json;
using System.Linq;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.YouDidntSeeAnythingLib;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

foreach (bool gameUi in new[] { false, true })
foreach (bool enabled in new[] { false, true })
foreach (bool patched in new[] { false, true })
{
    Check(UiGatePolicy.DrawUi(gameUi, enabled, patched) == (gameUi || (enabled && patched)), "UI gate policy");
    Check(UiGatePolicy.HiddenPass(gameUi, enabled, patched) == (!gameUi && enabled && patched), "Hidden pass policy");
}
Check(!UiGatePolicy.DrawGauge(true, true, "Navball"), "Stock gauge hidden");
Check(UiGatePolicy.DrawGauge(true, true, "custom-gauge"), "Custom gauge retained");
Check(UiGatePolicy.DrawGauge(false, true, "Navball"), "Normal UI retains gauges");

foreach (bool detached in new[] { false, true })
foreach (bool hideGauges in new[] { false, true })
foreach (bool main in new[] { false, true })
foreach (bool stock in new[] { false, true })
    Check(NativeWindowFilterPolicy.Allows(detached, hideGauges, main, stock) ==
        ((!detached || !main) && (!hideGauges || !stock)), "Window filtering truth table");

foreach (bool ownerExcluded in new[] { false, true })
foreach (bool allowedPresent in new[] { false, true })
    Check(NativeWindowFilterPolicy.HidePlatformOwner(ownerExcluded, allowedPresent) ==
        (ownerExcluded && !allowedPresent), "Platform owner visibility truth table");

using (var probe = KsaAssemblyProbe.TryOpen("hidden UI gate checks"))
{
    if (probe != null)
    {
        var frame = probe.CallsOf("KSA.Program", "OnFrame").Single();
        Check(frame.Count(c => c == "KSA.Program::get_DrawUI") == 2, "OnFrame has exactly two UI gates");
        Check(frame.IndexOf("KSA.Program::OnDrawUiFrame") > frame.IndexOf("KSA.Program::get_DrawUI"), "Frame UI follows first gate");
        Check(frame.IndexOf("KSA.Program::OnDrawUiViewports") > frame.IndexOf("KSA.Program::OnDrawUiFrame"), "Viewport callbacks follow frame callbacks");
        Check(frame.IndexOf("KSA.Program::OnDrawUiConsole") > frame.IndexOf("KSA.Program::OnDrawUiThreadSafe"), "Fallback follows UI callbacks");
        var threadSafe = probe.CallsOf("KSA.Program", "OnDrawUiThreadSafe").Single();
        Check(threadSafe.Count(c => c == "KSA.Program::get_DrawUI") == 1, "Thread-safe UI has one gate");
    }
}

var dispatch = new UiDispatchTracker();
Check(dispatch.BeginFrame(false, true, true), "Hidden callbacks dispatched");
// A checkbox can disable the feature while the callbacks are already running.
Check(dispatch.FrameDraw, "Mid-frame setting changes do not split the UI phases");
Check(dispatch.Consume(), "Mid-frame disable must not duplicate the update");
Check(!dispatch.Consume(), "Dispatch marker is consumed once");
Check(!dispatch.BeginFrame(false, false, true) && !dispatch.Consume(), "Disabled hidden UI uses fallback");
Check(dispatch.BeginFrame(true, true, true) && !dispatch.Consume(), "Visible UI never claims hidden dispatch");
Check(dispatch.BeginFrame(false, true, true), "Next hidden frame dispatches");
dispatch.Reset();
Check(!dispatch.Consume(), "Unload clears marker");

var coordinator = new SceneSaveCoordinator(new[] { VisibilitySettings.CreateParticipant() });
VisibilitySettings.Reset();
Check(!VisibilitySettings.Enabled && !VisibilitySettings.DetachedOnly && !VisibilitySettings.HideGauges, "Defaults must be off");
VisibilitySettings.Enabled = VisibilitySettings.DetachedOnly = VisibilitySettings.HideGauges = true;
var saved = coordinator.Capture();
coordinator.PrepareLoad(saved);
coordinator.ResetWorld();
Check(!VisibilitySettings.Enabled && !VisibilitySettings.DetachedOnly && !VisibilitySettings.HideGauges, "Reset before native reconstruction");
coordinator.RestoreWorld();
coordinator.FinishLoad();
Check(VisibilitySettings.Enabled && VisibilitySettings.DetachedOnly && VisibilitySettings.HideGauges, "All options round-trip");
VisibilitySettings.Reset();
coordinator.PrepareLoad(saved);
coordinator.ResetWorld();
coordinator.RestoreWorld();
coordinator.FinishLoad();
Check(VisibilitySettings.Enabled && VisibilitySettings.DetachedOnly && VisibilitySettings.HideGauges, "Repeated loads rebind");
coordinator.PrepareLoad(null);
coordinator.ResetWorld();
coordinator.RestoreWorld();
coordinator.FinishLoad();
Check(!VisibilitySettings.Enabled && !VisibilitySettings.DetachedOnly && !VisibilitySettings.HideGauges, "Vanilla save resets settings");
var malformed = coordinator.Capture();
malformed.Features["you-didnt-see-anything"].State = JsonDocument.Parse("{\"Enabled\":\"invalid\"}").RootElement.Clone();
coordinator.PrepareLoad(malformed);
coordinator.ResetWorld();
coordinator.RestoreWorld();
coordinator.FinishLoad();
Check(!VisibilitySettings.Enabled && coordinator.RetainedCount == 1, "Malformed record remains preserved");
Console.WriteLine("PASS: hidden UI settings persistence");
