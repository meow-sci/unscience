using System;
using System.Reflection;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Use the kitten's headward axis as the default rocket nose while enabled.</summary>
public static class IronManControlFramePatches
{
    private static bool _applied;

    public static void Apply(Harmony harmony)
    {
        if (_applied) return;
        try
        {
            harmony.Patch(Target(), postfix: new HarmonyMethod(PatchMethod()));
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
        harmony.Unpatch(Target(), PatchMethod());
    }

    private static void FramePostfix(Vehicle __instance, ref doubleQuat __result)
    {
        // A deliberately selected control part/docking port retains its native orientation.
        // This one shared frame feeds navball, attitude control, body rates and RCS mapping.
        if (_applied && __instance is KittenEva kitten
            && kitten.ControlPart == null && kitten.ControlConnector == null
            && IronManSubmod.Instance?.IsEnabled(kitten) == true)
            __result = IronManOrientation.RocketCtrl2Body;
    }

    private static MethodInfo Target() => AccessTools.PropertyGetter(typeof(Vehicle), nameof(Vehicle.Ctrl2Body))
        ?? throw new MissingMethodException(typeof(Vehicle).FullName, "get_Ctrl2Body");

    private static MethodInfo PatchMethod() =>
        AccessTools.DeclaredMethod(typeof(IronManControlFramePatches), nameof(FramePostfix))!;
}
