using System.Runtime.CompilerServices;
using Brutal.GlfwApi;
using KSA;

namespace Brutal.GlfwApi
{
    public sealed class GlfwWindow { }
}

namespace Purr2D2.Runtime
{
    // Mirrors the supplied purr2d2 gate/ownership shape without initializing KSA or ImGui.
    internal static class GameHooks
    {
        internal static SaberRuntime Runtime = new();
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Overlay() { if (Program.DrawUI) Runtime.DrawSliceOverlay(); }
    }

    internal sealed class SaberRuntime
    {
        private enum HudPanel { Saber, Force, Kittens }
        internal bool Enabled = true, Ready = true, Disposed, Editor;
        internal bool MultipleWindows = true;
        internal readonly bool[] Detached = new bool[3];
        internal readonly bool[] Visible = { true, true, true };
        internal readonly int[] Draws = new int[3];
        internal int InputCalls, ResetCalls, Frames;
        internal bool Held;

        private bool OverlayVisible
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get => Enabled && Ready && !Disposed && Program.DrawUI && !Editor;
        }
        private bool BFL9KUiVisible
        {
            [MethodImpl(MethodImplOptions.NoInlining)] get => OverlayVisible && Visible[0];
        }
        private bool ForceUiVisible
        {
            [MethodImpl(MethodImplOptions.NoInlining)] get => OverlayVisible && Visible[1];
        }
        private bool KittenListVisible
        {
            [MethodImpl(MethodImplOptions.NoInlining)] get => OverlayVisible && Visible[2];
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool IsHudDetached(HudPanel panel)
        {
            if (!Ready || Disposed) throw new System.Exception("Native detachment queried before runtime readiness");
            return Detached[(int)panel] && MultipleWindows;
        }

        internal bool OverlayCapturesPointer
        {
            [MethodImpl(MethodImplOptions.NoInlining)] get => OverlayVisible;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void DrawSliceOverlay()
        {
            if (BFL9KUiVisible) Draws[0]++;
            if (ForceUiVisible) Draws[1]++;
            if (KittenListVisible) Draws[2]++;
        }
        [MethodImpl(MethodImplOptions.NoInlining)] internal void Frame(double dt) => Frames++;
        [MethodImpl(MethodImplOptions.NoInlining)] internal void OverlayFocusLost() { Held = false; ResetCalls++; }
        [MethodImpl(MethodImplOptions.NoInlining)] internal bool FilterOverlayMouseButton(GlfwWindow window) => Input();
        [MethodImpl(MethodImplOptions.NoInlining)] internal bool FilterOverlayCursor(GlfwWindow window) => Input();
        [MethodImpl(MethodImplOptions.NoInlining)] internal bool FilterOverlayScroll(GlfwWindow window) => Input();
        [MethodImpl(MethodImplOptions.NoInlining)] internal bool SuppressOverlayImGuiMouseButton(GlfwWindow window) => Input();
        [MethodImpl(MethodImplOptions.NoInlining)] internal bool SuppressOverlayImGuiScroll(GlfwWindow window) => Input();
        [MethodImpl(MethodImplOptions.NoInlining)] internal bool FilterSaberDropSpinKey(GlfwWindow window) => Input();
        [MethodImpl(MethodImplOptions.NoInlining)] internal bool FilterForceKey(GlfwWindow window) => Input();
        private bool Input() { InputCalls++; return true; }
    }
}
