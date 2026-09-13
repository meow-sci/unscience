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

namespace MeowSci.DohLib.Materials;

/// <summary>
/// Reflection bridge to the game's GpuMaterialSystem and GpuTextureSystem.
/// Enables runtime creation of materials and direct GPU buffer writes.
/// Pattern based on humble-arteest.lib/KittenColor.cs.
/// </summary>
public static class MaterialSystemAccessor
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

    public static bool IsInitialized => _initialized && _materialSystem != null;
    public static string? LastError => _lastError;

    /// <summary>
    /// Discovers Program.Instance → MaterialSystem, TextureSystem; caches all reflection handles.
    /// Safe to call multiple times — returns immediately if already initialized.
    /// </summary>
    public static bool Initialize()
    {
        if (_initialized && _materialSystem != null) return true;
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

            // Step 4: Get BigBuffer property (for GPU writes)
            _bigBufferProp = _materialSystem.GetType().GetProperty("BigBuffer",
                BindingFlags.Public | BindingFlags.Instance);

            // Step 5: Get DeviceCtx field (for Vulkan staging)
            _deviceCtxField = FindFieldInfoInHierarchy(_materialSystem.GetType(), "DeviceCtx");

            // Step 6: Get CreateObject method
            _createObjectMethod = FindMethodInHierarchy(_materialSystem.GetType(), "CreateObject");

            // Step 7: Get GetOrLoad method
            _getOrLoadMethod = FindMethodInHierarchy(_materialSystem.GetType(), "GetOrLoad");

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
            Console.WriteLine($"doh: MaterialSystemAccessor initialized — {materialCount} materials, " +
                $"CreateObject={_createObjectMethod != null}, GetOrLoad={_getOrLoadMethod != null}, " +
                $"TextureSystem={_textureSystem != null}");
            return true;
        }
        catch (Exception ex)
        {
            _lastError = $"Init error: {ex.Message}";
            Console.WriteLine($"doh: MaterialSystemAccessor {_lastError}");
            return false;
        }
    }

    /// <summary>
    /// Creates a new material in the GPU buffer with the given name and data.
    /// Returns true if successful.
    /// </summary>
    public static bool CreateMaterial(string assetName, MaterialData data)
    {
        if (_materialSystem == null || _createObjectMethod == null)
        {
            _lastError = "MaterialSystem or CreateObject not available.";
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
            Console.WriteLine($"doh: {_lastError}");
            return false;
        }
    }

    /// <summary>
    /// Gets the GPU buffer handle for a material by name via GetOrLoad.
    /// Returns -1 if not found.
    /// </summary>
    public static int GetMaterialHandle(string assetName)
    {
        if (_materialSystem == null || _getOrLoadMethod == null)
        {
            _lastError = "MaterialSystem or GetOrLoad not available.";
            return -1;
        }

        try
        {
            var assetRef = _getOrLoadMethod.Invoke(_materialSystem, new object[] { (AssetName)assetName });
            if (assetRef == null) return -1;

            var handleField = assetRef.GetType().GetField("Handle",
                BindingFlags.Public | BindingFlags.Instance);
            if (handleField == null) return -1;

            return (int)handleField.GetValue(assetRef)!;
        }
        catch (Exception ex)
        {
            _lastError = $"GetMaterialHandle error for '{assetName}': {ex.InnerException?.Message ?? ex.Message}";
            return -1;
        }
    }

    /// <summary>
    /// Looks up a material handle directly in AssetMap without triggering a load.
    /// Returns -1 if not found.
    /// </summary>
    public static int GetExistingMaterialHandle(string materialName)
    {
        if (_assetMap == null) return -1;

        try
        {
            var key = (AssetName)materialName;
            if (!_assetMap.Contains(key)) return -1;

            var assetRef = _assetMap[key];
            if (assetRef == null) return -1;

            var handleField = assetRef.GetType().GetField("Handle",
                BindingFlags.Public | BindingFlags.Instance);
            if (handleField == null) return -1;

            return (int)handleField.GetValue(assetRef)!;
        }
        catch (Exception ex)
        {
            _lastError = $"GetExistingMaterialHandle error: {ex.Message}";
            return -1;
        }
    }

    /// <summary>
    /// Resolves a texture name to its bindless GPU handle via TextureSystem.GetOrLoad.
    /// Returns -1 if not found.
    /// </summary>
    public static int GetTextureBindlessHandle(string textureName)
    {
        if (_textureSystem == null || _textureGetOrLoadMethod == null)
        {
            _lastError = "TextureSystem not available.";
            return -1;
        }

        try
        {
            var textureRef = _textureGetOrLoadMethod.Invoke(_textureSystem, new object[] { (AssetName)textureName });
            if (textureRef == null) return -1;

            var bindlessField = textureRef.GetType().GetField("BindlessHandle",
                BindingFlags.Public | BindingFlags.Instance);
            if (bindlessField != null)
                return (int)bindlessField.GetValue(textureRef)!;

            var bindlessProp = textureRef.GetType().GetProperty("BindlessHandle",
                BindingFlags.Public | BindingFlags.Instance);
            if (bindlessProp != null)
                return (int)bindlessProp.GetValue(textureRef)!;

            return -1;
        }
        catch (Exception ex)
        {
            _lastError = $"GetTextureBindlessHandle error for '{textureName}': {ex.InnerException?.Message ?? ex.Message}";
            return -1;
        }
    }

    /// <summary>
    /// Returns all material names and handles from the AssetMap.
    /// </summary>
    public static (string Name, int Handle)[] GetAllMaterials()
    {
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
            return results.ToArray();
        }
        catch (Exception ex)
        {
            _lastError = $"GetAllMaterials error: {ex.Message}";
            return Array.Empty<(string, int)>();
        }
    }

    /// <summary>
    /// Writes the given AlbedoColor at the correct offset in the GPU material buffer
    /// for the given material handle, using staged Vulkan upload.
    /// Same implementation as KittenColor.WriteAlbedoColor().
    /// </summary>
    public static bool WriteAlbedoColor(int handle, float4 color)
    {
        if (_materialSystem == null || _bigBufferProp == null || _deviceCtxField == null)
        {
            _lastError = "MaterialSystem not fully initialized for GPU writes.";
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

            MeowSci.KsaAbstractions.Persistence.MaterialColorState.Record(handle, color);

            return true;
        }
        catch (Exception ex)
        {
            _lastError = $"WriteAlbedoColor error for handle {handle}: {ex.Message}";
            Console.WriteLine($"doh: {_lastError}");
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
        _textureSystem = null;
        _textureGetOrLoadMethod = null;
        _lastError = null;
    }

    // ---- Reflection helpers (same pattern as KittenColor.cs) ----

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
