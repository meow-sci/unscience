using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.WarPaintLib;
using MeowSci.WarPaintLib.Materials;

namespace Brutal.Numerics
{
    public readonly record struct float4(float X, float Y, float Z, float W)
    {
        public static float4 One => new(1, 1, 1, 1);
    }
}

namespace KSA
{
    public sealed class GpuObjectAssetRef(int handle) : IDisposable
    {
        public int Handle = handle;
        public bool IsValid { get; private set; } = true;
        public int DisposeCount;
        public void Dispose() { IsValid = false; DisposeCount++; }
    }
    public class Vehicle(string id)
    {
        public string Id = id;
        public bool IsDisposed;
        public bool IsDebris;
    }
    public sealed class CharacterReference(string id) { public string Id = id; }
    public sealed class KittenEva(string id, string character = "Calico") : Vehicle(id)
    {
        public CharacterReference Character = new(character);
        public int[] Materials = [1, 2];
    }
    public sealed class LookupCollection
    {
        public readonly List<object> Items = new();
        public List<object> UnsafeAsList() => Items;
    }
    public sealed class CelestialSystem { public LookupCollection All = new(); }
    public static class Universe { public static CelestialSystem? CurrentSystem = new(); }
    public static class Program { public static Vehicle? ControlledVehicle; }
}

namespace MeowSci.WarPaintLib.Materials
{
    public static partial class MaterialSystemAccessor
    {
        private static System.Collections.IDictionary? _assetMap;
        private static object? _materialSystem;
        private static string? _lastError;
        internal static void SetAssetMap(System.Collections.IDictionary assets)
        { _assetMap = assets; _materialSystem = new object(); _bigBufferAllocatorField = null; }
        public static int GetExistingMaterialHandle(string name) => GetMaterialIdentity(name) is GpuObjectAssetRef asset ? asset.Handle : -1;
        public static readonly List<(int Handle, float4 Color)> Writes = new();
        public static bool WriteAlbedoColor(int handle, float4 color)
        {
            Writes.Add((handle, color));
            MeowSci.KsaAbstractions.Persistence.MaterialColorState.Record(handle, color);
            return true;
        }
        public static void Cleanup() { }
    }
    public sealed partial class MaterialFactory
    {
        private int _nextMaterialId;
        private readonly List<KittenMaterialSet> _createdSets = new();
        internal void Track(KittenMaterialSet set) => _createdSets.Add(set);
    }
}

namespace Core
{
    public readonly record struct AssetName(string Name)
    {
        public static implicit operator AssetName(string name) => new(name);
        public override string ToString() => Name;
    }
}

namespace MeowSci.KsaAbstractions
{
    public static class KsaPaths { public static string ModDataDir => System.IO.Path.GetTempPath(); }
}

namespace MeowSci.WarPaintLib
{
    // Only used by Shared's initialization; tests inject their own isolated backend instance.
    public sealed class NativeKittenPaintBackend : FakeBackend { }
}

public class FakeBackend : IKittenPaintBackend
{
    public int Created, Reused, Released;
    public bool FailCreate, FailColor, Recycled, CharacterNames;
    public readonly List<string> Events = new();
    private int _next = 100;
    public bool Initialize() => true;
    public IReadOnlyList<KittenMaterialBinding>? CaptureBindings(KittenEva kitten) =>
        kitten.IsDisposed ? null : new[] { new KittenMaterialBinding("CharacterModel", kitten.Materials) };
    public KittenMaterialSet? CreateMaterials(IReadOnlyList<KittenMaterialBinding> bindings,
        string characterId, float4 tint, KittenMaterialSet? reusable)
    {
        if (FailCreate) return null;
        if (reusable != null && CanReuse(reusable)
            && bindings.SelectMany(b => b.OriginalIndices).All(reusable.HandleMap.ContainsKey))
        { Reused++; SetTint(reusable, tint); return reusable; }
        var set = new KittenMaterialSet("fixture_" + Created++, tint);
        foreach (int original in bindings.SelectMany(b => b.OriginalIndices).Distinct())
        {
            int handle = _next++;
            set.HandleMap.Add(original, handle);
            set.AllMaterialHandles.Add(handle);
            set.AssetNames.Add("fixture/" + handle);
            set.Materials.Add(new()
            {
                Name = (CharacterNames ? characterId : "stock") + "/" + original,
                Source = "CharacterModel", Slot = bindings[0].OriginalIndices.ToList().IndexOf(original),
                Handle = handle, Color = tint
            });
        }
        return set;
    }
    public void Bind(IReadOnlyList<KittenMaterialBinding> bindings, KittenMaterialSet materials)
    {
        Events.Add("bind:" + materials.Id);
        foreach (var b in bindings)
            for (int i = 0; i < b.Indices.Length; i++) b.Indices[i] = materials.HandleMap[b.OriginalIndices[i]];
    }
    public void Restore(IReadOnlyList<KittenMaterialBinding> bindings, KittenMaterialSet materials)
    {
        Events.Add("restore:" + materials.Id);
        foreach (var b in bindings)
            for (int i = 0; i < b.Indices.Length; i++)
                if (materials.HandleMap.TryGetValue(b.OriginalIndices[i], out int clone) && b.Indices[i] == clone)
                    b.Indices[i] = b.OriginalIndices[i];
    }
    public bool SetTint(KittenMaterialSet materials, float4 tint) => materials.UpdateTint(tint);
    public bool SetColor(MaterialEntry material, float4 color)
    {
        if (FailColor) return false;
        material.Color = color;
        return material.ApplyColor();
    }
    public bool CanReuse(KittenMaterialSet materials) => !Recycled && !materials.IsReleased;
    public string? GetMaterialName(int handle) => "stock/" + handle;
    public void Release(KittenMaterialSet materials)
    {
        if (materials.IsReleased) return;
        Events.Add("release:" + materials.Id);
        materials.IsReleased = true;
        Released++;
    }
}
