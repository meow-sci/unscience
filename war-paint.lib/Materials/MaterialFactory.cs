using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.WarPaintLib.Materials;

/// <summary>
/// Creates unique GPU material instances for each spawned kitten.
/// Each material is a clone of the base character material with a custom
/// AlbedoColor tint. Materials are registered in the GpuMaterialSystem
/// with unique names to prevent conflicts.
/// Slot release lives in MaterialFactory.Lifetime.cs.
/// </summary>
public sealed partial class MaterialFactory
{
    private int _nextMaterialId;
    private readonly List<KittenMaterialSet> _createdSets = new();

    public IReadOnlyList<KittenMaterialSet> CreatedSets => _createdSets;

    /// <summary>
    /// Clones every unique material handle in the given array, creating per-kitten
    /// unique GPU materials with the specified AlbedoColor tint.
    /// Returns a KittenMaterialSet containing the old→new handle mapping,
    /// or null on failure.
    /// </summary>
    public KittenMaterialSet? CloneAllMaterials(int[] materialIndices, float4 tintColor)
    {
        if (!MaterialSystemAccessor.IsInitialized && !MaterialSystemAccessor.Initialize())
        {
            Console.WriteLine($"war-paint: CloneAllMaterials — MaterialSystemAccessor not initialized: {MaterialSystemAccessor.LastError}");
            return null;
        }

        var pendingAssets = new List<string>();
        try
        {
            string prefix = NextPrefix();

            // Build handle→name reverse lookup from AssetMap
            var handleToName = BuildHandleToNameMap();

            // Defaults for MaterialData construction
            int samplerHandle = GetSamplerRepeatHandle();
            int defaultWhiteHandle = GetDefaultWhiteTextureHandle();
            int defaultBlackHandle = GetDefaultBlackTextureHandle();

            // Clone each unique handle
            var uniqueHandles = materialIndices.Distinct().ToArray();
            var handleMap = new Dictionary<int, int>(); // old handle → new handle
            var allNewHandles = new List<int>();
            var assetNames = new List<string>();

            for (int idx = 0; idx < uniqueHandles.Length; idx++)
            {
                int oldHandle = uniqueHandles[idx];
                if (oldHandle < 0) continue;

                string cloneName = $"{prefix}_m{idx}";
                int newHandle = CloneSingleMaterial(oldHandle, cloneName, tintColor,
                    handleToName, samplerHandle, defaultWhiteHandle, defaultBlackHandle);
                if (MaterialSystemAccessor.GetMaterialIdentity(cloneName) != null) pendingAssets.Add(cloneName);

                if (newHandle >= 0)
                {
                    handleMap[oldHandle] = newHandle;
                    allNewHandles.Add(newHandle);
                    assetNames.Add(cloneName);
                }
                else
                {
                    Console.WriteLine($"war-paint: Failed to clone handle {oldHandle}, keeping shared.");
                }
            }

            if (handleMap.Count != uniqueHandles.Count(h => h >= 0))
            {
                Console.WriteLine($"war-paint: CloneAllMaterials — incomplete set '{prefix}', leaving original bindings intact.");
                ReleaseUnboundAssets(pendingAssets);
                return null;
            }

            var matSet = new KittenMaterialSet(prefix, tintColor);
            matSet.HandleMap = handleMap;
            matSet.AllMaterialHandles.AddRange(allNewHandles);
            matSet.AssetNames.AddRange(assetNames);
            foreach (string name in matSet.AssetNames)
                if (MaterialSystemAccessor.GetMaterialIdentity(name) is { } identity)
                    matSet.AssetIdentities[name] = identity;

            // Populate per-material entries for individual color editing
            foreach (var (oldHandle, newHandle) in handleMap)
            {
                string sourceName = handleToName.TryGetValue(oldHandle, out var n) ? n : $"handle_{oldHandle}";
                matSet.Materials.Add(new MaterialEntry
                {
                    Name = sourceName,
                    Handle = newHandle,
                    Color = tintColor
                });
            }

            _createdSets.Add(matSet);
            Console.WriteLine($"war-paint: Cloned {handleMap.Count}/{uniqueHandles.Length} materials for '{prefix}'");
            return matSet;
        }
        catch (Exception ex)
        {
            ReleaseUnboundAssets(pendingAssets);
            Console.WriteLine($"war-paint: CloneAllMaterials error: {ex.Message}");
            return null;
        }
    }

    private static void ReleaseUnboundAssets(IEnumerable<string> names)
    {
        foreach (string name in names)
            if (MaterialSystemAccessor.GetMaterialIdentity(name) is { } identity)
                MaterialSystemAccessor.DestroyMaterial(name, identity);
    }

    /// <summary>
    /// Clones a single GPU material by its handle.
    /// Looks up the source PbrMaterialReference via name, reconstructs MaterialData
    /// with the same textures but a custom AlbedoColor, and uploads it.
    /// </summary>
    private int CloneSingleMaterial(int sourceHandle, string newName, float4 tintColor,
        Dictionary<int, string> handleToName,
        int samplerHandle, int defaultWhiteHandle, int defaultBlackHandle)
    {
        // Find the material name for this handle
        if (!handleToName.TryGetValue(sourceHandle, out string? materialName) || materialName == null)
        {
            Console.WriteLine($"war-paint: No name found for handle {sourceHandle}, creating blank tinted material.");
            return CreateBlankTintedMaterial(newName, tintColor, samplerHandle, defaultWhiteHandle, defaultBlackHandle);
        }

        // Try to look up PbrMaterialReference from ModLibrary to get texture info
        PbrMaterialReference? pbrRef = null;
        try { pbrRef = ModLibrary.Get<PbrMaterialReference>(materialName); } catch { }

        if (pbrRef != null)
        {
            // Follow the resolved data link to follow references
            var resolved = GameDataReference.Resolve(pbrRef) as PbrMaterialReference ?? pbrRef;
            return CreateMaterialFromPbrRef(newName, resolved, tintColor,
                samplerHandle, defaultWhiteHandle, defaultBlackHandle);
        }

        // Not a PbrMaterialReference — could be a GLTF inline material.
        // Create with default white textures + tint (texture detail lost but tint works)
        Console.WriteLine($"war-paint: Material '{materialName}' not in ModLibrary, creating tinted fallback.");
        return CreateBlankTintedMaterial(newName, tintColor, samplerHandle, defaultWhiteHandle, defaultBlackHandle);
    }

    /// <summary>
    /// Creates a MaterialData from a resolved PbrMaterialReference, with custom AlbedoColor.
    /// Matches the game's GpuMaterialSystem.CreateAsset() pattern.
    /// </summary>
    private int CreateMaterialFromPbrRef(string name, PbrMaterialReference pbrRef, float4 tintColor,
        int samplerHandle, int defaultWhiteHandle, int defaultBlackHandle)
    {
        int albedoTex = GetTextureBindlessHandle(pbrRef.DiffuseReference, defaultWhiteHandle);
        int normalTex = GetTextureBindlessHandle(pbrRef.NormalReference, defaultWhiteHandle);
        int pbrTex = GetTextureBindlessHandle(pbrRef.PBRMap, defaultWhiteHandle);
        int emissiveTex = GetTextureBindlessHandle(pbrRef.EmissiveMap, defaultBlackHandle);

        var matData = new MaterialData
        {
            AlbedoTexture = albedoTex,
            NormalTexture = normalTex,
            RoughMetallicAOTexture = pbrTex,
            Sampler = samplerHandle,
            AlbedoColor = tintColor,
            RoughnessMetalScale = float4.One,
            EmissiveTexture = emissiveTex,
            ExtraData = float4.Zero
        };

        if (!MaterialSystemAccessor.CreateMaterial(name, matData))
            return -1;
        return MaterialSystemAccessor.GetExistingMaterialHandle(name);
    }

    /// <summary>
    /// Gets the bindless handle from a TextureReference field on PbrMaterialReference,
    /// following the resolved DataReference.Value link.
    /// </summary>
    private int GetTextureBindlessHandle(object? textureRef, int fallback)
    {
        if (textureRef == null) return fallback;
        try
        {
            var resolved = GameDataReference.Resolve(textureRef);
            if (resolved == null) return fallback;
            int handle = GetIntFieldOrProp(resolved, "BindlessHandle");
            return handle >= 0 ? handle : fallback;
        }
        catch { return fallback; }
    }

    private int CreateBlankTintedMaterial(string name, float4 tintColor,
        int samplerHandle, int defaultWhiteHandle, int defaultBlackHandle)
    {
        var matData = new MaterialData
        {
            AlbedoTexture = defaultWhiteHandle,
            NormalTexture = defaultWhiteHandle,
            RoughMetallicAOTexture = defaultWhiteHandle,
            Sampler = samplerHandle,
            AlbedoColor = tintColor,
            RoughnessMetalScale = float4.One,
            EmissiveTexture = defaultBlackHandle,
            ExtraData = float4.Zero
        };

        if (!MaterialSystemAccessor.CreateMaterial(name, matData))
            return -1;
        return MaterialSystemAccessor.GetExistingMaterialHandle(name);
    }

    private Dictionary<int, string> BuildHandleToNameMap()
    {
        var map = new Dictionary<int, string>();
        var allMats = MaterialSystemAccessor.GetAllMaterials();
        foreach (var (matName, handle) in allMats)
        {
            if (handle >= 0 && !map.ContainsKey(handle))
                map[handle] = matName;
        }
        return map;
    }

}
