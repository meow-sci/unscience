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

public static partial class MaterialSystemAccessor
{
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
                if (entry.Value is GpuObjectAssetRef { IsValid: false }) continue;
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
    /// Preserves the staged upload behavior used by the original DOH implementation.
    /// </summary>
    public static bool WriteAlbedoColor(int handle, float4 color)
    {
        if (!IsInitialized || _materialSystem == null || _bigBufferProp == null || _deviceCtxField == null
            || !Array.Exists(GetAllMaterials(), material => material.Handle == handle))
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
            Console.WriteLine($"war-paint: {_lastError}");
            return false;
        }
    }

}
