using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.YouDidntSeeAnythingLib;

/// <summary>Replays the game's UI phase while preserving its F2 state.</summary>
public static class HiddenUiVisibilityPatches
{
    private static readonly List<(MethodInfo Target, MethodInfo Patch)> Installed = new();
    private static readonly UiDispatchTracker Dispatch = new();
    internal static bool IsHiddenPass => !Program.DrawUI && Dispatch.Active;
    private static bool Ready => IsApplied && NativeWindowFilter.IsReady;
    public static bool IsApplied { get; private set; }

    public static void Apply(Harmony harmony)
    {
        if (IsApplied) return;
        try
        {
            AddTranspiler(harmony, Required(typeof(Program), "OnFrame"), nameof(FrameTranspiler));
            AddTranspiler(harmony, Required(typeof(Program), "OnDrawUiThreadSafe"), nameof(ThreadSafeTranspiler));
            Add(harmony, Required(typeof(Program), "PrepareImGui"), nameof(PrepareImGuiPrefix), prefix: true, priority: Priority.First);
            Add(harmony, Required(typeof(Program), "DrawMenuBar"), nameof(MenuPrefix), prefix: true);
            Add(harmony, Required(typeof(GaugeCanvas), "OnDrawUi"), nameof(GaugePrefix), prefix: true);
            Add(harmony, Required(typeof(GaugeCanvas), "OnDrawUiFinal"), nameof(GaugeFinalPrefix), prefix: true);
            Add(harmony, Required(typeof(Universe), "OnDrawUi"), nameof(WorldOverlayPrefix), prefix: true);
            IsApplied = true;
            HiddenUiFrameHook.UiCallbacksDispatched = Dispatch.Consume;
        }
        catch
        {
            Remove(harmony);
            throw;
        }
    }

    public static void Remove(Harmony harmony)
    {
        IsApplied = false;
        for (int i = Installed.Count - 1; i >= 0; --i)
        {
            try { harmony.Unpatch(Installed[i].Target, Installed[i].Patch); }
            catch (Exception ex) { Console.WriteLine($"you-didnt-see-anything: Failed to remove {Installed[i].Patch.Name}: {ex}"); }
        }
        Installed.Clear();
        HiddenUiFrameHook.UiCallbacksDispatched = null;
        Dispatch.Reset();
        NativeWindowFilter.Remove();
    }

    private static MethodInfo Required(Type type, string name) => AccessTools.Method(type, name)
        ?? throw new MissingMethodException(type.FullName, name);

    private static void Add(Harmony harmony, MethodInfo target, string name, bool prefix, int priority = Priority.Normal)
    {
        var patch = Required(typeof(HiddenUiVisibilityPatches), name);
        var method = new HarmonyMethod(patch) { priority = priority };
        Installed.Add((target, patch));
        if (prefix) harmony.Patch(target, prefix: method); else harmony.Patch(target, postfix: method);
    }

    private static void AddTranspiler(Harmony harmony, MethodInfo target, string name)
    {
        var patch = Required(typeof(HiddenUiVisibilityPatches), name);
        Installed.Add((target, patch));
        harmony.Patch(target, transpiler: new HarmonyMethod(patch));
    }

    private static IEnumerable<CodeInstruction> FrameTranspiler(IEnumerable<CodeInstruction> instructions) => ReplaceGates(instructions, 2, true);
    private static IEnumerable<CodeInstruction> ThreadSafeTranspiler(IEnumerable<CodeInstruction> instructions) => ReplaceGates(instructions, 1, false);

    private static IEnumerable<CodeInstruction> ReplaceGates(IEnumerable<CodeInstruction> instructions, int expected, bool markDispatch)
    {
        var code = instructions.ToList();
        var getter = AccessTools.PropertyGetter(typeof(Program), nameof(Program.DrawUI));
        var replacement = Required(typeof(HiddenUiVisibilityPatches), nameof(ShouldDrawUi));
        var firstGate = Required(typeof(HiddenUiVisibilityPatches), nameof(ShouldDrawUiAndMark));
        int count = 0;
        foreach (var instruction in code)
        {
            if (instruction.opcode == OpCodes.Call && Equals(instruction.operand, getter))
            {
                instruction.operand = markDispatch && count == 0 ? firstGate : replacement;
                ++count;
            }
        }
        if (count != expected) throw new InvalidOperationException($"Expected {expected} UI gates, found {count}.");
        return code;
    }

    private static bool ShouldDrawUiAndMark() => Dispatch.BeginFrame(Program.DrawUI, VisibilitySettings.Enabled, Ready);
    private static bool ShouldDrawUi() => Dispatch.FrameDraw && (Program.DrawUI || Ready);
    private static bool MenuPrefix() => !IsHiddenPass;
    private static bool WorldOverlayPrefix() => !IsHiddenPass;
    private static bool GaugeFinalPrefix() => !IsHiddenPass || !VisibilitySettings.HideGauges;
    private static bool GaugePrefix(GaugeCanvas __instance) =>
        UiGatePolicy.DrawGauge(IsHiddenPass, VisibilitySettings.HideGauges, __instance.Id);

    private static void PrepareImGuiPrefix() => NativeWindowFilter.EnsureInstalled();

}
