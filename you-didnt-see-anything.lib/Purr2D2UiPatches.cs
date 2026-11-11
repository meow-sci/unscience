using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Brutal.GlfwApi;
using HarmonyLib;
using KSA;

namespace MeowSci.YouDidntSeeAnythingLib;

/// <summary>Opens purr2d2's own F2 gates without reviving its attached HUD or game input.</summary>
internal static class Purr2D2UiPatches
{
    private static readonly List<(MethodInfo Target, MethodInfo Patch)> Installed = new();
    private static FieldInfo? _runtimeField;
    private static MethodInfo? _releaseInput;
    private static bool _applied, _wasHidden, _wasRetaining;

    internal static void Apply(Harmony harmony)
    {
        // StarMap loads mod assemblies before AllModsLoaded. No reference or bundled
        // purr2d2 DLL is needed; absence is an ordinary supported configuration.
        Type? hooks = AccessTools.TypeByName("Purr2D2.Runtime.GameHooks");
        if (hooks == null) return;
        try
        {
            Type runtime = hooks.Assembly.GetType("Purr2D2.Runtime.SaberRuntime", throwOnError: true)!;
            _runtimeField = AccessTools.Field(hooks, "Runtime") ?? throw new MissingFieldException(hooks.FullName, "Runtime");
            _releaseInput = Require(runtime, "OverlayFocusLost");
            Add(harmony, Require(hooks, "Overlay"), nameof(VisibilityTranspiler), transpiler: true);
            Add(harmony, Require(runtime, "get_OverlayVisible"), nameof(VisibilityTranspiler), transpiler: true);
            foreach (string property in new[] { "BFL9KUiVisible", "ForceUiVisible", "KittenListVisible" })
                Add(harmony, Require(runtime, "get_" + property), nameof(PanelTranspiler), transpiler: true);
            Add(harmony, Require(runtime, "Frame"), nameof(FrameTranspiler), transpiler: true);
            foreach (string method in new[] { "FilterOverlayMouseButton", "FilterOverlayCursor", "FilterOverlayScroll",
                "SuppressOverlayImGuiMouseButton", "SuppressOverlayImGuiScroll", "FilterSaberDropSpinKey", "FilterForceKey" })
            {
                MethodInfo target = Require(runtime, method);
                if (target.ReturnType != typeof(bool) || target.GetParameters().FirstOrDefault()?.ParameterType != typeof(GlfwWindow))
                    throw new InvalidOperationException($"Unexpected purr2d2 input signature: {method}");
                Add(harmony, target, nameof(InputPrefix));
            }
            Add(harmony, Require(runtime, "get_OverlayCapturesPointer"), nameof(GameCapturePrefix));
            _wasHidden = !Program.DrawUI;
            _wasRetaining = false;
            _applied = true;
            Console.WriteLine("you-didnt-see-anything: purr2d2 detached HUD compatibility applied");
        }
        catch (Exception ex)
        {
            Remove(harmony);
            Console.WriteLine($"you-didnt-see-anything: purr2d2 detached HUD compatibility unavailable: {ex}");
        }
    }

    internal static void Remove(Harmony harmony)
    {
        // Teardown is outside the hot path; release purr2d2's own capture masks
        // before its windows return to ordinary F2 behavior.
        if (_applied && !Program.DrawUI)
        {
            try
            {
                object? runtime = _runtimeField?.GetValue(null);
                if (runtime != null) _releaseInput?.Invoke(runtime, null);
            }
            catch (Exception ex) { Console.WriteLine($"you-didnt-see-anything: Failed to release purr2d2 input: {ex}"); }
        }
        _applied = false;
        for (int i = Installed.Count - 1; i >= 0; --i)
        {
            try { harmony.Unpatch(Installed[i].Target, Installed[i].Patch); }
            catch (Exception ex) { Console.WriteLine($"you-didnt-see-anything: Failed to remove purr2d2 {Installed[i].Patch.Name}: {ex}"); }
        }
        Installed.Clear();
        _runtimeField = null;
        _releaseInput = null;
        _wasHidden = _wasRetaining = false;
    }

    private static MethodInfo Require(Type type, string name) => AccessTools.Method(type, name)
        ?? throw new MissingMethodException(type.FullName, name);

    private static void Add(Harmony harmony, MethodInfo target, string name, bool transpiler = false)
    {
        MethodInfo patch = Require(typeof(Purr2D2UiPatches), name);
        Installed.Add((target, patch));
        if (transpiler) harmony.Patch(target, transpiler: new HarmonyMethod(patch));
        else harmony.Patch(target, prefix: new HarmonyMethod(patch));
    }

    private static bool Retaining => _applied && HiddenUiVisibilityPatches.RetainingWindows;
    private static bool SubmitOverlay() => Program.DrawUI || Retaining;

    private static IEnumerable<CodeInstruction> VisibilityTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        MethodInfo getter = AccessTools.PropertyGetter(typeof(Program), nameof(Program.DrawUI));
        int count = 0;
        foreach (CodeInstruction instruction in code)
        {
            if (!instruction.Calls(getter)) continue;
            instruction.operand = Require(typeof(Purr2D2UiPatches), nameof(SubmitOverlay));
            count++;
        }
        if (count != 1) throw new InvalidOperationException($"Expected one purr2d2 UI gate, found {count}.");
        return code;
    }

    private static IEnumerable<CodeInstruction> PanelTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod, ILGenerator generator)
    {
        Type runtime = __originalMethod.DeclaringType!;
        MethodInfo detached = Require(runtime, "IsHudDetached");
        Type panelType = detached.GetParameters().Single().ParameterType;
        string panelName = __originalMethod.Name switch
        {
            "get_BFL9KUiVisible" => "Saber",
            "get_ForceUiVisible" => "Force",
            "get_KittenListVisible" => "Kittens",
            _ => throw new InvalidOperationException("Unknown purr2d2 HUD panel")
        };
        int panel = Convert.ToInt32(Enum.Parse(panelType, panelName));
        // An attached panel is a background draw list, or an arrangement window.
        // Filter a successful original result so its readiness/disposal guards still
        // run before IsHudDetached touches the native ImGui context.
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ret)
            {
                Label done = generator.DefineLabel();
                var first = new CodeInstruction(OpCodes.Dup).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                instruction.labels.Add(done);
                yield return first;
                yield return new CodeInstruction(OpCodes.Brfalse, done);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(Program), nameof(Program.DrawUI)));
                yield return new CodeInstruction(OpCodes.Brtrue, done);
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldc_I4, panel);
                yield return new CodeInstruction(OpCodes.Call, detached);
                yield return new CodeInstruction(OpCodes.And);
            }
            yield return instruction;
        }
    }

    private static IEnumerable<CodeInstruction> FrameTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod, ILGenerator generator)
    {
        MethodInfo release = Require(__originalMethod.DeclaringType!, "OverlayFocusLost");
        var code = instructions.ToList();
        Label original = generator.DefineLabel();
        code[0].labels.Add(original);
        return new[]
        {
            new CodeInstruction(OpCodes.Call, Require(typeof(Purr2D2UiPatches), nameof(InputModeChanged))),
            new CodeInstruction(OpCodes.Brfalse, original),
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Call, release)
        }.Concat(code);
    }

    private static bool InputModeChanged()
    {
        bool hidden = !Program.DrawUI, retaining = Retaining;
        bool changed = hidden != _wasHidden || retaining != _wasRetaining;
        (_wasHidden, _wasRetaining) = (hidden, retaining);
        return changed;
    }

    private static bool InputPrefix(GlfwWindow __0, ref bool __result)
    {
        // Raw purr2d2 gestures bypass ImGui's NoInputs flags. The main game window
        // must never acquire invisible HUD actions; detached backend routes still run.
        if (Program.DrawUI || __0 != Program.GetWindow()) return true;
        __result = false;
        return false;
    }

    private static bool GameCapturePrefix(ref bool __result)
    {
        if (Program.DrawUI) return true;
        __result = false;
        return false;
    }
}
