using System;
using System.Reflection;
using HarmonyLib;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Passive round-trip support applies only to explicitly authored Iron Man instances.</summary>
public static class IronManConnectorPatches
{
    private static MethodInfo? _serialize;
    private static ConstructorInfo? _construct;
    private static bool _applied;

    public static void Apply(Harmony harmony)
    {
        if (_applied) return;
        _serialize = AccessTools.Method(typeof(Part), nameof(Part.GetReferenceWithChildren),
            new[] { typeof(uint).MakeByRefType(), typeof(PartInstance), typeof(bool) })
            ?? throw new MissingMethodException("Part.GetReferenceWithChildren(ref uint, PartInstance, bool)");
        _construct = AccessTools.Constructor(typeof(Part),
            new[] { typeof(string), typeof(PartTemplate), typeof(PartInstance), typeof(Part) })
            ?? throw new MissingMethodException("Part(string, PartTemplate, PartInstance, Part)");
        try
        {
            harmony.Patch(_serialize, postfix: new HarmonyMethod(typeof(IronManConnectorPatches), nameof(SerializePostfix)));
            harmony.Patch(_construct,
                prefix: new HarmonyMethod(typeof(IronManConnectorPatches), nameof(ConstructPrefix)),
                postfix: new HarmonyMethod(typeof(IronManConnectorPatches), nameof(ConstructPostfix)));
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
        _serialize = null;
        _construct = null;
        _applied = false;
    }

    private static void SerializePostfix(Part __instance, PartInstance __result)
    {
        if (IronManConnectors.GetOwned(__instance).Count != 0)
            __result.Id = IronManConnectorData.Capture(__instance);
    }

    private static void ConstructPrefix(ref string __0, PartTemplate __1, PartInstance? __2,
        out IronManConnectorData? __state)
    {
        __state = IronManConnectorData.Decode(__2?.Id, __1);
        if (__state != null) __0 = __state.OriginalId;
    }

    private static void ConstructPostfix(Part __instance, IronManConnectorData? __state) => __state?.Restore(__instance);
}
