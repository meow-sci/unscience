using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Brutal.Numerics;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.HumbleArteestLib;

public static partial class KittenColor
{
    private sealed record OriginalMaterial(object Key, string Name, object Asset, float4 Color);
    private static readonly Dictionary<int, OriginalMaterial> OriginalColors = new();
    private static readonly Dictionary<string, float4> AppliedColors = new(StringComparer.Ordinal);

    private static void TrackColor(int handle, float4 original, float4 color)
    {
        if (_assetMap == null) return;
        foreach (DictionaryEntry material in _assetMap)
        {
            string? name = material.Key?.ToString();
            if (name == null || material.Key == null || material.Value == null || ReadHandle(material.Value) != handle) continue;
            if (!OriginalColors.TryGetValue(handle, out var previous) || !ReferenceEquals(previous.Asset, material.Value))
                OriginalColors[handle] = new OriginalMaterial(material.Key, name, material.Value, original);
            AppliedColors[name] = color;
            break;
        }
    }

    private static int? ReadHandle(object asset) => asset.GetType().GetField("Handle", BindingFlags.Public | BindingFlags.Instance)?.GetValue(asset) as int?;

    internal static Dictionary<string, float4> CaptureColors()
    {
        var result = new Dictionary<string, float4>(StringComparer.Ordinal);
        foreach (var pair in AppliedColors)
        {
            // DOH private slots have process-local names. Its participant records the actual
            // tracked color under kitten/material identity and reconstructs them itself.
            if (!pair.Key.StartsWith("doh_", StringComparison.Ordinal)
                && !pair.Key.StartsWith("free-fallin/", StringComparison.Ordinal)) result[pair.Key] = pair.Value;
        }
        return result;
    }

    internal static void ResetOwnedColors()
    {
        foreach (var pair in OriginalColors)
        {
            var original = pair.Value;
            // Renderer-owned assets can be disposed and their integer slot reused between edits.
            if (_assetMap == null || !ReferenceEquals(_assetMap[original.Key], original.Asset)
                || ReadHandle(original.Asset) != pair.Key) continue;
            if (!WriteAlbedoColor(pair.Key, original.Color))
                throw new InvalidOperationException(LastError ?? "Could not restore a material's original color.");
            MaterialColorState.Record(pair.Key, original.Color);
        }
        OriginalColors.Clear();
        AppliedColors.Clear();
    }

    internal static void RestoreColors(Dictionary<string, float4> colors, SaveRestoreContext context)
    {
        if (colors.Count == 0) return;
        if (!Initialize()) { context.Warn(LastError ?? "Kitten material system unavailable."); return; }
        RefreshMaterialCache();
        var materials = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in GetMaterials()) materials[item.Name] = item.Handle;
        foreach (var item in colors)
        {
            if (!materials.TryGetValue(item.Key, out int handle))
            { context.Warn($"Kitten material missing: {item.Key}."); continue; }
            if (!ApplyToMaterial(handle, item.Value)) context.Warn(LastError ?? $"Kitten material upload failed: {item.Key}.");
        }
    }
}
