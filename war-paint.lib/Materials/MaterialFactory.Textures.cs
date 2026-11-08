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
                Console.WriteLine($"war-paint: Character '{characterId}' not found in ModLibrary.");
                return null;
            }

            // Get CharacterTexturesReference
            var charTexturesSub = charRef.CharacterTextures;
            if (charTexturesSub == null)
            {
                Console.WriteLine($"war-paint: Character '{characterId}' has no CharacterTextures.");
                return null;
            }

            // Follow the resolved data link
            var charTextures = GameDataReference.Resolve(charTexturesSub);
            if (charTextures == null)
            {
                Console.WriteLine($"war-paint: Failed to resolve CharacterTextures for '{characterId}'.");
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
            Console.WriteLine($"war-paint: ResolveCharacterTextures error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Resolves a single texture bindless handle from a PbrMaterialReference field.
    /// Path: Pbr material data link → Value → texture data link → Value → BindlessHandle
    /// </summary>
    private int ResolvePbrTextureHandle(object? pbrMat, string textureFieldName)
    {
        if (pbrMat == null) return -1;

        try
        {
            // Follow the resolved data link
            var resolved = GameDataReference.Resolve(pbrMat);
            if (resolved == null) return -1;

            // Get the texture reference field (DiffuseReference, NormalReference, PBRMap)
            var textureRef = GetFieldOrPropValue(resolved, textureFieldName);
            if (textureRef == null) return -1;

            // Follow the resolved texture link
            var resolvedTexture = GameDataReference.Resolve(textureRef);
            if (resolvedTexture == null) return -1;

            // Get BindlessHandle
            return GetIntFieldOrProp(resolvedTexture, "BindlessHandle");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"war-paint: ResolvePbrTextureHandle({textureFieldName}): {ex.Message}");
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
            var resolved = GameDataReference.Resolve(pbrMat);
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
            Console.WriteLine($"war-paint: ResolveFurHandles error: {ex.Message}");
        }
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
