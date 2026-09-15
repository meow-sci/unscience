using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace MeowSci.HumbleArteestLib;

/// <summary>
/// Harmony patches for the Engine Emissive feature.
///
/// Prefixes PartModelDynamic.AddInstance to override Temperature and TfiThickness
/// in the PerInstanceData before the data is written to the GPU. Uses the
/// __instance (PartModelDynamic) to look up per-engine settings via
/// <see cref="EngineEmissive"/>.
///
/// No shader modifications needed — Temperature is already wired from C# through
/// DynamicMeshIndirect.vert/frag to the fragment shader's emissive color lookup.
/// </summary>
public static class EngineEmissivePatches
{
    private static MethodInfo? _addInstanceOriginal;
    private static MethodInfo? _addInstancePrefix;

    /// <summary>
    /// Mirror struct matching PartModelDynamic.PerInstanceData with writable fields.
    ///
    /// Only Temperature and TfiThickness are written; the trailing slot is the game's own
    /// Wetness value (KSA 2026.7.9.5018 repurposed the former packing1 padding for the
    /// ENABLE_WETNESS shader variant) and MUST be left alone.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct WritablePerInstanceData
    {
        public float4x4 ModelMatrix; // 64 bytes
        public int StateBitFlag;     //  4 bytes
        public float Temperature;    //  4 bytes ← override target
        public float TfiThickness;   //  4 bytes ← override target
        public float Wetness;        //  4 bytes (game-used since 5018 — do not write)
    }

    public static void Apply(Harmony harmony)
    {
        _addInstanceOriginal = ResolveAddInstance();
        _addInstancePrefix = typeof(EngineEmissivePatches).GetMethod(
            nameof(AddInstancePrefix), BindingFlags.NonPublic | BindingFlags.Static);

        if (_addInstanceOriginal == null)
        {
            Console.WriteLine("humble-arteest: WARNING — PartModelDynamic.AddInstance not found");
            return;
        }

        harmony.Patch(_addInstanceOriginal, prefix: new HarmonyMethod(_addInstancePrefix));
        Console.WriteLine("humble-arteest: EngineEmissive patches applied");
    }

    public static void Remove(Harmony harmony)
    {
        if (_addInstanceOriginal != null && _addInstancePrefix != null)
            harmony.Unpatch(_addInstanceOriginal, _addInstancePrefix);

        _addInstanceOriginal = null;
        _addInstancePrefix = null;

        Console.WriteLine("humble-arteest: EngineEmissive patches removed");
    }

    /// <summary>
    /// KSA 5438 routes both the no-dent and dent-aware public wrappers through this private
    /// submission overload. Bind all four parameter types explicitly so Harmony does not select
    /// an arbitrary public overload.
    /// </summary>
    private static MethodInfo? ResolveAddInstance()
    {
        MethodBase? submission = AccessTools.Method(typeof(PartModelDynamic),
            nameof(PartModelDynamic.AddInstance), new[]
        {
            typeof(PartModelDynamic.PerInstanceData), typeof(KSA.Deformation.PerInstanceDent),
            typeof(IViewport), typeof(int)
        });
        return submission?.IsPrivate == true ? (MethodInfo)submission : null;
    }

    /// <summary>
    /// Harmony prefix on PartModelDynamic.AddInstance. Overrides Temperature and
    /// TfiThickness when the EngineEmissive system has settings for this instance.
    /// </summary>
    private static void AddInstancePrefix(
        PartModelDynamic __instance,
        ref PartModelDynamic.PerInstanceData inInstanceData)
    {
        if (!EngineEmissive.TryGetEffective(__instance, out var temperature, out var tfi))
            return;

        ref var writable = ref Unsafe.As<PartModelDynamic.PerInstanceData, WritablePerInstanceData>(
            ref inInstanceData);
        writable.Temperature = temperature;
        writable.TfiThickness = tfi;
    }
}
