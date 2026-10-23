using System;
using System.Reflection;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Draw the navball thrust marker in the live helm frame of an enabled kitten.</summary>
/// <remarks>
/// Stock <c>Vehicle.UpdateNavBallMarkers</c> divides the copied worker <c>FlightComputer.Tnm2Asmb</c>
/// by the main-thread <c>FlightComputer.Helm2Asmb</c>. <c>FlightComputer.CopyFrom</c> never copies
/// <c>Helm2Asmb</c>, so on the main thread it stays Identity and the marker is drawn in body axes.
/// For Iron Man's headward rocket helm that is about 90 degrees off. Only enabled kittens are
/// corrected; every other vehicle keeps the stock marker. A missing target logs and is skipped:
/// the marker is cosmetic and must not disable the rest of Iron Man.
/// </remarks>
public static class IronManNavballPatches
{
    private const string MarkerMethodName = "UpdateNavBallMarkers";
    private const string MarkerFieldName = "_navBallMarkerData";
    private static bool _applied;

    public static void Apply(Harmony harmony)
    {
        if (_applied) return;
        MethodInfo? target = Target();
        if (target == null || !HasMarkerField())
        {
            Console.WriteLine($"iron-man: navball thrust-marker correction unavailable (Vehicle.{MarkerMethodName}/"
                + $"{MarkerFieldName} not found); the stock marker is retained");
            return;
        }
        try
        {
            harmony.Patch(target, postfix: new HarmonyMethod(PatchMethod()));
            _applied = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"iron-man: navball thrust-marker correction unavailable; the stock marker is retained: {ex.Message}");
            // An Unpatch failure means Harmony state is inconsistent: let IronManPatches roll back.
            harmony.Unpatch(target, PatchMethod());
        }
    }

    public static void Remove(Harmony harmony)
    {
        _applied = false;
        MethodInfo? target = Target();
        if (target != null) harmony.Unpatch(target, PatchMethod());
    }

    /// <summary>The stock thrust-marker expression with the live helm frame instead of the stale copy.</summary>
    public static double3 ThrustDirHelm(doubleQuat tnm2Asmb, doubleQuat helm2Body) =>
        double3.UnitX.Transform(tnm2Asmb).Transform(helm2Body.Inverse()).NormalizeOrZero();

    // Harmony injects the private marker struct by reference (three underscores + field name).
    private static void MarkerPostfix(Vehicle __instance, ref NavBallMarkerData ____navBallMarkerData)
    {
        if (!_applied || !____navBallMarkerData.ThrustValid) return;
        if (__instance is not KittenEva kitten || IronManSubmod.Instance?.IsEnabled(kitten) != true) return;
        // Helm2Body is the shared getter (Iron Man's rocket frame or an explicit control choice)
        // that the worker flight computer used for Helm2Asmb/Tnm2Asmb this frame.
        ____navBallMarkerData.ThrustDirHelm = ThrustDirHelm(kitten.FlightComputer.Tnm2Asmb, kitten.Helm2Body);
    }

    private static MethodInfo? Target() =>
        AccessTools.DeclaredMethod(typeof(Vehicle), MarkerMethodName, Type.EmptyTypes);

    private static bool HasMarkerField() =>
        AccessTools.DeclaredField(typeof(Vehicle), MarkerFieldName)?.FieldType == typeof(NavBallMarkerData);

    private static MethodInfo PatchMethod() =>
        AccessTools.DeclaredMethod(typeof(IronManNavballPatches), nameof(MarkerPostfix))!;
}
