using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace KSA;

// Native-free stand-ins for the KSA surfaces the production patches touch. Member names and
// shapes mirror KSA 5482 so the linked production code and Harmony lookups bind unchanged.

public sealed class Vehicle
{
    public Vehicle(string id, int partCount = 1)
    {
        Id = id;
        Parts = new PartTree(this, partCount);
    }

    public string Id { get; }
    public bool IsDisposed { get; set; }
    public bool IsDebris { get; set; }
    public PartTree Parts { get; }
}

public sealed class PartTree
{
    public PartTree(Vehicle? owner, int partCount)
    {
        OwningVehicle = owner;
        for (int i = 0; i < partCount; i++)
        {
            var part = new Part { Tree = this };
            _parts.Add(part);
            // Every full part carries one sub-part so sub-part ownership resolution is exercised too.
            _parts.Add(new Part { PartParent = part, Tree = this });
        }
    }

    private readonly List<Part> _parts = new();
    public Vehicle? OwningVehicle;
    public IReadOnlyList<Part> Parts => _parts;
    public int Count => _parts.Count;
}

public sealed class Part
{
    /// <summary>Authored template tolerance; NaN derives a fallback the way PartStructuralLimits does.</summary>
    public double AuthoredTolerance = double.NaN;
    public PartTree? Tree;
    public Part? PartParent;
    public bool IsAttachedInternal;
    public Part FullPart => PartParent ?? this;

    public double CrashTolerancePascals
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        get => PartStructuralLimits.ResolveCrashTolerance(AuthoredTolerance, 1000.0, 1.0);
    }
}

public static class PartStructuralLimits
{
    public const double DerivedFallback = 9_000_000.0;

    public static double ResolveCrashTolerance(double authoredPascals, double massKg, double volumeCubicMetres) =>
        double.IsNaN(authoredPascals) || authoredPascals <= 0.0 ? DerivedFallback : authoredPascals;

    public static bool HasFailed(double peakPressure, double crashTolerance) =>
        crashTolerance > 0.0 && peakPressure >= crashTolerance;
}

public sealed class PartFailureEvent
{
    public List<Part> FailedParts = new();
    public bool DestroyWholeVehicle;
}

/// <summary>Mirror of KSA 5482 PartFailure.Detect's decision (PartFailure.cs:46-77), minus Bepu buffers.</summary>
public static class PartFailure
{
    public static void Detect(VehicleUpdateState vehicleState, IReadOnlyList<(Part Part, double PeakPressure)> contacts)
    {
        if (vehicleState.DestructionEvent != null || vehicleState.PartFailureEvent != null || vehicleState.IsKitten) return;
        List<Part>? failed = null;
        foreach (var (part, peakPressure) in contacts)
        {
            if (PartStructuralLimits.HasFailed(peakPressure, part.CrashTolerancePascals) && !part.IsAttachedInternal)
                (failed ??= new List<Part>()).Add(part);
        }
        if (failed != null)
        {
            vehicleState.PartFailureEvent = new PartFailureEvent
            {
                FailedParts = failed,
                DestroyWholeVehicle = failed.Count >= Math.Max(2, (int)(vehicleState.ReadOnlyVehicle.Parts.Count * 0.5))
            };
        }
    }
}

public struct StructuralLoad
{
    public double PeakGLoad, MaxGLoad, PeakDynamicPressure, MaxDynamicPressure;
    public bool IsPressureHydrodynamic;
    public double GLoadFraction => MaxGLoad > 0 ? PeakGLoad / MaxGLoad : 0;
    public double DynamicPressureFraction => MaxDynamicPressure > 0 ? PeakDynamicPressure / MaxDynamicPressure : 0;
}

[Flags]
public enum Situation { None = 0, Terrain = 1, Ocean = 2 }

public static class SituationExtensions
{
    public static bool HasTerrainContact(this Situation situation) => (situation & Situation.Terrain) != 0;
    public static bool HasOceanContact(this Situation situation) => (situation & Situation.Ocean) != 0;
}

public enum VehicleDestructionCause
{
    GroundImpact, OceanImpact, Collision, ExcessiveGForce, AerodynamicForces, HydrodynamicForces
}

public sealed class VehicleDestructionEvent
{
    public VehicleDestructionCause Cause;
    public float PeakGLoad, PeakDynamicPressure;
}

public struct VehicleProperties
{
    public double Radius;
    public Situation Situation;
    public readonly double ComputeBoundingSphereRadiusAsmb() => Radius;
}

public struct VehicleUpdateData { public StructuralLoad NewStructuralLoad; }

public sealed class VehicleUpdateState(Vehicle vehicle)
{
    public readonly Vehicle ReadOnlyVehicle = vehicle;
    public VehicleProperties Props = new() { Radius = 1 };
    public VehicleUpdateData UpdateData;
    public VehicleDestructionEvent? DestructionEvent;
    public PartFailureEvent? PartFailureEvent;
    public double PeakGLoad = 75, PeakDynamicPressure;
    public bool IsKitten, IsDebris, HadVehicleContactThisFrame;
    public ref readonly VehicleProperties GetReadOnlyProps() => ref Props;
}

public static class VehicleStructuralLimits
{
    public static double EffectiveMaxGLoad(double radius) =>
        Math.Max(5.0, 50.0 * Math.Min(1.0, 5.0 / Math.Max(radius, 0.001)));
}

public static class PhysicsBubble
{
    public static void Detect(VehicleUpdateState state) => DetectStructuralFailure(state);

    // KSA 5482 PhysicsBubble.cs:958-997 decision flow (identical to 5438:873 and 5402:782).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DetectStructuralFailure(VehicleUpdateState vehicleState)
    {
        if (vehicleState.DestructionEvent != null) return;
        ref readonly VehicleProperties props = ref vehicleState.GetReadOnlyProps();
        double limit = VehicleStructuralLimits.EffectiveMaxGLoad(props.ComputeBoundingSphereRadiusAsmb());
        if (vehicleState.IsKitten) limit *= 2.5;
        Situation situation = props.Situation;
        ref StructuralLoad load = ref vehicleState.UpdateData.NewStructuralLoad;
        load.PeakGLoad = vehicleState.PeakGLoad;
        load.MaxGLoad = limit;
        load.PeakDynamicPressure = vehicleState.PeakDynamicPressure;
        load.MaxDynamicPressure = 200000;
        load.IsPressureHydrodynamic = situation.HasOceanContact();
        bool gFailed = load.GLoadFraction >= 1.0;
        bool pressureFailed = load.DynamicPressureFraction >= 1.0;
        bool contact = situation.HasTerrainContact() || situation.HasOceanContact() || vehicleState.HadVehicleContactThisFrame;
        if (gFailed && contact && vehicleState.PartFailureEvent != null) gFailed = false;
        if (gFailed && vehicleState.IsDebris) gFailed = false;
        if (gFailed || pressureFailed)
        {
            VehicleDestructionCause cause = situation.HasTerrainContact()
                ? VehicleDestructionCause.GroundImpact
                : situation.HasOceanContact()
                    ? (gFailed ? VehicleDestructionCause.OceanImpact : VehicleDestructionCause.HydrodynamicForces)
                    : gFailed && vehicleState.HadVehicleContactThisFrame
                        ? VehicleDestructionCause.Collision
                        : !pressureFailed ? VehicleDestructionCause.ExcessiveGForce : VehicleDestructionCause.AerodynamicForces;
            vehicleState.DestructionEvent = new VehicleDestructionEvent
            {
                Cause = cause,
                PeakGLoad = (float)vehicleState.PeakGLoad,
                PeakDynamicPressure = (float)vehicleState.PeakDynamicPressure
            };
        }
    }
}
