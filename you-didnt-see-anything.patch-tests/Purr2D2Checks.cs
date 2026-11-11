using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Brutal.GlfwApi;
using HarmonyLib;
using MeowSci.YouDidntSeeAnythingLib;
using Purr2D2.Runtime;
using Game = KSA.Program;

namespace YouDidntSeeAnythingPatchTests;

internal static class Purr2D2Checks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    internal static void Run()
    {
        var runtime = GameHooks.Runtime;
        var detachedWindow = new GlfwWindow();
        Array.Clear(runtime.Draws);
        Game.DrawUI = true;
        VisibilitySettings.Enabled = true;
        runtime.Frame(0);
        GameHooks.Overlay();
        Check(runtime.Draws.All(n => n == 1), "Ordinary purr2d2 HUD stays visible");

        Game.DrawUI = false;
        int setters = Game.SetCount;
        foreach (bool detachedOnly in new[] { false, true })
        for (int selected = 0; selected < 3; selected++)
        {
            VisibilitySettings.DetachedOnly = detachedOnly;
            Array.Clear(runtime.Detached);
            Array.Clear(runtime.Draws);
            runtime.Detached[selected] = true;
            GameHooks.Overlay();
            for (int panel = 0; panel < 3; panel++)
                Check(runtime.Draws[panel] == (panel == selected ? 1 : 0), "Only detached purr2d2 panels submit during F2");
        }
        Check(!Game.DrawUI && Game.SetCount == setters, "purr2d2 compatibility never writes F2 state");
        Array.Clear(runtime.Draws);
        new Game().OnDrawUiThreadSafe(0);
        Check(runtime.Draws[2] == 1, "Existing purr2d2 Harmony postfix submits detached controls through the patched game wrapper");
        runtime.Visible[2] = false;
        Array.Clear(runtime.Draws);
        GameHooks.Overlay();
        Check(runtime.Draws.All(n => n == 0), "purr2d2 panel visibility remains authoritative");
        runtime.Visible[2] = true;

        foreach (Action disable in new Action[] { () => runtime.Enabled = false, () => runtime.Ready = false,
            () => runtime.Disposed = true, () => runtime.Editor = true, () => runtime.MultipleWindows = false,
            () => VisibilitySettings.Enabled = false, () => NativeWindowFilter.IsReady = false })
        {
            disable();
            Array.Clear(runtime.Draws);
            GameHooks.Overlay();
            Check(runtime.Draws.All(n => n == 0), "Original runtime guards and unavailable retention remain closed");
            runtime.Enabled = runtime.Ready = runtime.MultipleWindows = NativeWindowFilter.IsReady = VisibilitySettings.Enabled = true;
            runtime.Disposed = runtime.Editor = false;
        }

        Func<GlfwWindow, bool>[] inputRoutes = { runtime.FilterOverlayMouseButton, runtime.FilterOverlayCursor,
            runtime.FilterOverlayScroll, runtime.SuppressOverlayImGuiMouseButton, runtime.SuppressOverlayImGuiScroll,
            runtime.FilterSaberDropSpinKey, runtime.FilterForceKey };
        foreach (var route in inputRoutes)
        {
            int count = runtime.InputCalls;
            Check(!route(Game.GetWindow()) && runtime.InputCalls == count, "Hidden main-window HUD input passes to the game");
            Check(route(detachedWindow) && runtime.InputCalls == count + 1, "Detached custom input still reaches purr2d2");
        }
        Check(!runtime.OverlayCapturesPointer, "Hidden HUD cannot block the main game picker");

        runtime.Held = true;
        int resets = runtime.ResetCalls;
        runtime.Frame(0);
        Check(!runtime.Held && runtime.ResetCalls == resets + 1, "F2 transition releases old gesture ownership");
        runtime.Held = true;
        runtime.Frame(0);
        Check(runtime.Held && runtime.ResetCalls == resets + 1, "Detached held gestures survive subsequent hidden frames");
        VisibilitySettings.Enabled = false;
        runtime.Frame(0);
        Check(!runtime.Held && runtime.ResetCalls == resets + 2, "Disabling retention releases custom input");

        Game.DrawUI = true;
        runtime.Frame(0);
        Check(inputRoutes.All(route => route(Game.GetWindow())) && runtime.OverlayCapturesPointer,
            "Restoring F2 restores ordinary purr2d2 input");
        Console.WriteLine("PASS: purr2d2 gates, detached panels, input ownership and transitions");
    }

    internal static void CheckRemovalAndRollback(Harmony harmony, Harmony foreign)
    {
        Game.DrawUI = false;
        VisibilitySettings.Enabled = true;
        var runtime = GameHooks.Runtime;
        Check(!runtime.Held, "Unload releases detached gesture ownership before removing hooks");
        Array.Clear(runtime.Draws);
        GameHooks.Overlay();
        Check(runtime.Draws.All(n => n == 0), "Removal restores purr2d2's native F2 gate");
        var overlay = AccessTools.Method(typeof(GameHooks), nameof(GameHooks.Overlay));
        var extraGate = AccessTools.Method(typeof(Purr2D2Checks), nameof(AddExtraGate));
        foreign.Patch(overlay, transpiler: new HarmonyMethod(extraGate));
        HiddenUiVisibilityPatches.Apply(harmony);
        Check(HiddenUiVisibilityPatches.IsApplied, "Incompatible optional purr2d2 does not disable window filtering");
        var patches = Harmony.GetPatchInfo(overlay)!;
        Check(patches.Transpilers.Any(p => p.owner == foreign.Id) && !patches.Transpilers.Any(p => p.owner == harmony.Id),
            "purr2d2 changed-gate rollback preserves foreign patches");
        foreach (var target in Harmony.GetAllPatchedMethods())
            if (target.DeclaringType?.Namespace == "Purr2D2.Runtime")
                Check(!Harmony.GetPatchInfo(target)!.Owners.Contains(harmony.Id), "All partial purr2d2 hooks are rolled back");
        HiddenUiVisibilityPatches.Remove(harmony);
        foreign.Unpatch(overlay, extraGate);
        Console.WriteLine("PASS: optional purr2d2 rollback and targeted removal");
    }

    private static IEnumerable<CodeInstruction> AddExtraGate(IEnumerable<CodeInstruction> instructions)
    {
        yield return new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Game), nameof(Game.DrawUI)));
        yield return new CodeInstruction(OpCodes.Pop);
        foreach (var instruction in instructions) yield return instruction;
    }
}
