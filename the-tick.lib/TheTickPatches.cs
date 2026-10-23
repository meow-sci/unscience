using System;
using System.Reflection;
using HarmonyLib;
using KSA;

namespace MeowSci.TheTickLib;

/// <summary>
/// Makes registered vehicles indestructible by forces without touching shared part templates.
/// <list type="bullet">
/// <item>Every part owned by a protected vehicle reports <see cref="TickProtection.UnbreakableTolerancePascals"/>
/// from <c>Part.CrashTolerancePascals</c>, so contact-pressure part failure never trips.</item>
/// <item>Any destruction event that <c>PhysicsBubble.DetectStructuralFailure</c> creates for a protected
/// vehicle is discarded: G-load, dynamic pressure and (since KSA 5638) an EVA kitten's
/// <c>DetectKittenImpact</c> hard landing. Whatever load telemetry it wrote is kept.</item>
/// </list>
/// </summary>
public static class TheTickPatches
{
    /// <summary>Exact parameter list of the private detector (KSA 5638 added the <c>SimStep frame</c>).</summary>
    internal static readonly Type[] DetectorParameters = { typeof(VehicleUpdateState), typeof(SimStep) };

    private static MethodInfo? _toleranceGetter;
    private static MethodInfo? _detector;
    private static readonly MethodInfo ToleranceMethod =
        AccessTools.Method(typeof(TheTickPatches), nameof(OverrideCrashTolerance));
    private static readonly MethodInfo RememberMethod =
        AccessTools.Method(typeof(TheTickPatches), nameof(RememberExistingEvent));
    private static readonly MethodInfo SuppressMethod =
        AccessTools.Method(typeof(TheTickPatches), nameof(SuppressStructuralDestruction));

    public static bool IsApplied { get; private set; }

    public static void Apply(Harmony harmony)
    {
        if (IsApplied) return;
        var getter = AccessTools.PropertyGetter(typeof(Part), nameof(Part.CrashTolerancePascals))
            ?? throw new MissingMethodException(typeof(Part).FullName, nameof(Part.CrashTolerancePascals));
        if (getter.ReturnType != typeof(double) || getter.IsStatic)
            throw new InvalidOperationException("Unexpected KSA part crash-tolerance getter signature.");
        var detector = ResolveDetector();

        harmony.Patch(getter, prefix: new HarmonyMethod(ToleranceMethod));
        try
        {
            harmony.Patch(detector, prefix: new HarmonyMethod(RememberMethod), postfix: new HarmonyMethod(SuppressMethod));
        }
        catch
        {
            harmony.Unpatch(getter, ToleranceMethod);
            throw;
        }
        _toleranceGetter = getter;
        _detector = detector;
        IsApplied = true;
        Console.WriteLine("the-tick: indestructibility patches applied.");
    }

    public static void Remove(Harmony harmony)
    {
        TickProtection.Clear();
        if (_toleranceGetter != null) harmony.Unpatch(_toleranceGetter, ToleranceMethod);
        if (_detector != null)
        {
            harmony.Unpatch(_detector, RememberMethod);
            harmony.Unpatch(_detector, SuppressMethod);
        }
        _toleranceGetter = null;
        _detector = null;
        IsApplied = false;
    }

    /// <summary>Private static <c>PhysicsBubble.DetectStructuralFailure(VehicleUpdateState, SimStep) : void</c>.</summary>
    internal static MethodInfo ResolveDetector()
    {
        var detector = AccessTools.Method(typeof(PhysicsBubble), "DetectStructuralFailure", DetectorParameters)
            ?? throw new MissingMethodException(typeof(PhysicsBubble).FullName, "DetectStructuralFailure");
        if (!detector.IsStatic || detector.ReturnType != typeof(void))
            throw new InvalidOperationException("Unexpected KSA structural-failure detector signature.");
        return detector;
    }

    /// <summary>Prefix on <c>Part.CrashTolerancePascals</c>: protected owners skip the native resolution.</summary>
    private static bool OverrideCrashTolerance(Part __instance, ref double __result)
    {
        var owner = OwnerOf(__instance);
        if (owner == null || !TickProtection.Contains(owner)) return true;
        __result = TickProtection.UnbreakableTolerancePascals;
        return false;
    }

    private static Vehicle? OwnerOf(Part part) =>
        part.Tree?.OwningVehicle ?? part.FullPart.Tree?.OwningVehicle;

    /// <summary>Prefix: a destruction event queued before the detector ran is never ours to discard.</summary>
    private static void RememberExistingEvent(VehicleUpdateState vehicleState, out bool __state) =>
        __state = vehicleState.DestructionEvent != null;

    /// <summary>
    /// Postfix: drop only the event the detector just created for a protected vehicle, whichever
    /// branch created it (kitten impact, G-load or dynamic pressure). Binds <c>vehicleState</c> by name.
    /// </summary>
    private static void SuppressStructuralDestruction(VehicleUpdateState vehicleState, bool __state)
    {
        if (__state || vehicleState.DestructionEvent == null) return;
        if (!TickProtection.Contains(vehicleState.ReadOnlyVehicle)) return;
        vehicleState.DestructionEvent = null;
    }
}
