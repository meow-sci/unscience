using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.WarPaintLib.Materials;

public sealed partial class MaterialFactory
{
    /// <summary>
    /// Creates a tinted material set for a kitten using the specified character's textures.
    /// Resolves PbrMaterialReference textures via reflection and constructs new MaterialData
    /// structs with the same textures but a custom AlbedoColor.
    /// </summary>
    public KittenMaterialSet? CreateTintedMaterialSet(string characterId, float4 tintColor)
    {
        if (!MaterialSystemAccessor.IsInitialized && !MaterialSystemAccessor.Initialize())
        {
            Console.WriteLine($"war-paint: MaterialFactory — MaterialSystemAccessor not initialized: {MaterialSystemAccessor.LastError}");
            return null;
        }

        string? prefix = null;
        try
        {
            prefix = NextPrefix();

            // Resolve character textures
            var charTextures = ResolveCharacterTextures(characterId);
            if (charTextures == null)
            {
                Console.WriteLine($"war-paint: MaterialFactory — Failed to resolve textures for '{characterId}'.");
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
                Console.WriteLine($"war-paint: MaterialFactory — Failed to create materials for '{prefix}' (body={bodyHandle}, head={headHandle}).");
                ReleaseUnboundAssets(new[] { $"{prefix}_body", $"{prefix}_head", FurAssetName(prefix) });
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
            foreach (string name in matSet.AssetNames)
                if (MaterialSystemAccessor.GetMaterialIdentity(name) is { } identity)
                    matSet.AssetIdentities[name] = identity;

            _createdSets.Add(matSet);
            Console.WriteLine($"war-paint: Created material set '{prefix}' — body={bodyHandle}, head={headHandle}, eye={eyeHandle}, fur={furHandle}");
            return matSet;
        }
        catch (Exception ex)
        {
            if (prefix != null) ReleaseUnboundAssets(new[] { $"{prefix}_body", $"{prefix}_head", FurAssetName(prefix) });
            Console.WriteLine($"war-paint: MaterialFactory error: {ex.Message}");
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

        return MaterialSystemAccessor.GetExistingMaterialHandle(name);
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

        return MaterialSystemAccessor.GetExistingMaterialHandle(name);
    }

}
