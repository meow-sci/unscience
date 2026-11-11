using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MeowSci.KsaAbstractions;
using MeowSci.YouDidntSeeAnythingLib;
using Game = KSA.Program;

namespace YouDidntSeeAnythingPatchTests;

internal static class FixtureEntry
{
    private static int _fallbackBefore, _fallbackAfter, _foreign;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        var harmony = new Harmony("ydsa.fixture");
        var foreign = new Harmony("ydsa.foreign.fixture");
        var frame = AccessTools.Method(typeof(Game), nameof(Game.OnFrame));
        var menu = AccessTools.Method(typeof(Game), nameof(Game.DrawMenuBar));
        var foreignPostfix = AccessTools.Method(typeof(FixtureEntry), nameof(ForeignPostfix));
        var extraGate = AccessTools.Method(typeof(FixtureEntry), nameof(AddExtraGate));
        foreign.Patch(menu, postfix: new HarmonyMethod(foreignPostfix));
        foreign.Patch(AccessTools.Method(typeof(Game), nameof(Game.OnDrawUiThreadSafe)),
            postfix: new HarmonyMethod(typeof(Purr2D2.Runtime.GameHooks), nameof(Purr2D2.Runtime.GameHooks.Overlay)));
        HiddenUiFrameHook.BeforeGui = _ => _fallbackBefore++;
        HiddenUiFrameHook.AfterGui = _ => _fallbackAfter++;
        HiddenUiFrameHook.Patch(harmony);
        HiddenUiVisibilityPatches.Apply(harmony);
        Check(HiddenUiVisibilityPatches.IsApplied, "Patch installation");

        var game = new Game();
        Game.DrawUI = false;
        int setters = Game.SetCount;
        VisibilitySettings.Enabled = true;
        VisibilitySettings.HideGauges = true;
        game.OnFrame(0, 0.1);
        Check(Game.Before == 1 && Game.After == 1 && _fallbackBefore == 0 && _fallbackAfter == 0, "Normal UI callbacks replace fallback exactly once");
        Check(!Game.DrawUI && Game.SetCount == setters, "F2 state never written");
        Check(Game.MenuDraws == 0 && KSA.GaugeCanvas.Drawn == 1 && KSA.GaugeCanvas.Final == 0 && KSA.Universe.Drawn == 0,
            "Hidden pass suppresses menu, stock gauge and world overlays but retains custom gauge");

        Game.BeforeCallback = () => VisibilitySettings.Enabled = false;
        game.OnFrame(0, 0.1);
        Check(Game.Before == 2 && Game.After == 2 && _fallbackBefore == 0 && _fallbackAfter == 0,
            "Mid-frame disable does not duplicate callbacks or split phases");
        Game.BeforeCallback = null;
        game.OnFrame(0, 0.1);
        Check(Game.Before == 2 && Game.After == 2 && _fallbackBefore == 1 && _fallbackAfter == 1,
            "Disabled feature returns to fallback exactly once");
        VisibilitySettings.Enabled = true;
        NativeWindowFilter.IsReady = false;
        game.OnFrame(0, 0.1);
        Check(Game.Before == 2 && _fallbackBefore == 2 && _fallbackAfter == 2,
            "Unavailable native filter returns to stock hidden UI and fallback");
        NativeWindowFilter.IsReady = true;
        VisibilitySettings.Enabled = false;
        Game.DrawUI = true;
        game.OnFrame(0, 0.1);
        Check(Game.Before == 3 && Game.After == 3 && Game.MenuDraws == 1 && KSA.GaugeCanvas.Drawn == 4 && KSA.Universe.Drawn == 1,
            "Normal UI remains intact");

        Purr2D2Checks.Run();
        Game.DrawUI = false;
        Purr2D2.Runtime.GameHooks.Runtime.Held = true;
        HiddenUiVisibilityPatches.Remove(harmony);
        Purr2D2Checks.CheckRemovalAndRollback(harmony, foreign);
        int beforeForeign = _foreign;
        game.DrawMenuBar();
        Check(_foreign == beforeForeign + 1, "Removing feature leaves foreign patches installed");
        foreign.Patch(frame, transpiler: new HarmonyMethod(extraGate));
        bool failed = false;
        try { HiddenUiVisibilityPatches.Apply(harmony); }
        catch (Exception) { failed = true; }
        Check(failed && !HiddenUiVisibilityPatches.IsApplied, "Changed gate shape fails closed");
        var framePatches = Harmony.GetPatchInfo(frame);
        Check(framePatches != null && framePatches.Transpilers.Any(p => p.owner == foreign.Id), "Failed install preserves foreign transpiler");
        Check(framePatches != null && !framePatches.Transpilers.Any(p => p.owner == harmony.Id), "Failed install rolls back feature transpiler");
        HiddenUiFrameHook.Unpatch(harmony);
        foreign.UnpatchAll(foreign.Id);
        Console.WriteLine("PASS: real Harmony fixture gates, rollback and callback lifecycle");
    }

    [MethodImpl(MethodImplOptions.NoInlining)] private static void ForeignPostfix() => _foreign++;
    private static IEnumerable<CodeInstruction> AddExtraGate(IEnumerable<CodeInstruction> instructions)
    {
        yield return new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Game), nameof(Game.DrawUI)));
        yield return new CodeInstruction(OpCodes.Pop);
        foreach (var instruction in instructions) yield return instruction;
    }
}
