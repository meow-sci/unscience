using Brutal.Numerics;

namespace MeowSci.GarrysTorchLib;

/// <summary>Shared scale constraints and helpers for weld state, UI, presets, and RPC callers.</summary>
public static class WeldScale
{
    // Drag-widget bounds only. Typed values, APIs and presets may exceed this range.
    public const float Minimum = 0.05f;
    public const float Maximum = 20f;

    public static readonly float3 Identity = new(1f, 1f, 1f);

    public static float3 Uniform(float factor) => new(factor, factor, factor);

    public static bool IsValid(float3 scale) =>
        IsValidAxis(scale.X) && IsValidAxis(scale.Y) && IsValidAxis(scale.Z);

    public static bool Equals(float3 left, float3 right) =>
        left.X == right.X && left.Y == right.Y && left.Z == right.Z;

    private static bool IsValidAxis(float value) =>
        float.IsFinite(value) && value > 0f;
}
