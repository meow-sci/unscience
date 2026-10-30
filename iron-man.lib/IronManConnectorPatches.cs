using System;
using System.Reflection;
using HarmonyLib;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Passive round-trip support applies only to explicitly authored Iron Man instances.</summary>
public static class IronManConnectorPatches
{
    // KSA 5707 routes every Part construction (save load via Part(PartInstance, Part), template
    // sub-parts, the public 4-argument constructor and PartCloner copies) through this private
    // constructor. Decoding here, and only here, restores a saved marker exactly once on any path.
    private static readonly Type[] ConstructorParameters =
    {
        typeof(string), typeof(PartTemplate), typeof(PartInstance), typeof(Part), typeof(TransformReference),
        typeof(int), typeof(int), typeof(int), typeof(Part), typeof(bool),
    };
    private static MethodInfo? _serialize;
    private static ConstructorInfo? _construct;
    private static ConstructorInfo? _copy;
    private static bool _applied;

    public static void Apply(Harmony harmony)
    {
        if (_applied) return;
        _serialize = AccessTools.Method(typeof(Part), nameof(Part.GetReferenceWithChildren),
            new[] { typeof(uint).MakeByRefType(), typeof(PartInstance), typeof(bool) })
            ?? throw new MissingMethodException("Part.GetReferenceWithChildren(ref uint, PartInstance, bool)");
        _construct = AccessTools.Constructor(typeof(Part), ConstructorParameters)
            ?? throw new MissingMethodException("private Part(string, PartTemplate, PartInstance, Part, TransformReference, int, int, int, Part, bool)");
        // Copies (editor Copy Tree, symmetry and radial clones) are rebuilt from the template, so
        // owned anchors must be re-added before PartCloner reconnects by connector index.
        _copy = AccessTools.Constructor(typeof(Part), new[] { typeof(Part), typeof(Part) })
            ?? throw new MissingMethodException("Part(Part, Part)");
        try
        {
            harmony.Patch(_serialize, postfix: new HarmonyMethod(typeof(IronManConnectorPatches), nameof(SerializePostfix)));
            harmony.Patch(_construct,
                prefix: new HarmonyMethod(typeof(IronManConnectorPatches), nameof(ConstructPrefix)),
                postfix: new HarmonyMethod(typeof(IronManConnectorPatches), nameof(ConstructPostfix)));
            harmony.Patch(_copy, postfix: new HarmonyMethod(typeof(IronManConnectorPatches), nameof(CopyPostfix)));
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
        if (_serialize != null) harmony.Unpatch(_serialize, AccessTools.Method(typeof(IronManConnectorPatches), nameof(SerializePostfix)));
        if (_construct != null)
        {
            harmony.Unpatch(_construct, AccessTools.Method(typeof(IronManConnectorPatches), nameof(ConstructPrefix)));
            harmony.Unpatch(_construct, AccessTools.Method(typeof(IronManConnectorPatches), nameof(ConstructPostfix)));
        }
        if (_copy != null) harmony.Unpatch(_copy, AccessTools.Method(typeof(IronManConnectorPatches), nameof(CopyPostfix)));
        _serialize = null;
        _construct = null;
        _copy = null;
        _applied = false;
    }

    private static void SerializePostfix(Part __instance, PartInstance __result)
    {
        if (IronManConnectors.GetOwned(__instance).Count != 0)
            __result.Id = IronManConnectorData.Capture(__instance);
    }

    private static void ConstructPrefix(ref string? __0, PartTemplate __1, PartInstance? __2,
        out IronManConnectorData? __state)
    {
        __state = IronManConnectorData.Decode(__2?.Id, __1);
        if (__state != null) __0 = __state.OriginalId;
    }

    private static void ConstructPostfix(Part __instance, IronManConnectorData? __state) => __state?.Restore(__instance);

    private static void CopyPostfix(Part __instance, Part __0) => IronManConnectors.CopyOwned(__0, __instance);
}
