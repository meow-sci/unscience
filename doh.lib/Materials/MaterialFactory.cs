using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Brutal.Numerics;
using KSA;

namespace MeowSci.DohLib.Materials;

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
    /// Creates a tinted material set for a kitten using the specified character's textures.
    /// Resolves PbrMaterialReference textures via reflection and constructs new MaterialData
    /// structs with the same textures but a custom AlbedoColor.
    /// </summary>
    public KittenMaterialSet? CreateTintedMaterialSet(string characterId, float4 tintColor)
    {
        if (!MaterialSystemAccessor.IsInitialized && !MaterialSystemAccessor.Initialize())
        {
            Console.WriteLine($"doh: MaterialFactory — MaterialSystemAccessor not initialized: {MaterialSystemAccessor.LastError}");
            return null;
        }

        try
        {
            string prefix = NextPrefix();

            // Resolve character textures
            var charTextures = ResolveCharacterTextures(characterId);
            if (charTextures == null)
            {
                Console.WriteLine($"doh: MaterialFactory — Failed to resolve textures for '{characterId}'.");
                return null;
            }

            // Get default handles for material construction
            int samplerHandle = GetSamplerRepeatHandle();
            int defaultBlackHandle = GetDefaultBlackTextureHandle();
            int blankMaterialHandle = GetBlankMaterialTextureHandle();

            // Create body material
            int bodyHandle = CreateMaterialFromTextures(
                $"{prefix}_body", charTextures.Value.BodyDiffuse, charTextures.Value.BodyNormal,
                charTextures.Value.BodyPbr, samplerHandle, defaultBlackHandle, blankMaterialHandle, tintColor);

            // Create head material
            int headHandle = CreateMaterialFromTextures(
                $"{prefix}_head", charTextures.Value.HeadDiffuse, charTextures.Value.HeadNormal,
                charTextures.Value.HeadPbr, samplerHandle, defaultBlackHandle, blankMaterialHandle, tintColor);

            // Eye material — use shared (no tint on eyes)
            int eyeHandle = charTextures.Value.SharedEyeHandle;

            // Fur material — create tinted copy using head textures
            int furHandle = CreateFurMaterial(prefix, charTextures.Value, samplerHandle, defaultBlackHandle, tintColor);

            if (bodyHandle < 0 || headHandle < 0)
            {
                Console.WriteLine($"doh: MaterialFactory — Failed to create materials for '{prefix}' (body={bodyHandle}, head={headHandle}).");
                return null;
            }

            var matSet = new KittenMaterialSet(prefix, tintColor)
            {
                BodyMaterialHandle = bodyHandle,
                HeadMaterialHandle = headHandle,
                EyeMaterialHandle = eyeHandle,
                FurMaterialHandle = furHandle
            };

            matSet.AssetNames.Add($"{prefix}_body");
            matSet.AssetNames.Add($"{prefix}_head");
            if (furHandle >= 0) matSet.AssetNames.Add(FurAssetName(prefix));

            _createdSets.Add(matSet);
            Console.WriteLine($"doh: Created material set '{prefix}' — body={bodyHandle}, head={headHandle}, eye={eyeHandle}, fur={furHandle}");
            return matSet;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: MaterialFactory error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Creates a unique fur material for a kitten, preserving the fur shader's
    /// required ExtraData (FurTexture, FurSampler, FurMask handles).
    /// </summary>
    public int CreateClonedFurMaterial(string namePrefix, string characterId, float4 tintColor)
    {
        var charTextures = ResolveCharacterTextures(characterId);
        if (charTextures == null) return -1;

        int samplerHandle = GetSamplerRepeatHandle();
        int defaultBlackHandle = GetDefaultBlackTextureHandle();

        return CreateFurMaterial(namePrefix, charTextures.Value, samplerHandle, defaultBlackHandle, tintColor);
    }


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
            Console.WriteLine($"doh: CloneAllMaterials — MaterialSystemAccessor not initialized: {MaterialSystemAccessor.LastError}");
            return null;
        }

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

                if (newHandle >= 0)
                {
                    handleMap[oldHandle] = newHandle;
                    allNewHandles.Add(newHandle);
                    assetNames.Add(cloneName);
                }
                else
                {
                    Console.WriteLine($"doh: Failed to clone handle {oldHandle}, keeping shared.");
                }
            }

            if (handleMap.Count == 0)
            {
                Console.WriteLine($"doh: CloneAllMaterials — no materials cloned for '{prefix}'.");
                return null;
            }

            var matSet = new KittenMaterialSet(prefix, tintColor);
            matSet.HandleMap = handleMap;
            matSet.AllMaterialHandles.AddRange(allNewHandles);
            matSet.AssetNames.AddRange(assetNames);

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
            Console.WriteLine($"doh: Cloned {handleMap.Count}/{uniqueHandles.Length} materials for '{prefix}'");
            return matSet;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: CloneAllMaterials error: {ex.Message}");
            return null;
        }
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
            Console.WriteLine($"doh: No name found for handle {sourceHandle}, creating blank tinted material.");
            return CreateBlankTintedMaterial(newName, tintColor, samplerHandle, defaultWhiteHandle, defaultBlackHandle);
        }

        // Try to look up PbrMaterialReference from ModLibrary to get texture info
        PbrMaterialReference? pbrRef = null;
        try { pbrRef = ModLibrary.Get<PbrMaterialReference>(materialName); } catch { }

        if (pbrRef != null)
        {
            // Resolve via .Get() to follow references
            var resolved = InvokeGet(pbrRef) as PbrMaterialReference ?? pbrRef;
            return CreateMaterialFromPbrRef(newName, resolved, tintColor,
                samplerHandle, defaultWhiteHandle, defaultBlackHandle);
        }

        // Not a PbrMaterialReference — could be a GLTF inline material.
        // Create with default white textures + tint (texture detail lost but tint works)
        Console.WriteLine($"doh: Material '{materialName}' not in ModLibrary, creating tinted fallback.");
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
        return MaterialSystemAccessor.GetMaterialHandle(name);
    }

    /// <summary>
    /// Gets the bindless handle from a TextureReference field on PbrMaterialReference,
    /// calling .Get() to resolve references.
    /// </summary>
    private int GetTextureBindlessHandle(object? textureRef, int fallback)
    {
        if (textureRef == null) return fallback;
        try
        {
            var resolved = InvokeGet(textureRef);
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
        return MaterialSystemAccessor.GetMaterialHandle(name);
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

    // ---- Private implementation ----

    private int CreateMaterialFromTextures(
        string name, int albedoTex, int normalTex, int pbrTex,
        int samplerHandle, int defaultBlackHandle, int blankMaterialHandle,
        float4 albedoColor)
    {
        // Use blank material texture as fallback for missing textures
        if (albedoTex < 0) albedoTex = blankMaterialHandle;
        if (normalTex < 0) normalTex = blankMaterialHandle;
        if (pbrTex < 0) pbrTex = blankMaterialHandle;

        var matData = new MaterialData
        {
            AlbedoTexture = albedoTex,
            NormalTexture = normalTex,
            RoughMetallicAOTexture = pbrTex,
            Sampler = samplerHandle,
            AlbedoColor = albedoColor,
            RoughnessMetalScale = float4.One,
            EmissiveTexture = defaultBlackHandle,
            ExtraData = float4.Zero
        };

        if (!MaterialSystemAccessor.CreateMaterial(name, matData))
            return -1;

        return MaterialSystemAccessor.GetMaterialHandle(name);
    }

    private int CreateFurMaterial(string prefix, CharacterTextureHandles textures,
        int samplerHandle, int defaultBlackHandle, float4 tintColor)
    {
        // Fur needs ExtraData with FurTexture, FurSampler, CatFurMaskTexture handles.
        // If we can't resolve these, skip fur material creation.
        if (textures.FurTextureHandle < 0 || textures.FurSamplerHandle < 0)
            return -1;

        string name = FurAssetName(prefix);

        var furData = new MaterialData
        {
            AlbedoTexture = textures.HeadDiffuse,
            NormalTexture = textures.HeadNormal,
            RoughMetallicAOTexture = GetBlankMaterialTextureHandle(),
            Sampler = samplerHandle,
            AlbedoColor = tintColor,
            RoughnessMetalScale = float4.One,
            EmissiveTexture = defaultBlackHandle,
            ExtraData = new float4(textures.FurTextureHandle, textures.FurSamplerHandle,
                textures.FurMaskTextureHandle, 0f)
        };

        if (!MaterialSystemAccessor.CreateMaterial(name, furData))
            return -1;

        return MaterialSystemAccessor.GetMaterialHandle(name);
    }

    /// <summary>
    /// Resolves all texture bindless handles for a character via reflection.
    /// Accesses ModLibrary → CharacterReference → CharacterTexturesReference → PbrMaterialReference
    /// → TextureReference → BindlessHandle.
    /// </summary>
    private CharacterTextureHandles? ResolveCharacterTextures(string characterId)
    {
        try
        {
            // Get CharacterReference from ModLibrary
            var charRef = ModLibrary.Get<CharacterReference>(characterId);
            if (charRef == null)
            {
                Console.WriteLine($"doh: Character '{characterId}' not found in ModLibrary.");
                return null;
            }

            // Get CharacterTexturesReference
            var charTexturesSub = charRef.CharacterTextures;
            if (charTexturesSub == null)
            {
                Console.WriteLine($"doh: Character '{characterId}' has no CharacterTextures.");
                return null;
            }

            // Resolve via .Get()
            var charTextures = InvokeGet(charTexturesSub);
            if (charTextures == null)
            {
                Console.WriteLine($"doh: Failed to resolve CharacterTextures for '{characterId}'.");
                return null;
            }

            // Get PbrMaterialReferences
            var bodyMat = GetFieldOrPropValue(charTextures, "CharacterBodyMaterial");
            var headMat = GetFieldOrPropValue(charTextures, "CharacterHeadMaterial");
            var eyeMat = GetFieldOrPropValue(charTextures, "CharacterEyeMaterial");

            // Resolve texture handles
            var handles = new CharacterTextureHandles
            {
                BodyDiffuse = ResolvePbrTextureHandle(bodyMat, "DiffuseReference"),
                BodyNormal = ResolvePbrTextureHandle(bodyMat, "NormalReference"),
                BodyPbr = ResolvePbrTextureHandle(bodyMat, "PBRMap"),
                HeadDiffuse = ResolvePbrTextureHandle(headMat, "DiffuseReference"),
                HeadNormal = ResolvePbrTextureHandle(headMat, "NormalReference"),
                HeadPbr = ResolvePbrTextureHandle(headMat, "PBRMap"),
                // Eye — get the shared material handle via MaterialSystem
                SharedEyeHandle = ResolveSharedMaterialHandle(eyeMat)
            };

            // Resolve fur-related handles via CharacterRenderResources
            ResolveFurHandles(ref handles);

            return handles;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: ResolveCharacterTextures error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolves a single texture bindless handle from a PbrMaterialReference field.
    /// Path: pbrMat → .Get() → field (TextureReference) → .Get() → .BindlessHandle
    /// </summary>
    private int ResolvePbrTextureHandle(object? pbrMat, string textureFieldName)
    {
        if (pbrMat == null) return -1;

        try
        {
            // Call .Get() to resolve the reference
            var resolved = InvokeGet(pbrMat);
            if (resolved == null) return -1;

            // Get the texture reference field (DiffuseReference, NormalReference, PBRMap)
            var textureRef = GetFieldOrPropValue(resolved, textureFieldName);
            if (textureRef == null) return -1;

            // Call .Get() on the texture reference
            var resolvedTexture = InvokeGet(textureRef);
            if (resolvedTexture == null) return -1;

            // Get BindlessHandle
            return GetIntFieldOrProp(resolvedTexture, "BindlessHandle");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: ResolvePbrTextureHandle({textureFieldName}): {ex.Message}");
            return -1;
        }
    }

    /// <summary>
    /// Gets the shared material handle from MaterialSystem via PbrMaterialReference.Id.
    /// </summary>
    private int ResolveSharedMaterialHandle(object? pbrMat)
    {
        if (pbrMat == null) return -1;

        try
        {
            var resolved = InvokeGet(pbrMat);
            if (resolved == null) return -1;

            string? id = GetFieldOrPropValue(resolved, "Id")?.ToString();
            if (string.IsNullOrEmpty(id)) return -1;

            return MaterialSystemAccessor.GetMaterialHandle(id);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// Resolves global fur texture handles from CharacterRenderResources.
    /// </summary>
    private void ResolveFurHandles(ref CharacterTextureHandles handles)
    {
        try
        {
            var programType = typeof(Part).Assembly.GetType("KSA.Program");
            if (programType == null) return;

            var instanceProp = programType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var programInstance = instanceProp?.GetValue(null);
            if (programInstance == null) return;

            var charRenderSystem = GetFieldOrPropValue(programInstance, "CharacterRenderSystem");
            if (charRenderSystem == null) return;

            var resources = GetFieldOrPropValue(charRenderSystem, "_resources");
            if (resources == null)
                resources = GetFieldOrPropValue(charRenderSystem, "Resources");
            if (resources == null) return;

            // FurTexture.BindlessHandle
            var furTex = GetFieldOrPropValue(resources, "FurTexture");
            if (furTex != null)
                handles.FurTextureHandle = GetIntFieldOrProp(furTex, "BindlessHandle");

            // FurSampler.BindlessIndex
            var furSampler = GetFieldOrPropValue(resources, "FurSampler");
            if (furSampler != null)
                handles.FurSamplerHandle = GetIntFieldOrProp(furSampler, "BindlessIndex");

            // CatFurMaskTexture.BindlessHandle
            var furMask = GetFieldOrPropValue(resources, "CatFurMaskTexture");
            if (furMask != null)
                handles.FurMaskTextureHandle = GetIntFieldOrProp(furMask, "BindlessHandle");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: ResolveFurHandles error: {ex.Message}");
        }
    }

    // ---- Default handle accessors (from TextureSystem/GltfSystem) ----

    private int GetSamplerRepeatHandle()
    {
        try
        {
            var (_, textureSystem) = GetRenderSystems();
            if (textureSystem == null) return 0;
            return GetIntFieldOrProp(textureSystem, "SamplerRepeatHandle");
        }
        catch { return 0; }
    }

    private int GetDefaultBlackTextureHandle()
    {
        try
        {
            var (_, textureSystem) = GetRenderSystems();
            if (textureSystem == null) return 0;
            var tex = GetFieldOrPropValue(textureSystem, "DefaultBlackTexture");
            return tex != null ? GetIntFieldOrProp(tex, "BindlessHandle") : 0;
        }
        catch { return 0; }
    }

    private int GetDefaultWhiteTextureHandle()
    {
        try
        {
            var (_, textureSystem) = GetRenderSystems();
            if (textureSystem == null) return 0;
            var tex = GetFieldOrPropValue(textureSystem, "DefaultWhiteTexture");
            return tex != null ? GetIntFieldOrProp(tex, "BindlessHandle") : 0;
        }
        catch { return 0; }
    }

    private int GetBlankMaterialTextureHandle()
    {
        try
        {
            var (gltfSystem, _) = GetRenderSystems();
            if (gltfSystem == null) return 0;
            var tex = GetFieldOrPropValue(gltfSystem, "BlankMaterialTexture");
            return tex != null ? GetIntFieldOrProp(tex, "BindlessHandle") : 0;
        }
        catch { return 0; }
    }

    private (object? gltfSystem, object? textureSystem) GetRenderSystems()
    {
        var programType = typeof(Part).Assembly.GetType("KSA.Program");
        var instanceProp = programType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        var programInstance = instanceProp?.GetValue(null);
        if (programInstance == null) return (null, null);

        var superMesh = GetFieldOrPropValue(programInstance, "SuperMeshRenderSystem");
        if (superMesh == null) return (null, null);

        var textureSystem = GetFieldOrPropValue(superMesh, "TextureSystem");
        var gltfSystem = GetFieldOrPropValue(superMesh, "GltfSystem");

        return (gltfSystem, textureSystem);
    }

    // ---- Reflection helpers ----

    private static object? InvokeGet(object instance)
    {
        // KSA reference types have both T Get<T>() and Foo Get() — must filter for non-generic
        var methods = instance.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
        var getMethod = Array.Find(methods, m => m.Name == "Get" && !m.IsGenericMethod && m.GetParameters().Length == 0);
        return getMethod?.Invoke(instance, null);
    }

    private static object? GetFieldOrPropValue(object instance, string name)
    {
        var type = instance.GetType();
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null) return field.GetValue(instance);

        var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return prop?.GetValue(instance);
    }

    private static int GetIntFieldOrProp(object instance, string name)
    {
        var val = GetFieldOrPropValue(instance, name);
        return val is int i ? i : -1;
    }

    private struct CharacterTextureHandles
    {
        public int BodyDiffuse;
        public int BodyNormal;
        public int BodyPbr;
        public int HeadDiffuse;
        public int HeadNormal;
        public int HeadPbr;
        public int SharedEyeHandle;
        public int FurTextureHandle;
        public int FurSamplerHandle;
        public int FurMaskTextureHandle;

        public CharacterTextureHandles()
        {
            BodyDiffuse = BodyNormal = BodyPbr = -1;
            HeadDiffuse = HeadNormal = HeadPbr = -1;
            SharedEyeHandle = -1;
            FurTextureHandle = FurSamplerHandle = FurMaskTextureHandle = -1;
        }
    }
}
