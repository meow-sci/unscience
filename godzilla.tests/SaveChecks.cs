using System;
using Brutal.Numerics;
using KSA;
using MeowSci.GodzillaLib;
using MeowSci.KsaAbstractions.Persistence;

internal static class SaveChecks
{
    internal static void Run()
    {
        var axes = new float3(.25f, 8, 128);
        var copiedAxes = SaveJson.FromElement<float3>(SaveJson.ToElement(axes));
        Require(copiedAxes == axes, "float3 components did not round-trip");
        var quaternion = new doubleQuat(.1, .2, .3, .9);
        var copiedQuaternion = SaveJson.FromElement<doubleQuat>(SaveJson.ToElement(quaternion));
        Require(copiedQuaternion.X == quaternion.X && copiedQuaternion.Y == quaternion.Y
            && copiedQuaternion.Z == quaternion.Z && copiedQuaternion.W == quaternion.W, "quaternion components did not round-trip");
        var pixels = new (int x, int y)[] { (4, 7), (8, 1) };
        var copiedPixels = SaveJson.FromElement<(int x, int y)[]>(SaveJson.ToElement(pixels));
        Require(copiedPixels[0] == pixels[0] && copiedPixels[1] == pixels[1], "sparse grid coordinates did not round-trip");
        foreach (bool smart in new[] { false, true })
        {
            var original = Craft(new(2, 3, 4), new(.2, .3, .4), new(5, 2, 1));
            original.CenterOfMassAsmb = new(1, 2, 3);
            var before = new VesselScaleSnapshot(original);
            var factor = smart ? new float3(3) : new float3(4, 5, 6);
            before.Apply(smart, factor);
            var saved = SaveJson.FromElement<SavedVesselScaleBaseline>(SaveJson.ToElement(before.CaptureSaved()));
            var effective = original.Parts.Parts[0].Scale;
            var position = original.Parts.Parts[0].PositionParentAsmb;
            for (int load = 0; load < 3; load++)
            {
                // Native loading saves the modified full part, but recreates subparts from templates.
                var loaded = Craft(effective, new(.2, .3, .4), position);
                loaded.CenterOfMassAsmb = new(999); // Must use the original saved pivot, not recapture COM.
                SavedPartReference.Current = loaded;
                var restored = new VesselScaleSnapshot(loaded, saved);
                restored.RestoreSavedSubpartScales(saved);
                restored.Apply(smart, factor);
                Require(loaded.Parts.Parts[0].Scale == effective, "repeated load compounds scale");
                Require(loaded.Parts.Parts[0].PositionParentAsmb == position, "repeated load shifts layout");
                saved = SaveJson.FromElement<SavedVesselScaleBaseline>(SaveJson.ToElement(restored.CaptureSaved()));
                restored.Restore();
                Require(loaded.Parts.Parts[0].Scale == new double3(2, 3, 4), "restore loses original scale");
                Require(loaded.Parts.Parts[0].PositionParentAsmb == new double3(5, 2, 1), "restore loses original layout");
                if (!smart) Require(loaded.Parts.Parts[0].SubParts[0].Scale == new double3(.2, .3, .4), "Basic restore loses authored child scale");
            }
            var missing = Craft(effective, new(.2), position);
            missing.Parts.Parts[0].SubParts.Clear();
            SavedPartReference.Current = missing;
            try { _ = new VesselScaleSnapshot(missing, saved); throw new Exception("Accepted missing saved subpart."); }
            catch (InvalidOperationException) { }
            Require(missing.Parts.Parts[0].Scale == effective, "failed baseline validation changes loaded geometry");
        }
        Console.WriteLine("PASS: saved Smart/Basic baselines round-trip, repeated load, original Restore and missing-part rejection");
    }

    private static Vehicle Craft(double3 scale, double3 childScale, double3 position)
    {
        var vehicle = new Vehicle();
        var root = new Part { Scale = scale, PositionParentAsmb = position };
        root.SubParts.Add(new Part { PartParent = root, Scale = childScale });
        vehicle.Parts.Parts.Add(root);
        return vehicle;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
