using HarmonyLib;
using KSA;
using Brutal.GlfwApi;
using Brutal.Numerics;

namespace Purr2D2.Runtime
{
    internal sealed class SaberRuntime
    {
        internal int Updates;
        internal int Resets;
        internal int UnsafeResets;
        internal double Delta;
        internal void BeforePhysics(double dt) { Updates++; Delta = dt; Universe.Events.Add("purr settled"); }
        internal void ResetWorld()
        {
            Resets++;
            if (JobSystems.WorkersBusy) UnsafeResets++;
            Universe.Events.Add("purr reset");
        }
        internal void Frame(double dt) { }
        internal void DrawSliceOverlay() { }
        internal void DrawMenu() { }
        internal bool FilterOverlayMouseButton(GlfwWindow w, GlfwMouseButton b, GlfwButtonAction a) => false;
        internal bool FilterOverlayCursor(GlfwWindow w) => false;
        internal bool FilterOverlayScroll(GlfwWindow w, double2 o) => false;
        internal bool FilterForceKey(GlfwWindow w, GlfwKey k, GlfwKeyAction a) => false;
        internal bool FilterSpaceSomersaultKey(KittenEva owner, GlfwKeyEvent keyEvent) => false;
        internal bool SuppressOverlayImGuiMouseButton(GlfwWindow w, GlfwMouseButton b, GlfwButtonAction a) => false;
        internal bool SuppressOverlayImGuiScroll(GlfwWindow w) => false;
        internal void OverlayFocusLost() { }
        internal void CancelPendingSpaceSomersault() { }
        internal void Shutdown() { }
        internal static void Report(string message, Exception error) => Universe.Events.Add("purr report " + error.Message);
    }
}

namespace Purr2D2.Slicing
{
    internal enum SliceFrameSegment { VehicleWait, DirtyDerived, ResourceFlush, VehiclePreparationAndDispatch }
    internal static class SliceFrameDiagnostics
    {
        internal static void EnterFrame() => Universe.Events.Add("purr enter");
        internal static long StartSegment() => 1;
        internal static void EndSegment(SliceFrameSegment segment, long start) => Universe.Events.Add("purr end " + segment);
        internal static void WorkerJoined(long start, long end) => Universe.Events.Add("purr worker joined");
        internal static void HookReturned() => Universe.Events.Add("purr returned");
        internal static void PumpSummaries() { }
        internal static void Reset() => Universe.Events.Add("purr diagnostics reset");
    }
    internal static class SliceWorkerDiagnostics { internal static bool HasPending => true; }
    internal static class SliceWorkerHooks { internal static void Install(Harmony harmony) { } }
}

namespace Purr2D2.Cutting
{
    internal static class PartSliceTargeting { internal static void RepairLegacyFragmentPicker(Part part) { } }
}

namespace Brutal.GlfwApi
{
    internal sealed class GlfwWindow { }
    internal enum GlfwMouseButton { Left }
    internal enum GlfwButtonAction { Press, Release }
    internal enum GlfwModifier { None }
    internal enum GlfwKey { Space }
    internal enum GlfwKeyAction { Press, Release }
    internal enum GlfwFocusEvent { GainedFocus, LostFocus }
    internal readonly record struct GlfwKeyEvent(GlfwKey Key, GlfwKeyAction Action);
}
namespace Brutal.Numerics { internal readonly record struct double2(double X, double Y); }
namespace RenderCore.Input
{
    internal static class ImGuiBackendGlfwImpl
    {
        internal static void OnMouseButton(GlfwWindow window, GlfwMouseButton button, GlfwButtonAction action, GlfwModifier modifiers) { }
        internal static void OnScroll(GlfwWindow w, double2 o) { }
    }
}
