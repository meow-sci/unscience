using Brutal.Numerics;

namespace MeowSci.DohLib.Spawning;

/// <summary>
/// Parameters for spawning one or more kittens.
/// Supports relative-to-vehicle positioning (with offset) or absolute orbital state.
/// </summary>
public sealed class SpawnRequest
{
    // ---- Positioning Mode 1: Relative to vehicle ----

    /// <summary>
    /// Reference vehicle object. Preferred over <see cref="ReferenceVehicleId"/> when the caller
    /// holds the vehicle (e.g. the UI selection), so the target can never be re-resolved to a
    /// different vehicle by name.
    /// </summary>
    public KSA.Vehicle? ReferenceVehicle { get; init; }

    /// <summary>
    /// Reference vehicle ID. The kitten will be spawned near this vehicle.
    /// Used when <see cref="ReferenceVehicle"/> is null; an ambiguous ID is rejected.
    /// If both are null, PositionCci/VelocityCci/ParentBodyName must be provided.
    /// </summary>
    public string? ReferenceVehicleId { get; init; }

    /// <summary>
    /// Offset in the reference vehicle's body frame (meters).
    /// X = right, Y = up, Z = forward.
    /// Default: (0, 0, 10) = 10 meters ahead.
    /// </summary>
    public double3 OffsetBodyFrame { get; init; } = new double3(0, 0, 10);

    // ---- Positioning Mode 2: Absolute orbital state ----

    /// <summary>Absolute position in CCI frame of the parent body (meters).</summary>
    public double3? PositionCci { get; init; }

    /// <summary>Absolute velocity in CCI frame (m/s).</summary>
    public double3? VelocityCci { get; init; }

    /// <summary>Parent celestial body name (e.g., "Caturn"). Required for absolute positioning.</summary>
    public string? ParentBodyName { get; init; }

    // ---- Batch Spawning ----

    /// <summary>
    /// Number of kittens to spawn. Each subsequent kitten is offset further.
    /// Default: 1.
    /// </summary>
    public int Count { get; init; } = 1;

    // ---- Character & Material ----

    /// <summary>
    /// Character reference ID (e.g., "Calico"). If null, a random character is selected.
    /// </summary>
    public string? CharacterId { get; init; }

    /// <summary>
    /// Custom material tint color as RGBA float4.
    /// float4(1,1,1,1) = no tint.
    /// If null, no custom materials are created.
    /// </summary>
    public float4? TintColor { get; init; }

    /// <summary>
    /// When spawning multiple kittens (Count > 1), whether each gets unique materials.
    /// If false (and no PerKittenColors), they share one custom material set, which uses one
    /// set of GPU material slots instead of one per kitten.
    /// </summary>
    public bool UniqueMaterialsPerKitten { get; init; }

    /// <summary>
    /// Per-kitten colors when UniqueMaterialsPerKitten is true.
    /// If shorter than Count, remaining kittens use TintColor.
    /// </summary>
    public float4[]? PerKittenColors { get; init; }
}
