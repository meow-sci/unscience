using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Brutal.Numerics;
using KSA;
using MeowSci.WarPaintLib.Materials;

namespace MeowSci.WarPaintLib;

/// <summary>DOH's proven character/attachment cloning path, owned independently by War Paint.</summary>
public sealed class NativeKittenPaintBackend : IKittenPaintBackend
{
    private readonly MaterialFactory _factory = new();

    public bool Initialize() => MaterialSystemAccessor.Initialize();

    public IReadOnlyList<KittenMaterialBinding>? CaptureBindings(KittenEva kitten)
    {
        if (kitten.IsDisposed) return null;
        object? renderable = Field(kitten, "_renderable");
        object? avatar = renderable == null ? null : Field(renderable, "_characterAvatar");
        if (avatar == null) return null;
        var bindings = new List<KittenMaterialBinding>();
        Add("CharacterModel", "Core", "CharacterModel");
        Add("Fur", "Fur", "CatFurRenderable");
        Add("Helmet", "Attachments", "Helmet", "HelmetMesh");
        Add("Visor", "Attachments", "Helmet", "VisorMesh");
        Add("MMU", "Attachments", "Mmu", "MmuMesh");
        return bindings.Count == 0 ? null : bindings;

        void Add(string source, params string[] path)
        {
            object? node = avatar;
            foreach (string field in path) node = node == null ? null : Field(node, field);
            if (node != null && Field(node, "MaterialIndices") is int[] indices)
                bindings.Add(new(source, indices));
        }
    }

    public KittenMaterialSet? CreateMaterials(IReadOnlyList<KittenMaterialBinding> bindings,
        string characterId, float4 tint, KittenMaterialSet? reusable)
    {
        if (!Initialize()) return null;
        int[] nonFur = bindings.Where(b => b.Source != "Fur").SelectMany(b => b.OriginalIndices).ToArray();
        int[] all = bindings.SelectMany(b => b.OriginalIndices).Where(h => h >= 0).Distinct().ToArray();
        if (all.Length == 0) return null;
        bool reuse = reusable != null && CanReuse(reusable)
            && KittenMaterialReuse.TryRemapFur(bindings, reusable);
        if (reuse)
        {
            // Native EVA reconstruction allocates a fresh fur source slot. The detached clone's
            // textures/ExtraData still belong to this character; rebind its semantic fur slots.
            if (!SetTint(reusable!, tint)) return null;
            return reusable;
        }
        // No binding changes occur until the complete set exists. A failed allocation cannot
        // leave a target partly shared or make its old bindings point at freed slots.
        int available = MaterialSystemAccessor.GetAvailableSlots();
        if (available >= 0 && available < all.Length) return null;
        var set = _factory.CloneAllMaterials(nonFur, tint);
        if (set == null) return null;
        try
        {
            int furNumber = 0;
            foreach (int oldHandle in bindings.Where(b => b.Source == "Fur")
                .SelectMany(b => b.OriginalIndices).Where(h => h >= 0).Distinct())
            {
                if (set.HandleMap.ContainsKey(oldHandle)) continue;
                string furPrefix = furNumber++ == 0 ? set.Id : $"{set.Id}_f{furNumber}";
                string furAssetName = MaterialFactory.FurAssetName(furPrefix);
                int handle = _factory.CreateClonedFurMaterial(furPrefix, characterId, tint);
                if (handle < 0) { Release(set); return null; }
                set.HandleMap[oldHandle] = handle;
                set.AllMaterialHandles.Add(handle);
                set.AssetNames.Add(furAssetName);
                if (MaterialSystemAccessor.GetMaterialIdentity(furAssetName) is { } identity)
                    set.AssetIdentities[furAssetName] = identity;
                set.Materials.Add(new MaterialEntry
                    { Name = "CatFur", Source = "Fur", Handle = handle, Color = tint });
            }
            if (all.Any(h => !set.HandleMap.ContainsKey(h))) { Release(set); return null; }
            foreach (var material in set.Materials)
            {
                var binding = bindings.First(b => b.OriginalIndices.Any(h =>
                    set.HandleMap.TryGetValue(h, out int clone) && clone == material.Handle));
                material.Source = binding.Source;
                material.Slot = Array.FindIndex(binding.OriginalIndices,
                    h => set.HandleMap.TryGetValue(h, out int clone) && clone == material.Handle);
            }
            return set;
        }
        catch
        {
            Release(set);
            throw;
        }
    }

    public void Bind(IReadOnlyList<KittenMaterialBinding> bindings, KittenMaterialSet materials)
    {
        foreach (var binding in bindings)
            for (int i = 0; i < binding.OriginalIndices.Length; i++)
                if (materials.HandleMap.TryGetValue(binding.OriginalIndices[i], out int clone))
                    binding.Indices[i] = clone;
    }

    public void Restore(IReadOnlyList<KittenMaterialBinding> bindings, KittenMaterialSet materials)
    {
        foreach (var binding in bindings)
            for (int i = 0; i < binding.OriginalIndices.Length; i++)
            {
                int original = binding.OriginalIndices[i];
                if (materials.HandleMap.TryGetValue(original, out int clone) && binding.Indices[i] == clone)
                    binding.Indices[i] = original;
            }
    }

    public bool SetTint(KittenMaterialSet materials, float4 tint) => !materials.IsReleased && materials.UpdateTint(tint);
    public bool SetColor(MaterialEntry material, float4 color)
    {
        if (!MaterialSystemAccessor.WriteAlbedoColor(material.Handle, color)) return false;
        material.Color = color;
        return true;
    }

    public bool CanReuse(KittenMaterialSet materials) => !materials.IsReleased
        && materials.AssetNames.Count == materials.AllMaterialHandles.Count
        && materials.AssetNames.All(name => materials.AssetIdentities.TryGetValue(name, out var identity)
            && MaterialSystemAccessor.OwnsMaterial(name, identity))
        && materials.AssetNames.Select(MaterialSystemAccessor.GetExistingMaterialHandle)
            .OrderBy(h => h).SequenceEqual(materials.AllMaterialHandles.OrderBy(h => h));

    public void Release(KittenMaterialSet materials) => _factory.Release(materials);
    public string? GetMaterialName(int handle) => MaterialSystemAccessor.GetAllMaterials()
        .Where(m => m.Handle == handle).Select(m => m.Name).FirstOrDefault();

    private static object? Field(object instance, string name)
    {
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null) return field.GetValue(instance);
        }
        return null;
    }
}
