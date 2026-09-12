using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace MeowSci.GodzillaLib;

/// <summary>Scales draw transforms and render culling only; never changes simulation geometry.</summary>
public static class VisualScalePatches
{
    private sealed record Scale(float3 Factor);
    private static ConditionalWeakTable<Vehicle, Scale> _scales = new();
    private static readonly List<(MethodInfo Target, MethodInfo Patch)> Installed = new();
    public static bool IsApplied { get; private set; }

    public static void Apply(Harmony harmony)
    {
        if (IsApplied) return;
        try
        {
            Install(harmony, typeof(Vehicle), nameof(Vehicle.UpdateRenderData), nameof(RenderRadiusTranspiler), true);
            Install(harmony, typeof(Vehicle), nameof(Vehicle.GetWorldMatrix), nameof(RenderRadiusTranspiler), true);
            Install(harmony, typeof(Vehicle), nameof(Vehicle.GetWorldMatrix), nameof(WorldMatrixPostfix), false, true);
            Install(harmony, typeof(PartTree), nameof(PartTree.UpdateRenderData), nameof(PartsPrefix), false);
            Install(harmony, typeof(PartTree), nameof(PartTree.UpdateRenderData), nameof(PartsFinalizer), false, finalizer: true);
            IsApplied = true;
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
        foreach (var (target, patch) in Installed) harmony.Unpatch(target, patch);
        Installed.Clear();
        _scales = new();
    }

    internal static void SetScale(Vehicle vehicle, float3 factor)
    {
        _scales.Remove(vehicle);
        _scales.Add(vehicle, new(factor));
    }

    internal static void ClearScale(Vehicle vehicle) => _scales.Remove(vehicle);

    private static void Install(Harmony harmony, Type type, string name, string patchName,
        bool transpiler, bool postfix = false, bool finalizer = false)
    {
        var target = AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
        var patch = AccessTools.Method(typeof(VisualScalePatches), patchName);
        Installed.Add((target, patch));
        var method = new HarmonyMethod(patch);
        harmony.Patch(target, prefix: !transpiler && !postfix && !finalizer ? method : null,
            postfix: postfix ? method : null, transpiler: transpiler ? method : null,
            finalizer: finalizer ? method : null);
    }

    // Replace only the radius reads inside these two draw methods. A global MeanRadius override
    // would leak straight back into physics-bubble sizing, camera positioning and terrain logic.
    private static IEnumerable<CodeInstruction> RenderRadiusTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        int matches = 0;
        foreach (var instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                instruction.operand is MethodInfo method && method.Name == "get_MeanRadius" &&
                method.ReturnType == typeof(double) && method.DeclaringType!.IsAssignableFrom(typeof(Vehicle)))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(VisualScalePatches), nameof(RenderRadius));
                matches++;
            }
            yield return instruction;
        }
        if (matches != 1) throw new InvalidOperationException($"Expected one render radius read; found {matches}.");
    }

    private static double RenderRadius(Vehicle vehicle) =>
        IsApplied && _scales.TryGetValue(vehicle, out var scale)
            ? vehicle.MeanRadius * Math.Max(scale.Factor.X, Math.Max(scale.Factor.Y, scale.Factor.Z))
            : vehicle.MeanRadius;

    private static void WorldMatrixPostfix(Vehicle __instance, ref float4x4? __result)
    {
        if (IsApplied && __result.HasValue && _scales.TryGetValue(__instance, out var scale))
            __result = float4x4.CreateScale(scale.Factor) * __result.Value;
    }

    private static void PartsPrefix(PartTree __instance, ref double4x4 matrixAsmb2Ego, out double4x4? __state)
    {
        __state = null;
        var vehicle = __instance.OwningVehicle;
        if (!IsApplied || vehicle == null || !_scales.TryGetValue(vehicle, out var scale)) return;
        __state = matrixAsmb2Ego;
        // KSA's assembly-to-ego matrix already subtracts the live COM. Scale around that same
        // pivot to keep the vessel's physical position fixed, including asymmetric/fuelled craft.
        var pivot = vehicle.CenterOfMassAsmb;
        var factor = new double3(scale.Factor.X, scale.Factor.Y, scale.Factor.Z);
        matrixAsmb2Ego = double4x4.CreateTranslation(-pivot) * double4x4.CreateScale(factor) *
            double4x4.CreateTranslation(pivot) * matrixAsmb2Ego;
    }

    private static void PartsFinalizer(ref double4x4 matrixAsmb2Ego, double4x4? __state)
    {
        // The game's input is ref readonly; do not leave its caller's local matrix modified.
        if (__state.HasValue) matrixAsmb2Ego = __state.Value;
    }
}
