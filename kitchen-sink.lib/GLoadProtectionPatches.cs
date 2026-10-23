using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KSA;

namespace MeowSci.KitchenSinkLib;

/// <summary>
/// Exempts selected vehicles from KSA's G-load destruction decision without changing physics or
/// telemetry. Since KSA 5638 an EVA kitten's hard landing is decided by a separate
/// <c>DetectKittenImpact</c> check (formerly it was a G-load kill), so its result is filtered too.
/// </summary>
public static class GLoadProtectionPatches
{
    /// <summary>Exact parameter list of the private detector and kitten-impact check (KSA 5638).</summary>
    internal static readonly Type[] DetectorParameters = { typeof(VehicleUpdateState), typeof(SimStep) };

    private static MethodInfo? _original;
    private static readonly MethodInfo TranspilerMethod =
        AccessTools.Method(typeof(GLoadProtectionPatches), nameof(Transpile));

    public static bool IsApplied { get; private set; }

    public static void Apply(Harmony harmony)
    {
        if (IsApplied) return;
        var original = ResolveDetector();
        ResolveKittenImpact();
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

    /// <summary>Private static <c>PhysicsBubble.DetectStructuralFailure(VehicleUpdateState, SimStep)</c>.</summary>
    internal static MethodInfo ResolveDetector()
    {
        var detector = AccessTools.Method(typeof(PhysicsBubble), "DetectStructuralFailure", DetectorParameters)
            ?? throw new MissingMethodException(typeof(PhysicsBubble).FullName, "DetectStructuralFailure");
        if (!detector.IsStatic || detector.ReturnType != typeof(void))
            throw new InvalidOperationException("Unexpected KSA structural-failure detector signature.");
        return detector;
    }

    /// <summary>Private static <c>PhysicsBubble.DetectKittenImpact(VehicleUpdateState, SimStep) : bool</c>.</summary>
    internal static MethodInfo ResolveKittenImpact()
    {
        var check = AccessTools.Method(typeof(PhysicsBubble), "DetectKittenImpact", DetectorParameters)
            ?? throw new MissingMethodException(typeof(PhysicsBubble).FullName, "DetectKittenImpact");
        if (!check.IsStatic || check.ReturnType != typeof(bool))
            throw new InvalidOperationException("Unexpected KSA kitten-impact check signature.");
        return check;
    }

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var code = new List<CodeInstruction>(instructions);
        var getter = AccessTools.PropertyGetter(typeof(StructuralLoad), nameof(StructuralLoad.GLoadFraction));
        int gLoad = FindSingleCall(code, getter, "G-load comparison");
        int kittenImpact = FindSingleCall(code, ResolveKittenImpact(), "kitten-impact check");

        // Change only the values consumed by the destruction decisions, preserving telemetry,
        // pressure checks, part damage, flight-computer limits and the kitten velocity sample
        // DetectKittenImpact records. Insert at the later anchor first so the other index stays valid.
        var gLoadFilter = AccessTools.Method(typeof(GLoadProtectionPatches), nameof(FilterGLoadFraction));
        var impactFilter = AccessTools.Method(typeof(GLoadProtectionPatches), nameof(FilterKittenImpact));
        if (gLoad > kittenImpact)
        {
            InsertFilter(code, gLoad, gLoadFilter);
            InsertFilter(code, kittenImpact, impactFilter);
        }
        else
        {
            InsertFilter(code, kittenImpact, impactFilter);
            InsertFilter(code, gLoad, gLoadFilter);
        }
        return code;
    }

    private static int FindSingleCall(List<CodeInstruction> code, MethodInfo target, string anchor)
    {
        int match = -1;
        for (int i = 0; i < code.Count; i++)
        {
            if (!code[i].Calls(target)) continue;
            if (match >= 0)
                throw new InvalidOperationException($"KSA structural-failure detector has multiple {anchor} anchors.");
            match = i;
        }
        if (match < 0)
            throw new InvalidOperationException($"KSA structural-failure detector {anchor} anchor was not found.");
        return match;
    }

    /// <summary>Appends <c>(value, vehicleState) -> filtered value</c>; ldarg.0 is the static method's vehicleState.</summary>
    private static void InsertFilter(List<CodeInstruction> code, int anchor, MethodInfo filter) =>
        code.InsertRange(anchor + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Call, filter)
        });

    private static double FilterGLoadFraction(double fraction, VehicleUpdateState state) =>
        GLoadProtection.Contains(state.ReadOnlyVehicle) ? 0.0 : fraction;

    /// <summary>A protected kitten's impact falls through to the (filtered) G-load and pressure checks.</summary>
    private static bool FilterKittenImpact(bool impact, VehicleUpdateState state) =>
        impact && !GLoadProtection.Contains(state.ReadOnlyVehicle);
}
