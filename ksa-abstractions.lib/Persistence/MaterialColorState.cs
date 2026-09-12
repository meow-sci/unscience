using System.Collections.Generic;
using Brutal.Numerics;

namespace MeowSci.KsaAbstractions.Persistence;

/// <summary>Tracks known successful mod-owned GPU albedo writes; handles are runtime-only, never save identities.</summary>
public static class MaterialColorState
{
    private static readonly Dictionary<int, float4> Colors = new();
    public static void Record(int handle, float4 color) { if (handle >= 0) Colors[handle] = color; }
    public static float4 GetOrDefault(int handle, float4 fallback) => Colors.TryGetValue(handle, out var value) ? value : fallback;
}
