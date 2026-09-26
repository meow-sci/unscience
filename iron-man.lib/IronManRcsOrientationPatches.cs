using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Re-evaluate the stock backpack jets in the enabled kitten's control frame.</summary>
public static class IronManRcsOrientationPatches
{
    private static bool _applied;

    public static void Apply(Harmony harmony)
    {
        if (_applied) return;
        try
        {
            harmony.Patch(TargetMethod(), transpiler: new HarmonyMethod(TranspilerMethod()));
            _applied = true;
        }
        catch
        {
            Remove(harmony);
            throw;
        }
    }

    public static void Remove(Harmony harmony)
    {
        _applied = false;
        harmony.Unpatch(TargetMethod(), TranspilerMethod());
    }

    /// <summary>Call only at the joined physics handoff when activation membership changes.</summary>
    public static void InvalidateCache(KittenEva kitten)
    {
        var states = ModuleStateful<ThrusterController, ThrusterControllerState,
            ThrusterControllerGlobalState, EmptyStruct>.InitializeHotPathList(kitten.Parts.States);
        // Changing eligibility can change the mapping even when an explicit control part
        // keeps Ctrl2Body unchanged. Force the native authority cache to recalculate.
        states.GetMutableGlobalStateForInitialization() = ThrusterControllerGlobalState.Zero;
    }

    private static ThrusterMapFlags? ReadManualControlMap(ThrusterController controller)
    {
        if (_applied)
        {
            var root = controller.Parent.FullPart;
            if (root.Template.Id == IronManConnectors.BackpackTemplateId
                && root.Tree?.OwningVehicle is KittenEva kitten
                && ReferenceEquals(kitten.Parts.Root, root)
                && IronManSubmod.Instance?.IsEnabled(kitten) == true)
            {
                // Null selects native geometry-based mapping. Do not mutate the module:
                // this runs on physics workers and the authored map must survive disable,
                // editor copies, serialization and unload unchanged.
                return null;
            }
        }
        return controller.ManualControlMap;
    }

    internal static IEnumerable<CodeInstruction> ReplaceManualControlMap(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.Select(code => new CodeInstruction(code)).ToList();
        FieldInfo field = AccessTools.Field(typeof(ThrusterController), nameof(ThrusterController.ManualControlMap))
            ?? throw new MissingFieldException(typeof(ThrusterController).FullName, nameof(ThrusterController.ManualControlMap));
        MethodInfo replacement = AccessTools.DeclaredMethod(typeof(IronManRcsOrientationPatches), nameof(ReadManualControlMap))!;
        int replaced = 0;
        foreach (var code in codes)
        {
            if (code.opcode != OpCodes.Ldfld || !Equals(code.operand, field)) continue;
            // Receiver -> nullable map has the same stack effect as the original ldfld;
            // changing the existing instruction preserves branch labels and EH blocks.
            code.opcode = OpCodes.Call;
            code.operand = replacement;
            replaced++;
        }
        if (replaced != 1)
            throw new InvalidOperationException($"Iron Man expected one authored thruster-map read, found {replaced}.");
        return codes;
    }

    private static MethodInfo TargetMethod() =>
        AccessTools.DeclaredMethod(typeof(ThrusterController), nameof(ThrusterController.RecomputeDynamicData))
        ?? throw new MissingMethodException(typeof(ThrusterController).FullName, nameof(ThrusterController.RecomputeDynamicData));

    private static MethodInfo TranspilerMethod() =>
        AccessTools.DeclaredMethod(typeof(IronManRcsOrientationPatches), nameof(ReplaceManualControlMap))!;
}
