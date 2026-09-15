using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KSA;

namespace MeowSci.KitchenSinkLib;

/// <summary>Exempts selected vehicles from the G-load decision without changing physics or telemetry.</summary>
public static class GLoadProtectionPatches
{
    private static MethodInfo? _original;
    private static readonly MethodInfo TranspilerMethod =
        AccessTools.Method(typeof(GLoadProtectionPatches), nameof(Transpile));

    public static bool IsApplied { get; private set; }

    public static void Apply(Harmony harmony)
    {
        if (IsApplied) return;
        var original = AccessTools.Method(typeof(PhysicsBubble), "DetectStructuralFailure",
            new[] { typeof(VehicleUpdateState) })
            ?? throw new MissingMethodException(typeof(PhysicsBubble).FullName, "DetectStructuralFailure");
        if (!original.IsStatic || original.ReturnType != typeof(void))
            throw new InvalidOperationException("Unexpected KSA structural-failure detector signature.");

        harmony.Patch(original, transpiler: new HarmonyMethod(TranspilerMethod));
        _original = original;
        IsApplied = true;
        Console.WriteLine("kitchen-sink: G-load protection patch applied.");
    }

    public static void Remove(Harmony harmony)
    {
        GLoadProtection.Clear();
        if (_original != null) harmony.Unpatch(_original, TranspilerMethod);
        _original = null;
        IsApplied = false;
    }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var code = new List<CodeInstruction>(instructions);
        var getter = AccessTools.PropertyGetter(typeof(StructuralLoad), nameof(StructuralLoad.GLoadFraction));
        int match = -1;
        for (int i = 0; i < code.Count; i++)
        {
            if (!code[i].Calls(getter)) continue;
            if (match >= 0)
                throw new InvalidOperationException("KSA structural-failure detector has multiple G-load comparisons.");
            match = i;
        }
        if (match < 0)
            throw new InvalidOperationException("KSA structural-failure detector G-load comparison was not found.");

        // Change only the value consumed by the destruction decision, preserving
        // telemetry, pressure checks, part damage and flight-computer limits.
        code.InsertRange(match + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Call,
                AccessTools.Method(typeof(GLoadProtectionPatches), nameof(FilterGLoadFraction)))
        });
        return code;
    }

    private static double FilterGLoadFraction(double fraction, VehicleUpdateState state) =>
        GLoadProtection.Contains(state.ReadOnlyVehicle) ? 0.0 : fraction;
}
