using System;
using System.Runtime.CompilerServices;

namespace MeowSci.YouDidntSeeAnythingLib
{
    // No native context is initialized in this fixture.
    internal static class NativeWindowFilter
    {
        internal static bool IsReady = true;
        internal static void EnsureInstalled() { }
        internal static void Remove() { }
    }
    public static class VisibilitySettings
    {
        public static bool Enabled, DetachedOnly, HideGauges;
    }
}

namespace KSA
{
    public sealed class GaugeCanvas
    {
        public string Id { get; set; } = "";
        public static int Drawn, Final;
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnDrawUi() => Drawn++;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void OnDrawUiFinal() => Final++;
    }
    public static class Universe
    {
        public static int Drawn;
        [MethodImpl(MethodImplOptions.NoInlining)] public static void OnDrawUi() => Drawn++;
    }
    public sealed class Program
    {
        private static bool _drawUi;
        public static int SetCount, MenuDraws, Before, After, ConsoleDraws;
        public static Action? BeforeCallback;
        public static bool DrawUI
        {
            [MethodImpl(MethodImplOptions.NoInlining)] get => _drawUi;
            [MethodImpl(MethodImplOptions.NoInlining)] set { _drawUi = value; SetCount++; }
        }
        [MethodImpl(MethodImplOptions.NoInlining)] public void PrepareImGui(double dt) { }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnDrawUiConsole(double dt) => ConsoleDraws++;
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnDrawUiFrame(double dt) { Before++; BeforeCallback?.Invoke(); }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnDrawUiViewports(double dt) => After++;
        [MethodImpl(MethodImplOptions.NoInlining)] public void DrawMenuBar() => MenuDraws++;
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnDrawUiThreadSafe(double dt)
        {
            if (!DrawUI) return;
            DrawMenuBar();
            new GaugeCanvas { Id = "Navball" }.OnDrawUi();
            new GaugeCanvas { Id = "Custom" }.OnDrawUi();
            GaugeCanvas.OnDrawUiFinal();
            Universe.OnDrawUi();
        }
        [MethodImpl(MethodImplOptions.NoInlining)] public void OnFrame(double currentPlayerTime, double dtPlayer)
        {
            PrepareImGui(dtPlayer);
            if (DrawUI) { OnDrawUiFrame(dtPlayer); OnDrawUiViewports(dtPlayer); }
            if (DrawUI) OnDrawUiThreadSafe(dtPlayer);
            OnDrawUiConsole(dtPlayer);
        }
    }
}
