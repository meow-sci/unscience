using System.Collections;
using Brutal.Numerics;
using Core;
using KSA;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.WarPaintLib.Materials;
using MeowSci.WarPaintLib;
using System.Linq;

internal static partial class WarPaintChecks
{
    private static void NativeOwnership()
    {
        var original = new GpuObjectAssetRef(42);
        var key = (AssetName)"war_paint_0042_m0";
        var map = new Hashtable { [key] = original };
        MaterialSystemAccessor.SetAssetMap(map);
        Check(ReferenceEquals(MaterialSystemAccessor.GetMaterialIdentity(key.Name), original), "native bridge resolves structured asset keys by exact object");
        var set = new KittenMaterialSet("war_paint_0042", float4.One);
        set.AssetNames.Add(key.Name); set.AssetIdentities.Add(key.Name, original); set.AllMaterialHandles.Add(42);
        var factory = new MaterialFactory(); factory.Track(set);
        MaterialColorState.Record(42, Red);
        factory.Release(set); factory.Release(set);
        Check(original.DisposeCount == 1 && !map.Contains(key) && set.IsReleased,
            "production factory/bridge free exact owned native asset once");
        Check(MaterialColorState.GetOrDefault(42, Blue) == Blue, "released material color cache cannot color a recycled slot");

        original = new GpuObjectAssetRef(42);
        var replacement = new GpuObjectAssetRef(42);
        map[key] = replacement;
        set = new KittenMaterialSet("stale", Red);
        set.AssetNames.Add(key.Name); set.AssetIdentities.Add(key.Name, original);
        factory.Track(set); factory.Release(set);
        Check(ReferenceEquals(map[key], replacement) && replacement.DisposeCount == 0 && original.DisposeCount == 0,
            "same-name/same-handle replacement is never freed by stale owner");
        Check(!MaterialSystemAccessor.OwnsMaterial(key.Name, original)
            && MaterialSystemAccessor.OwnsMaterial(key.Name, replacement), "reuse requires exact native asset identity");
        replacement.Dispose();
        Check(MaterialSystemAccessor.GetMaterialIdentity(key.Name) == null,
            "disposed native asset cannot be reused through a surviving map entry");
        map.Clear();
        map[(AssetName)"alias-a"] = new GpuObjectAssetRef(2);
        map[(AssetName)"alias-b"] = new GpuObjectAssetRef(2);
        Check(MaterialSystemAccessor.GetFreeSlotCount() == 511 && MaterialSystemAccessor.GetAvailableSlots() == 447,
            "pool accounting deduplicates native handles and preserves 64 game slots");
        var reusable = new KittenMaterialSet("fur-rebind", Red);
        reusable.HandleMap.Add(1, 100); reusable.HandleMap.Add(2, 101); reusable.HandleMap.Add(3, 102);
        reusable.Materials.Add(new() { Name = "CatFur", Source = "Fur", Slot = 0, Handle = 102, Color = Red });
        var bindings = new[] { new KittenMaterialBinding("CharacterModel", [1, 2]), new KittenMaterialBinding("Fur", [9]) };
        Check(KittenMaterialReuse.TryRemapFur(bindings, reusable) && reusable.HandleMap[9] == 102
            && !reusable.HandleMap.ContainsKey(3) && reusable.HandleMap[1] == 100,
            "native reconstructed fur source reuses exact existing private fur slot");
        var previousMap = reusable.HandleMap.ToArray();
        Check(!KittenMaterialReuse.TryRemapFur(new[] { new KittenMaterialBinding("CharacterModel", [99]),
            new KittenMaterialBinding("Fur", [10]) }, reusable) && reusable.HandleMap.SequenceEqual(previousMap),
            "changed nonfur identity rejects reuse without mutating earlier map");
        Check(!KittenMaterialReuse.TryRemapFur(new[] { new KittenMaterialBinding("CharacterModel", [1, 2]),
            new KittenMaterialBinding("Fur", [10, 11]) }, reusable) && reusable.HandleMap.SequenceEqual(previousMap),
            "incompatible fur slot shape rejects reuse without mutating earlier map");
    }
}
