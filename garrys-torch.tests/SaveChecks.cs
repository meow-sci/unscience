using System;
using Brutal.Numerics;
using KSA;
using MeowSci.GarrysTorchLib;
using MeowSci.KsaAbstractions.Persistence;

internal static class SaveChecks
{
    internal static void Run()
    {
        var source = new Vehicle();
        source.Parts.Parts.Add(new Part { Scale = new(2, 3, 4) });
        WeldEngine.CaptureVehicleScale(source);
        WeldEngine.ApplyVehicleScale(source, new float3(3, 4, 5));
        var saved = SaveJson.FromElement<SavedWeldScale>(SaveJson.ToElement(WeldEngine.CaptureSavedScale(source)));
        for (int i = 0; i < 3; i++)
        {
            var loaded = new Vehicle();
            loaded.Parts.Parts.Add(new Part { Scale = new(6, 12, 20) });
            SavedPartReference.Current = loaded;
            WeldEngine.ImportSavedScale(loaded, saved);
            WeldEngine.ApplyVehicleScale(loaded, new float3(3, 4, 5));
            Require(loaded.Parts.Parts[0].Scale == new double3(6, 12, 20), "load compounded source scale");
            saved = SaveJson.FromElement<SavedWeldScale>(SaveJson.ToElement(WeldEngine.CaptureSavedScale(loaded)));
            WeldEngine.RestoreVehicleScale(loaded);
            Require(loaded.Parts.Parts[0].Scale == new double3(2, 3, 4), "unweld lost original size");
        }
        var kitten = new KittenEva();
        kitten.Renderable.Avatar!.Core.Scale = .025f;
        WeldEngine.ApplyVehicleScale(kitten, new float3(2, 3, 4));
        var catSaved = SaveJson.FromElement<SavedWeldScale>(SaveJson.ToElement(WeldEngine.CaptureSavedScale(kitten)));
        var catLoaded = new KittenEva();
        SavedPartReference.Current = catLoaded;
        WeldEngine.ImportSavedScale(catLoaded, catSaved);
        WeldEngine.ApplyVehicleScale(catLoaded, new float3(2, 3, 4));
        Require(catLoaded.Renderable.Avatar!.Core.Scale == .05f, "load lost nondefault kitten avatar scale");
        WeldEngine.RestoreVehicleScale(catLoaded);
        Require(catLoaded.Renderable.Avatar.Core.Scale == .025f, "unweld lost original kitten scale");
        // A new full part attached while a fixed weld is idle has not yet received its factor.
        source.Parts.Parts.Add(new Part { Scale = new(7, 8, 9) });
        var topologySaved = SaveJson.FromElement<SavedWeldScale>(SaveJson.ToElement(WeldEngine.CaptureSavedScale(source)));
        var topologyLoaded = new Vehicle();
        topologyLoaded.Parts.Parts.Add(new Part { Scale = new(6, 12, 20) });
        topologyLoaded.Parts.Parts.Add(new Part { Scale = new(7, 8, 9) });
        SavedPartReference.Current = topologyLoaded;
        WeldEngine.ImportSavedScale(topologyLoaded, topologySaved, new float3(3, 4, 5));
        Require(topologyLoaded.Parts.Parts[1].Scale == new double3(7, 8, 9), "restore prematurely scaled a newly attached part");
        WeldEngine.RestoreVehicleScale(topologyLoaded);
        Require(topologyLoaded.Parts.Parts[0].Scale == new double3(2, 3, 4)
            && topologyLoaded.Parts.Parts[1].Scale == new double3(7, 8, 9), "newly attached part lost its own baseline");
        Console.WriteLine("PASS: saved weld original XYZ/avatar baselines, repeated loads and unweld restoration");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
