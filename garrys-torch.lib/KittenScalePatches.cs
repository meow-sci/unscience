using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace MeowSci.GarrysTorchLib;

/// <summary>
/// Extends KittenEva's scalar-only character scale to an XYZ transform.
/// Ordinary vehicles do not use this patch because their render path already consumes Part.Scale.
/// </summary>
public static class KittenScalePatches
{
    private sealed class ScaleCorrection(float3 value)
    {
        public float3 Value { get; } = value;
    }

    private static ConditionalWeakTable<KittenRenderable, ScaleCorrection> _corrections = new();
    private static MethodInfo? _modelToBodyMatrix;
    private static MethodInfo? _postfix;

    public static void Apply(Harmony harmony)
    {
        _modelToBodyMatrix = AccessTools.Method(
            typeof(KittenRenderable), "ModelToBodyMatrix", Type.EmptyTypes);
        if (_modelToBodyMatrix == null)
            throw new MissingMethodException(typeof(KittenRenderable).FullName, "ModelToBodyMatrix");

        _postfix = AccessTools.Method(typeof(KittenScalePatches), nameof(ModelToBodyMatrixPostfix));
        if (_postfix == null)
            throw new MissingMethodException(typeof(KittenScalePatches).FullName, nameof(ModelToBodyMatrixPostfix));

        harmony.Patch(_modelToBodyMatrix, postfix: new HarmonyMethod(_postfix));
        Console.WriteLine("garrys-torch.lib: KittenEva XYZ scale patch applied");
    }

    public static void Remove(Harmony harmony)
    {
        if (_modelToBodyMatrix != null && _postfix != null)
            harmony.Unpatch(_modelToBodyMatrix, _postfix);

        _modelToBodyMatrix = null;
        _postfix = null;
        _corrections = new ConditionalWeakTable<KittenRenderable, ScaleCorrection>();
        Console.WriteLine("garrys-torch.lib: KittenEva XYZ scale patch removed");
    }

    /// <summary>Set the character axis correction; the caller owns the scalar avatar scale.</summary>
    public static void SetScale(KittenRenderable renderable, float3 scale)
    {
        _corrections.Remove(renderable);

        var correction = new float3(1f, scale.Y / scale.X, scale.Z / scale.X);
        if (!WeldScale.Equals(correction, WeldScale.Identity))
            _corrections.Add(renderable, new ScaleCorrection(correction));
    }

    private static void ModelToBodyMatrixPostfix(
        KittenRenderable __instance, ref float4x4 __result)
    {
        if (_corrections.TryGetValue(__instance, out var correction))
            __result = float4x4.CreateScale(correction.Value) * __result;
    }
}
