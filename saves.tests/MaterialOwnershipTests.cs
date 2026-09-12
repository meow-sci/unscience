using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using MeowSci.HumbleArteestLib;
using MeowSci.KsaAbstractions.Persistence;

internal static class MaterialOwnershipTests
{
    private readonly record struct AssetName(string Name) { public override string ToString() => Name; }
    private sealed class Asset(int handle) { public int Handle = handle; }

    public static void Run()
    {
        var key = new AssetName("stock/kitten");
        var asset = new Asset(42);
        KittenColor.Assets = new Hashtable { [key] = asset };
        var original = new float4(0.2f, 0.3f, 0.4f, 1);
        var edited = new float4(1, 0, 0, 1);
        MaterialColorState.Record(42, original);
        KittenColor.ApplyToMaterial(42, edited);
        Check(KittenColor.CaptureColors()["stock/kitten"] == edited, "structured asset key captures by stable name");
        KittenColor.Writes.Clear();
        KittenColor.ResetOwnedColors();
        Check(KittenColor.Writes.Single() == (42, original), "structured asset key restores exact owner's original");

        KittenColor.ApplyToMaterial(42, edited);
        KittenColor.Assets[key] = new Asset(42);
        KittenColor.Writes.Clear();
        KittenColor.ResetOwnedColors();
        Check(KittenColor.Writes.Count == 0, "recycled handle with replaced asset cannot receive stale reset");

        KittenColor.Assets[key] = asset;
        KittenColor.ApplyToMaterial(42, edited);
        asset.Handle = 43;
        KittenColor.Writes.Clear();
        KittenColor.ResetOwnedColors();
        Check(KittenColor.Writes.Count == 0, "moved asset cannot redirect stale reset");

        KittenColor.Assets = new Hashtable
        {
            [new AssetName("doh_private")] = new Asset(1),
            [new AssetName("free-fallin/material/1")] = new Asset(2)
        };
        KittenColor.ApplyToMaterial(1, edited);
        KittenColor.ApplyToMaterial(2, edited);
        Check(KittenColor.CaptureColors().Count == 0, "private generated names remain with owner participants");
        KittenColor.ResetOwnedColors();
        Console.WriteLine("Material ownership: structured keys, exact owners, recycled/moved slots and private names passed.");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}

namespace Brutal.Numerics
{
    public readonly record struct float4(float X, float Y, float Z, float W)
    {
        public static float4 One => new(1, 1, 1, 1);
    }
}

namespace MeowSci.HumbleArteestLib
{
    public static partial class KittenColor
    {
        private static IDictionary? _assetMap;
        internal static IDictionary Assets { get => _assetMap!; set => _assetMap = value; }
        internal static readonly List<(int, float4)> Writes = new();
        public static string? LastError => null;
        public static bool Initialize() => true;
        public static void RefreshMaterialCache() { }
        public static (string Name, int Handle)[] GetMaterials() => _assetMap!.Cast<DictionaryEntry>()
            .Select(item => (item.Key.ToString()!, ReadHandle(item.Value!)!.Value)).ToArray();
        private static bool WriteAlbedoColor(int handle, float4 color) { Writes.Add((handle, color)); return true; }
        public static bool ApplyToMaterial(int handle, float4 color)
        {
            var original = MaterialColorState.GetOrDefault(handle, float4.One);
            if (!WriteAlbedoColor(handle, color)) return false;
            TrackColor(handle, original, color);
            MaterialColorState.Record(handle, color);
            return true;
        }
    }
}
