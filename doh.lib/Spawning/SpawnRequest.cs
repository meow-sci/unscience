using Brutal.Numerics;
using MeowSci.WarPaintLib;

namespace MeowSci.DohLib.Spawning;

/// <summary>
/// Parameters for spawning one or more kittens.
/// Supports relative-to-vehicle positioning (with offset) or absolute orbital state.
/// </summary>
public sealed class SpawnRequest
{
    /// <summary>Default prefix for automatically numbered kitten names.</summary>
    public const string DefaultNamePrefix = "Kitten_doh_";

    /// <summary>
    /// Preferred vehicle name for a single spawn, or a prefix for numbered batch names.
    /// Null, empty or whitespace uses the default prefix with automatic numbering.
    /// Occupied single names advance their trailing number (or gain one) above existing suffixes.
    /// </summary>
    public string? VehicleName { get; init; }

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

    public static double3 DefaultOffsetBodyFrame => new(1, 1, -2);

    /// <summary>
    /// Offset in the reference vehicle's body frame (meters).
    /// X = right, Y = up, Z = forward.
    /// Default: (1, 1, -2) meters.
    /// </summary>
    public double3 OffsetBodyFrame { get; init; } = DefaultOffsetBodyFrame;

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

    /// <summary>Saved War Paint preset name. Unknown names are rejected before spawning.</summary>
    public string? PaintPresetName { get; init; }

    private PaintSettings? _paintSettings;
    /// <summary>
    /// Detached preset snapshot, preferred over resolving the name at execution time. Both assignment
    /// and retrieval clone the recipe so deferred callers cannot mutate an already prepared request.
    /// </summary>
    public PaintSettings? PaintSettings
    {
        get => _paintSettings?.Clone();
        init => _paintSettings = value?.Clone();
    }

    /// <summary>
    /// Legacy API: forwarded to War Paint, with private materials for every kitten.
    /// Custom material tint color as RGBA float4.
    /// float4(1,1,1,1) = no tint.
    /// If null, no custom materials are created.
    /// </summary>
    public float4? TintColor { get; init; }

    /// <summary>
    /// Legacy compatibility flag. War Paint always gives every kitten private materials; this flag
    /// remains accepted so older callers do not break, but no longer enables shared mutable sets.
    /// </summary>
    public bool UniqueMaterialsPerKitten { get; init; }

    /// <summary>
    /// Legacy per-kitten colors. Ignored when a War Paint preset or snapshot is supplied.
    /// If shorter than Count, remaining kittens use TintColor.
    /// </summary>
    public float4[]? PerKittenColors { get; init; }

    /// <summary>New preset recipes take precedence over the legacy tint fields.</summary>
    internal PaintSettings? GetPaintForKitten(PaintSettings? preset, int index)
    {
        if (preset != null) return preset.Clone();
        if (PerKittenColors != null && index >= 0 && index < PerKittenColors.Length)
            return new PaintSettings { Tint = PerKittenColors[index] };
        return TintColor.HasValue || PerKittenColors != null
            ? new PaintSettings { Tint = TintColor ?? float4.One } : null;
    }
}
