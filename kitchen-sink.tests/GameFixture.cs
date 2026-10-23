using System;
using System.Runtime.CompilerServices;

namespace KSA;

public sealed partial class Vehicle(string id)
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

/// <summary>Mirror of KSA 5638 <c>SimStep</c> (<c>SimStep.cs:3</c>); times are seconds instead of UniverseTime.</summary>
internal readonly struct SimStep
{
    public double PreviousTime { get; init; }
    public double NextTime { get; init; }
    public double DeltaTime { get; init; }
}

internal sealed class VehicleUpdateState(Vehicle vehicle)
{
    public Vehicle ReadOnlyVehicle = vehicle;
    public VehicleProperties Props = new() { Radius = 1 };
    public VehicleUpdateData UpdateData;
    public VehicleDestructionEvent? DestructionEvent;
    public object? PartFailureEvent;
    public double PeakGLoad = 75, PeakDynamicPressure;
    public bool IsKitten, IsDebris, HadVehicleContactThisFrame;
    /// <summary>Fixture input: vertical surface-relative velocity (m/s, up positive) this frame.</summary>
    public double VerticalSurfaceVelocity;
    /// <summary>Fixture input standing in for <c>LadderFollowOwnsBody</c> (kitten on a ladder).</summary>
    public bool OnLadder;
    public double KittenSurfaceVelocity;
    public double KittenSurfaceVelocityTime = double.NegativeInfinity;
    public ref readonly VehicleProperties GetReadOnlyProps() => ref Props;
}

internal static class VehicleStructuralLimits
{
    public static double EffectiveMaxGLoad(double radius) =>
        Math.Max(5.0, 50.0 * Math.Min(1.0, 5.0 / Math.Max(radius, 0.001)));
}

internal static class PhysicsBubble
{
    public static readonly SimStep FirstFrame = new() { PreviousTime = 0, NextTime = 0.02, DeltaTime = 0.02 };

    public static void Detect(VehicleUpdateState state) => Detect(state, FirstFrame);
    public static void Detect(VehicleUpdateState state, SimStep frame) => DetectStructuralFailure(state, frame);

    public static SimStep After(SimStep frame) =>
        new() { PreviousTime = frame.NextTime, NextTime = frame.NextTime + frame.DeltaTime, DeltaTime = frame.DeltaTime };

    // KSA 5638 PhysicsBubble.cs:1051-1078 (DownwardSpeedLost, DetectKittenImpact), vertical axis only.
    internal static double DownwardSpeedLost(double previousVelocity, double velocity) =>
        Math.Max(0.0, -previousVelocity) - Math.Max(0.0, -velocity);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool DetectKittenImpact(VehicleUpdateState vehicleState, SimStep frame)
    {
        double velocity = vehicleState.VerticalSurfaceVelocity;
        double? previous = vehicleState.KittenSurfaceVelocityTime == frame.PreviousTime
            ? vehicleState.KittenSurfaceVelocity : null;
        vehicleState.KittenSurfaceVelocity = velocity;
        vehicleState.KittenSurfaceVelocityTime = frame.NextTime;
        Situation situation = vehicleState.Props.Situation;
        if (previous.HasValue && !vehicleState.OnLadder && (situation.HasTerrainContact() || situation.HasOceanContact()))
            return DownwardSpeedLost(previous.Value, velocity) >= 20.0;
        return false;
    }

    // KSA 5638 PhysicsBubble.cs:1080-1132 decision flow (5482:958 plus the SimStep parameter and the
    // kitten-impact branch that returns before the load telemetry is written). Descriptive local names
    // retain the native decision order and thresholds. Native-free fixture; production transpiler and
    // identity registry are linked unchanged. KsaAssemblyProbe proves this signature matches KSA.dll.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DetectStructuralFailure(VehicleUpdateState vehicleState, SimStep frame)
    {
        if (vehicleState.DestructionEvent != null) return;
        if (vehicleState.IsKitten && DetectKittenImpact(vehicleState, frame))
        {
            Situation impactSituation = vehicleState.GetReadOnlyProps().Situation;
            vehicleState.DestructionEvent = new VehicleDestructionEvent
            {
                Cause = !impactSituation.HasTerrainContact() ? VehicleDestructionCause.OceanImpact : VehicleDestructionCause.GroundImpact,
                PeakGLoad = (float)vehicleState.PeakGLoad,
                PeakDynamicPressure = (float)vehicleState.PeakDynamicPressure
            };
            return;
        }
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
