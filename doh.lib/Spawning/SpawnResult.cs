using System;
using Brutal.Numerics;

namespace MeowSci.DohLib.Spawning;

/// <summary>Result of a single kitten spawn operation.</summary>
public sealed class SpawnedKittenInfo
{
    /// <summary>The kitten's vehicle ID in the game.</summary>
    public string KittenId { get; init; } = "";

    /// <summary>The character reference ID used.</summary>
    public string CharacterId { get; init; } = "";

    /// <summary>Material set ID if custom materials were created.</summary>
    public string? MaterialSetId { get; init; }

    /// <summary>The tint color applied, or null if using defaults.</summary>
    public float4? TintColor { get; init; }

    /// <summary>Position in CCI frame after spawn (meters).</summary>
    public double3 PositionCci { get; init; }

    /// <summary>Velocity in CCI frame after spawn (m/s).</summary>
    public double3 VelocityCci { get; init; }

    /// <summary>Parent celestial body name.</summary>
    public string ParentBodyName { get; init; } = "";
}

/// <summary>Result of a batch spawn operation.</summary>
public sealed class SpawnResult
{
    /// <summary>Whether the overall operation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Error message if the operation failed.</summary>
    public string? Error { get; init; }

    /// <summary>Non-fatal problem on a successful spawn (e.g. some kittens could not be tinted).</summary>
    public string? Warning { get; init; }

    /// <summary>Info about each kitten that was spawned (also set when a batch stopped part-way).</summary>
    public SpawnedKittenInfo[] SpawnedKittens { get; init; } = Array.Empty<SpawnedKittenInfo>();

    /// <summary>Total number of kittens spawned.</summary>
    public int Count => SpawnedKittens.Length;

    public static SpawnResult Failure(string error) => new() { Success = false, Error = error };
}
