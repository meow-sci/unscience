using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Brutal.Numerics;
using KSA;
using MeowSci.DohLib.Materials;

namespace MeowSci.DohLib.Spawning;

/// <summary>Per-kitten material cloning: swaps every renderable's shared handles for cloned ones.</summary>
public sealed partial class KittenSpawner
{
    /// <summary>
    /// Creates unique per-kitten materials by cloning every material the kitten uses
    /// (character model, fur, helmet, visor, MMU, cosmetics), then replaces all
    /// MaterialIndices entries with the cloned handles.
    /// Returns the KittenMaterialSet, or null on failure.
    /// </summary>
    internal KittenMaterialSet? ApplyClonedMaterials(KittenEva kittenEva, float4 tintColor, string characterId,
        KittenMaterialSet? reusable = null)
    {
        KittenMaterialSet? createdSet = null;
        bool indicesSwapped = false;
        try
        {
            // Navigate to CharacterAvatar
            var avatar = GetCharacterAvatar(kittenEva);
            if (avatar == null)
            {
                Console.WriteLine("doh: Failed to get CharacterAvatar from KittenEva.");
                return null;
            }

            // Collect MaterialIndices arrays from all renderables
            var renderables = new List<(string name, int[] indices)>();

            // 1. CharacterModel (body/head/eyes)
            var charModelIndices = GetMaterialIndicesFromPath(avatar, "Core", "CharacterModel");
            if (charModelIndices != null)
                renderables.Add(("CharacterModel", charModelIndices));

            // 2. Fur — collected but cloned separately (needs special ExtraData for fur shader)
            var furIndices = GetMaterialIndicesFromPath(avatar, "Fur", "CatFurRenderable");
            if (furIndices != null)
                renderables.Add(("Fur", furIndices));

            // 3. Helmet
            var helmetIndices = GetMaterialIndicesFromPath(avatar, "Attachments", "Helmet", "HelmetMesh");
            if (helmetIndices != null)
                renderables.Add(("Helmet", helmetIndices));

            // 4. Visor
            var visorIndices = GetMaterialIndicesFromPath(avatar, "Attachments", "Helmet", "VisorMesh");
            if (visorIndices != null)
                renderables.Add(("Visor", visorIndices));

            // 5. MMU
            var mmuIndices = GetMaterialIndicesFromPath(avatar, "Attachments", "Mmu", "MmuMesh");
            if (mmuIndices != null)
                renderables.Add(("MMU", mmuIndices));

            if (renderables.Count == 0)
            {
                Console.WriteLine("doh: No MaterialIndices found on any renderable.");
                return null;
            }

            // Separate fur handles — fur shader needs ExtraData with fur texture handles
            var furHandleSet = furIndices != null ? new HashSet<int>(furIndices) : new HashSet<int>();
            var nonFurHandles = renderables
                .Where(r => r.name != "Fur")
                .SelectMany(r => r.indices)
                .ToArray();

            Console.WriteLine($"doh: Found {renderables.Count} renderables, {nonFurHandles.Distinct().Count()} non-fur + {furHandleSet.Count} fur unique handles");

            // Clone non-fur materials via PbrMaterialReference lookup.
            // `reusable` is either the batch's shared set or a set detached by a load (the global
            // material system survives ordinary loads). Reuse its slots when the native shared
            // material identity still matches instead of allocating from the fixed-size pool.
            // A released set's slots may already belong to other materials, so never reuse one.
            bool canReuse = reusable != null && !reusable.IsReleased
                && nonFurHandles.Where(h => h >= 0).All(reusable.HandleMap.ContainsKey);
            var matSet = canReuse ? reusable : _materialFactory.CloneAllMaterials(nonFurHandles, tintColor);
            if (matSet == null) return null;
            if (canReuse) matSet.UpdateTint(tintColor);
            else createdSet = matSet;

            // Clone fur materials with proper ExtraData (FurTexture, FurSampler, FurMask)
            if (furIndices != null && furIndices.Length > 0)
            {
                foreach (int oldHandle in furIndices.Distinct())
                {
                    if (oldHandle < 0 || matSet.HandleMap.ContainsKey(oldHandle)) continue;
                    int newHandle = _materialFactory.CreateClonedFurMaterial(
                        matSet.Id, characterId, tintColor);
                    if (newHandle >= 0)
                    {
                        matSet.HandleMap[oldHandle] = newHandle;
                        matSet.AllMaterialHandles.Add(newHandle);
                        matSet.AssetNames.Add(MaterialFactory.FurAssetName(matSet.Id));
                        matSet.Materials.Add(new MaterialEntry
                        {
                            Name = "CatFur",
                            Source = "Fur",
                            Handle = newHandle,
                            Color = tintColor
                        });
                        Console.WriteLine($"doh:   Fur: cloned handle {oldHandle} → {newHandle}");
                    }
                    else
                    {
                        Console.WriteLine($"doh:   Fur: failed to clone handle {oldHandle}");
                    }
                }
            }

            // Replace entries in each renderable's MaterialIndices using HandleMap
            indicesSwapped = true;
            int totalReplacements = 0;
            foreach (var (name, indices) in renderables)
            {
                int replaced = 0;
                for (int i = 0; i < indices.Length; i++)
                {
                    if (matSet.HandleMap.TryGetValue(indices[i], out int newHandle))
                    {
                        indices[i] = newHandle;
                        replaced++;
                    }
                }
                if (replaced > 0)
                    Console.WriteLine($"doh:   {name}: {replaced}/{indices.Length} slots replaced");
                totalReplacements += replaced;
            }

            // Tag Materials entries with their source renderable name
            var newHandleToSource = new Dictionary<int, string>();
            foreach (var (name, indices) in renderables)
            {
                foreach (int h in indices)
                {
                    if (!newHandleToSource.ContainsKey(h))
                        newHandleToSource[h] = name;
                }
            }
            foreach (var mat in matSet.Materials)
            {
                if (string.IsNullOrEmpty(mat.Source) && newHandleToSource.TryGetValue(mat.Handle, out var src))
                    mat.Source = src;
            }

            Console.WriteLine($"doh: Applied cloned materials '{matSet.Id}' ({totalReplacements} replacements, {matSet.AllMaterialHandles.Count} unique cloned handles)");
            return matSet;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: ApplyClonedMaterials error: {ex.Message}");
            // Return a set nothing references yet back to the fixed-size GPU pool.
            if (createdSet != null && !indicesSwapped) _materialFactory.Release(createdSet);
            return null;
        }
    }

    /// <summary>Gets the CharacterAvatar from a KittenEva via reflection.</summary>
    private static object? GetCharacterAvatar(KittenEva kittenEva)
    {
        var renderableField = typeof(KittenEva).GetField("_renderable",
            BindingFlags.NonPublic | BindingFlags.Instance);
        if (renderableField == null) return null;

        var renderable = renderableField.GetValue(kittenEva);
        if (renderable == null) return null;

        var avatarField = renderable.GetType().GetField("_characterAvatar",
            BindingFlags.NonPublic | BindingFlags.Instance);
        return avatarField?.GetValue(renderable);
    }

    /// <summary>
    /// Navigates a chain of fields on an object and extracts MaterialIndices from the final renderable.
    /// E.g., GetMaterialIndicesFromPath(avatar, "Core", "CharacterModel") navigates
    /// avatar.Core.CharacterModel.MaterialIndices.
    /// </summary>
    private static int[]? GetMaterialIndicesFromPath(object root, params string[] fieldPath)
    {
        object? current = root;
        foreach (string fieldName in fieldPath)
        {
            if (current == null) return null;
            current = GetFieldValue(current, fieldName);
        }

        if (current == null) return null;

        // Get MaterialIndices from the final renderable
        var matField = FindFieldInHierarchy(current.GetType(), "MaterialIndices");
        return matField?.GetValue(current) as int[];
    }

    /// <summary>Gets a field value by name, searching public and non-public fields.</summary>
    private static object? GetFieldValue(object instance, string name)
    {
        var field = instance.GetType().GetField(name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(instance);
    }

    private static FieldInfo? FindFieldInHierarchy(Type? type, string fieldName)
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
