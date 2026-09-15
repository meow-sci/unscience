using System;
using System.Runtime.CompilerServices;

namespace KSA;

public sealed class Vehicle(string id)
{
    public string Id { get; } = id;
    public bool IsDisposed { get; set; }
    public bool IsDebris { get; set; }
}

internal struct StructuralLoad
{
    public double PeakGLoad, MaxGLoad, PeakDynamicPressure, MaxDynamicPressure;
    public bool IsPressureHydrodynamic;
    public double GLoadFraction => MaxGLoad > 0 ? PeakGLoad / MaxGLoad : 0;
    public double DynamicPressureFraction => MaxDynamicPressure > 0 ? PeakDynamicPressure / MaxDynamicPressure : 0;
}

[Flags]
internal enum Situation { None = 0, Terrain = 1, Ocean = 2 }

internal static class SituationExtensions
{
    public static bool HasTerrainContact(this Situation situation) => (situation & Situation.Terrain) != 0;
    public static bool HasOceanContact(this Situation situation) => (situation & Situation.Ocean) != 0;
}

internal enum VehicleDestructionCause
{
    GroundImpact, OceanImpact, Collision, ExcessiveGForce, AerodynamicForces, HydrodynamicForces
}

internal sealed class VehicleDestructionEvent
{
    public VehicleDestructionCause Cause;
    public float PeakGLoad, PeakDynamicPressure;
}

internal struct VehicleProperties
{
    public double Radius;
    public Situation Situation;
    public readonly double ComputeBoundingSphereRadiusAsmb() => Radius;
}

internal struct VehicleUpdateData { public StructuralLoad NewStructuralLoad; }

internal sealed class VehicleUpdateState(Vehicle vehicle)
{
    public Vehicle ReadOnlyVehicle = vehicle;
    public VehicleProperties Props = new() { Radius = 1 };
    public VehicleUpdateData UpdateData;
    public VehicleDestructionEvent? DestructionEvent;
    public object? PartFailureEvent;
    public double PeakGLoad = 75, PeakDynamicPressure;
    public bool IsKitten, IsDebris, HadVehicleContactThisFrame;
    public ref readonly VehicleProperties GetReadOnlyProps() => ref Props;
}

internal static class VehicleStructuralLimits
{
    public static double EffectiveMaxGLoad(double radius) =>
        Math.Max(5.0, 50.0 * Math.Min(1.0, 5.0 / Math.Max(radius, 0.001)));
}

internal static class PhysicsBubble
{
    public static void Detect(VehicleUpdateState state) => DetectStructuralFailure(state);

    // KSA 5438 PhysicsBubble.cs:873-912 decision flow (identical to 5402:782-821).
    // Descriptive local names retain the native decision order and thresholds.
    // Native-free fixture; production transpiler and identity registry are linked unchanged.
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
