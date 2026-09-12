using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using KSA;
using RenderCore;

namespace MeowSci.HumbleArteestLib;

/// <summary>
/// Core logic for the Kitten Coloring feature.
///
/// Uses reflection to access the GpuMaterialSystem and write AlbedoColor
/// into the GPU material buffer. ModelPbr.frag respects tint and alpha discard;
/// visor glass uses ModelTranslucent.frag with fixed opacity, so hiding it
/// requires the separate KittenVisorPatches draw gate.
///
/// Pattern validated in Experiments/MaterialColorTest.cs.
/// </summary>
public static partial class KittenColor
{
    private static bool _initialized;
    private static string? _lastError;
    private static string? _statusMessage;

    // Cached reflection handles
    private static object? _materialSystem;
    private static IDictionary? _assetMap;
    private static PropertyInfo? _bigBufferProp;
    private static FieldInfo? _deviceCtxField;

    // Cached material list
    private static (string Name, int Handle)[]? _cachedMaterials;

    public static bool IsInitialized => _initialized && _materialSystem != null;
    public static string? LastError => _lastError;
    public static string? StatusMessage => _statusMessage;

    /// <summary>
    /// Discovers the GpuMaterialSystem and its AssetMap via reflection.
    /// Safe to call multiple times — returns immediately if already initialized.
    /// </summary>
    public static bool Initialize()
    {
        if (_initialized) return _materialSystem != null;
        _initialized = true;
        _lastError = null;

        try
        {
            var programType = typeof(Part).Assembly.GetType("KSA.Program");
            if (programType == null) { _lastError = "KSA.Program type not found."; return false; }

            var instanceProp = programType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProp == null) { _lastError = "Program.Instance not found."; return false; }

            var programInstance = instanceProp.GetValue(null);
            if (programInstance == null) { _lastError = "Program.Instance is null (game not fully loaded?)."; return false; }

            _materialSystem = GetFieldOrProp(programType, programInstance, "MaterialSystem");
            if (_materialSystem == null) { _lastError = "MaterialSystem not found on Program."; return false; }

            _assetMap = FindFieldInHierarchy(_materialSystem, "AssetMap") as IDictionary;
            if (_assetMap == null) { _lastError = "AssetMap not found in MaterialSystem hierarchy."; return false; }

            _bigBufferProp = _materialSystem.GetType().GetProperty("BigBuffer",
                BindingFlags.Public | BindingFlags.Instance);

            _deviceCtxField = FindFieldInfoInHierarchy(_materialSystem.GetType(), "DeviceCtx");

            Console.WriteLine($"humble-arteest: KittenColor initialized — {_assetMap.Count} materials found");
            _statusMessage = $"Initialized: {_assetMap.Count} materials found";
            return true;
        }
        catch (Exception ex)
        {
            _lastError = $"Init error: {ex.Message}";
            Console.WriteLine($"humble-arteest: KittenColor {_lastError}");
            return false;
        }
    }

    /// <summary>Returns sorted material names and their GPU buffer handles.</summary>
    public static (string Name, int Handle)[] GetMaterials()
    {
        if (_cachedMaterials != null) return _cachedMaterials;
        if (_assetMap == null) return Array.Empty<(string, int)>();

        try
        {
            var results = new List<(string, int)>();
            foreach (DictionaryEntry entry in _assetMap)
            {
                string name = entry.Key?.ToString() ?? "unknown";
                int handle = -1;

                if (entry.Value != null)
                {
                    var handleField = entry.Value.GetType().GetField("Handle",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (handleField != null)
                        handle = (int)handleField.GetValue(entry.Value)!;
                }

                results.Add((name, handle));
            }

            results.Sort((a, b) => string.Compare(a.Item1, b.Item1, StringComparison.Ordinal));
            _cachedMaterials = results.ToArray();
            return _cachedMaterials;
        }
        catch (Exception ex)
        {
            _lastError = $"Error listing materials: {ex.Message}";
            return Array.Empty<(string, int)>();
        }
    }

    /// <summary>Invalidates the cached material list so the next call to GetMaterials() rebuilds it.</summary>
    public static void RefreshMaterialCache()
    {
        _cachedMaterials = null;
    }

    /// <summary>
    /// Writes the given AlbedoColor to ALL materials in the GPU buffer.
    /// </summary>
    public static bool ApplyToAll(float4 color)
    {
        _lastError = null;
        var materials = GetMaterials();
        if (materials.Length == 0)
        {
            _lastError = "No materials available.";
            return false;
        }

        int successCount = 0;
        foreach (var (name, handle) in materials)
        {
            if (handle < 0) continue;
            if (ApplyToMaterial(handle, color))
                successCount++;
        }

        if (successCount == 0)
        {
            _lastError = "Failed to write to any materials.";
            return false;
        }

        _statusMessage = $"Applied color to {successCount}/{materials.Length} materials.";
        Console.WriteLine($"humble-arteest: KittenColor {_statusMessage}");
        return true;
    }

    /// <summary>
    /// Resets the AlbedoColor to white (1,1,1,1) on all materials.
    /// </summary>
    public static bool ResetAll()
    {
        return ApplyToAll(new float4(1f, 1f, 1f, 1f));
    }

    /// <summary>
    /// Writes the given AlbedoColor to a single material identified by its GPU buffer handle.
    /// </summary>
    public static bool ApplyToMaterial(int handle, float4 color)
    {
        _lastError = null;
        if (handle < 0) { _lastError = "Invalid material handle."; return false; }
        var original = MeowSci.KsaAbstractions.Persistence.MaterialColorState.GetOrDefault(handle, float4.One);
        if (!WriteAlbedoColor(handle, color)) return false;
        TrackColor(handle, original, color);
        MeowSci.KsaAbstractions.Persistence.MaterialColorState.Record(handle, color);
        return true;
    }

    /// <summary>
    /// Resets a single material's AlbedoColor to white (1,1,1,1).
    /// </summary>
    public static bool ResetMaterial(int handle)
    {
        return ApplyToMaterial(handle, new float4(1f, 1f, 1f, 1f));
    }

    /// <summary>
    /// Writes a float4 AlbedoColor at the correct offset in the GPU material buffer
    /// for the given material handle, using staged Vulkan upload.
    /// </summary>
    private static bool WriteAlbedoColor(int handle, float4 color)
    {
        if (_materialSystem == null || _bigBufferProp == null || _deviceCtxField == null)
        {
            _lastError = "MaterialSystem not fully initialized.";
            return false;
        }

        try
        {
            var bigBuffer = (BufferEx)_bigBufferProp.GetValue(_materialSystem)!;
            var deviceCtx = (IVulkanContext)_deviceCtxField.GetValue(_materialSystem)!;

            int albedoColorOffset = (int)Marshal.OffsetOf<MaterialData>(nameof(MaterialData.AlbedoColor));
            ByteSize targetOffset = handle * ByteSize.Of<MaterialData>() + albedoColorOffset;

            using var stagingPool = deviceCtx.Device.CreateStagingPool(deviceCtx.MainQueue, 1);
            var commandBuffer = stagingPool.NextCommandBuffer();

            float4 colorCopy = color;
            var span = new Span<float4>(ref colorCopy);

            commandBuffer.Begin();
            VkUtils.StageAndUploadToBuffer(stagingPool, bigBuffer.VkBuffer, targetOffset, MemoryMarshal.AsBytes(span), commandBuffer);
            commandBuffer.End();

            return true;
        }
        catch (Exception ex)
        {
            _lastError = $"Error writing material handle {handle}: {ex.Message}";
            Console.WriteLine($"humble-arteest: KittenColor {_lastError}");
            return false;
        }
    }

    /// <summary>Resets initialization state. Call on mod unload.</summary>
    public static void Cleanup()
    {
        ResetOwnedColors();
        _initialized = false;
        _materialSystem = null;
        _assetMap = null;
        _bigBufferProp = null;
        _deviceCtxField = null;
        _cachedMaterials = null;
        _lastError = null;
        _statusMessage = null;
    }

    // ---- Reflection helpers ----

    private static object? GetFieldOrProp(Type type, object instance, string name)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null) return field.GetValue(instance);

        var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (prop != null) return prop.GetValue(instance);

        return null;
    }

    private static object? FindFieldInHierarchy(object instance, string fieldName)
    {
        var type = instance.GetType();
        while (type != null)
        {
            var field = type.GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null) return field.GetValue(instance);
            type = type.BaseType;
        }
        return null;
    }

    private static FieldInfo? FindFieldInfoInHierarchy(Type? type, string fieldName)
    {
        while (type != null)
        {
            var field = type.GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null) return field;
            type = type.BaseType;
        }
        return null;
    }
}
