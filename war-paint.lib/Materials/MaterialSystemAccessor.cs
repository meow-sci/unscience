using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;

namespace MeowSci.WarPaintLib.Materials;

/// <summary>
/// Reflection bridge to the game's GpuMaterialSystem and GpuTextureSystem.
/// Enables runtime creation of materials and direct GPU buffer writes.
/// Uses the extracted DOH staged Vulkan upload path.
/// Pool capacity, reserve and release live in MaterialSystemAccessor.Pool.cs.
/// </summary>
public static partial class MaterialSystemAccessor
{
    private static bool _initialized;
    private static string? _lastError;

    // Cached reflection handles — material system
    private static object? _materialSystem;
    private static IDictionary? _assetMap;
    private static PropertyInfo? _bigBufferProp;
    private static FieldInfo? _deviceCtxField;
    private static MethodInfo? _createObjectMethod;
    private static MethodInfo? _getOrLoadMethod;

    // Cached reflection handles — texture system
    private static object? _textureSystem;
    private static MethodInfo? _textureGetOrLoadMethod;

    public static bool IsInitialized => _initialized && _materialSystem != null && _assetMap is { Count: > 0 };
    public static string? LastError => _lastError;

    /// <summary>
    /// Discovers Program.Instance → MaterialSystem, TextureSystem; caches all reflection handles.
    /// Safe to call multiple times — returns immediately if already initialized.
    /// </summary>
    public static bool Initialize()
    {
        if (IsInitialized) return true;
        _lastError = null;

        try
        {
            // Step 1: Get Program.Instance
            var programType = typeof(Part).Assembly.GetType("KSA.Program");
            if (programType == null) { _lastError = "KSA.Program type not found."; return false; }

            var instanceProp = programType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProp == null) { _lastError = "Program.Instance not found."; return false; }

            var programInstance = instanceProp.GetValue(null);
            if (programInstance == null) { _lastError = "Program.Instance is null (game not fully loaded?)."; return false; }

            // Step 2: Get MaterialSystem
            _materialSystem = GetFieldOrProp(programType, programInstance, "MaterialSystem");
            if (_materialSystem == null) { _lastError = "MaterialSystem not found on Program."; return false; }

            // Step 3: Get AssetMap (inherited from AssetManager)
            _assetMap = FindFieldInHierarchy(_materialSystem, "AssetMap") as IDictionary;
            if (_assetMap == null) { _lastError = "AssetMap not found in MaterialSystem hierarchy."; return false; }
            if (_assetMap.Count == 0) { _lastError = "Native material assets are not available."; return false; }

            // Step 4: Get BigBuffer property (for GPU writes)
            _bigBufferProp = _materialSystem.GetType().GetProperty("BigBuffer",
                BindingFlags.Public | BindingFlags.Instance);

            // Step 5: Get DeviceCtx field (for Vulkan staging)
            _deviceCtxField = FindFieldInfoInHierarchy(_materialSystem.GetType(), "DeviceCtx");

            // Step 6: Get CreateObject method
            _createObjectMethod = FindMethodInHierarchy(_materialSystem.GetType(), "CreateObject");

            // Step 7: Get GetOrLoad method
            _getOrLoadMethod = FindMethodInHierarchy(_materialSystem.GetType(), "GetOrLoad");

            // Step 7b: Get the fixed-size GPU slot allocator (for capacity/budget checks)
            _bigBufferAllocatorField = FindFieldInfoInHierarchy(_materialSystem.GetType(), "BigBufferAllocator");

            // Step 8: Get TextureSystem
            var superMeshRenderSystem = GetFieldOrProp(programType, programInstance, "SuperMeshRenderSystem");
            if (superMeshRenderSystem != null)
            {
                _textureSystem = GetFieldOrProp(superMeshRenderSystem.GetType(), superMeshRenderSystem, "TextureSystem");
                if (_textureSystem != null)
                {
                    _textureGetOrLoadMethod = FindMethodInHierarchy(_textureSystem.GetType(), "GetOrLoad");
                }
            }

            _initialized = true;
            int materialCount = _assetMap.Count;
            Console.WriteLine($"war-paint: MaterialSystemAccessor initialized — {materialCount} materials, " +
                $"CreateObject={_createObjectMethod != null}, GetOrLoad={_getOrLoadMethod != null}, " +
                $"TextureSystem={_textureSystem != null}, slots free {GetFreeSlotCount()}/{GetCapacity()}");
            return true;
        }
        catch (Exception ex)
        {
            _lastError = $"Init error: {ex.Message}";
            Console.WriteLine($"war-paint: MaterialSystemAccessor {_lastError}");
            return false;
        }
    }

    /// <summary>
    /// Creates a new material in the GPU buffer with the given name and data.
    /// Returns true if successful.
    /// </summary>
    public static bool CreateMaterial(string assetName, MaterialData data)
    {
        if (!IsInitialized || _materialSystem == null || _createObjectMethod == null)
        {
            _lastError = "MaterialSystem or CreateObject not available.";
            return false;
        }

        // Never let doh consume the slots the game itself needs. The pool is fixed-size and an
        // exhausted pool makes the game's own allocations (e.g. every KittenEva's fur material) throw.
        int freeSlots = GetFreeSlotCount();
        if (freeSlots >= 0 && freeSlots <= ReservedSlots)
        {
            _lastError = $"GPU material pool nearly full ({freeSlots} of {GetCapacity()} slots free, " +
                $"{ReservedSlots} reserved for the game); not creating '{assetName}'.";
            Console.WriteLine($"war-paint: {_lastError}");
            return false;
        }

        try
        {
            var result = _createObjectMethod.Invoke(_materialSystem, new object[] { (AssetName)assetName, data });
            bool success = result is bool b && b;
            if (success)
                MeowSci.KsaAbstractions.Persistence.MaterialColorState.Record(GetExistingMaterialHandle(assetName), data.AlbedoColor);
            if (!success)
                _lastError = $"CreateObject returned false for '{assetName}' — name may already exist.";
            return success;
        }
        catch (Exception ex)
        {
            _lastError = $"CreateMaterial error for '{assetName}': {ex.InnerException?.Message ?? ex.Message}";
            Console.WriteLine($"war-paint: {_lastError}");
            return false;
        }
    }

    /// <summary>Resets all cached state. Call on mod unload.</summary>
    public static void Cleanup()
    {
        _initialized = false;
        _materialSystem = null;
        _assetMap = null;
        _bigBufferProp = null;
        _deviceCtxField = null;
        _createObjectMethod = null;
        _getOrLoadMethod = null;
        _bigBufferAllocatorField = null;
        _textureSystem = null;
        _textureGetOrLoadMethod = null;
        _lastError = null;
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

    private static MethodInfo? FindMethodInHierarchy(Type? type, string methodName)
    {
        while (type != null)
        {
            var method = type.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (method != null) return method;
            type = type.BaseType;
        }
        return null;
    }
}
