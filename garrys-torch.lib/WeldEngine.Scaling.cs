using System;
using System.Runtime.CompilerServices;
using Brutal.Numerics;
using KSA;

namespace MeowSci.GarrysTorchLib;

public static partial class WeldEngine
{
    private static readonly ConditionalWeakTable<Vehicle, WeldScaleSnapshot> ScaleSnapshots = new();

    internal static void CaptureVehicleScale(Vehicle vehicle) =>
        ScaleSnapshots.GetValue(vehicle, source => new WeldScaleSnapshot(source));

    /// <summary>
    /// Applies local XYZ multipliers relative to the source's captured full-part scales.
    /// Subparts inherit the result without overwriting their authored/animated local scales.
    /// Call RestoreVehicleScale to end the scale session (also required for low-level callers).
    /// </summary>
    public static void ApplyVehicleScale(Vehicle vehicle, float3 scale)
    {
        if (!WeldScale.IsValid(scale)) throw new ArgumentOutOfRangeException(nameof(scale));
        if (vehicle.IsDisposed) return;
        ScaleSnapshots.GetValue(vehicle, source => new WeldScaleSnapshot(source)).Apply(scale);
    }

    /// <summary>Restores surviving original full parts and the avatar, then releases the snapshot.</summary>
    public static void RestoreVehicleScale(Vehicle vehicle)
    {
        if (!ScaleSnapshots.TryGetValue(vehicle, out var snapshot)) return;
        snapshot.Restore();
        ScaleSnapshots.Remove(vehicle);
    }

    /// <summary>Backwards-compatible uniform-scale overload.</summary>
    public static void ApplyVehicleScale(Vehicle vehicle, float factor) =>
        ApplyVehicleScale(vehicle, WeldScale.Uniform(factor));

    /// <summary>
    /// Low-level absolute override, retained for compatibility. Does not capture originals;
    /// welding uses ApplyVehicleScale instead to preserve subpart scales and inheritance.
    /// </summary>
    public static void SetPartScaleRecursive(Part part, float3 scale)
    {
        part.Scale = new double3(scale.X, scale.Y, scale.Z);
        foreach (var sub in part.SubParts) SetPartScaleRecursive(sub, scale);
    }

    /// <summary>Backwards-compatible absolute uniform-scale overload.</summary>
    public static void SetPartScaleRecursive(Part part, float factor) =>
        SetPartScaleRecursive(part, WeldScale.Uniform(factor));
}
