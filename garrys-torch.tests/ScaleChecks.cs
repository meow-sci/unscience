using System;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.GarrysTorchLib;
using MeowSci.KsaAbstractions;

internal static class ScaleChecks
{
    public static void Run()
    {
        var source = new Vehicle();
        var root = new Part { Scale = new(2, 3, 4), PositionParentAsmb = new(10, 20, 30) };
        var child = new Part { Scale = new(.2, 5, .7), PartParent = root };
        var nested = new Part { Scale = new(3, .4, 2), PartParent = child };
        root.SubParts.Add(child);
        child.SubParts.Add(nested);
        var second = new Part { Scale = new(.5, 2, 7) };
        source.Parts.Parts.AddRange(new[] { root, second });
        var originalTotal = nested.ScaleTotal; // Warm the cache before scaling the parent.

        // Exact reported regression: create at identity, then unweld without ever scaling.
        WeldEngine.CaptureVehicleScale(source);
        int writes = root.Writes + child.Writes + nested.Writes;
        WeldEngine.RestoreVehicleScale(source);
        Require(root.Writes + child.Writes + nested.Writes == writes, "identity unweld must not write scale");
        Equal(child.Scale, new(.2, 5, .7), "authored child scale survives identity unweld");

        WeldEngine.CaptureVehicleScale(source);
        WeldEngine.ApplyVehicleScale(source, new float3(2, 3, .5f));
        Equal(root.Scale, new(4, 9, 2), "multiply original full-part XYZ scale");
        Equal(second.Scale, new(1, 6, 3.5), "each full part keeps its own baseline");
        Equal(nested.ScaleTotal, new(originalTotal.X * 2, originalTotal.Y * 3, originalTotal.Z * .5),
            "nested effective scale receives factor once, with invalidated caches");
        Equal(root.PositionParentAsmb, new(10, 20, 30), "preserve existing full-part spacing");
        Equal(child.Scale, new(.2, 5, .7), "never overwrite child local scale");

        for (int i = 0; i < 100; i++)
        {
            WeldEngine.ApplyVehicleScale(source, new float3(.5f, 2, 4));
            WeldEngine.ApplyVehicleScale(source, new float3(2, 3, .5f));
        }
        Equal(root.Scale, new(4, 9, 2), "edits do not compound or drift");
        child.Scale = new(.6, .8, 1.2); // Game-owned animation during welding.
        child.PositionParentAsmb = new(6, 7, 8);
        WeldEngine.ApplyVehicleScale(source, WeldScale.Identity);
        Equal(root.Scale, new(2, 3, 4), "identity restores original while retaining session");
        Equal(child.Scale, new(.6, .8, 1.2), "scale edits preserve current child animation");
        WeldEngine.RestoreVehicleScale(source);
        Equal(child.PositionParentAsmb, new(6, 7, 8), "restore does not rewind animation transforms");
        Equal(child.Scale, new(.6, .8, 1.2), "restore does not rewind animated local scale");

        // Restoration releases the old baseline: a later weld must capture the latest instance.
        root.Scale = new(7, 8, 9);
        WeldEngine.ApplyVehicleScale(source, 2f);
        Equal(root.Scale, new(14, 16, 18), "uniform API captures a fresh baseline after unweld");
        source.Parts.Parts.Remove(second);
        var added = new Part { Scale = new(4, 5, 6) };
        source.Parts.Parts.Add(added);
        WeldEngine.ApplyVehicleScale(source, 3f);
        Equal(added.Scale, new(12, 15, 18), "new full part captures its original on first edit");
        WeldEngine.RestoreVehicleScale(source);
        Equal(root.Scale, new(7, 8, 9), "surviving original restores after topology change");
        Equal(second.Scale, new(1, 4, 14), "detached part is not mutated during edit or restore");
        Equal(added.Scale, new(4, 5, 6), "newly scaled part restores its own baseline");
        int restoredWrites = root.Writes;
        WeldEngine.RestoreVehicleScale(source);
        Require(root.Writes == restoredWrites, "repeated restore is a no-op");

        var unrestrictedScale = new float3(1f / 128f, 32f, 128f);
        WeldEngine.ApplyVehicleScale(source, unrestrictedScale);
        Equal(root.Scale, new(7d / 128, 256, 1152), "accept axes below .05 and above 20 without clamping");
        WeldEngine.RestoreVehicleScale(source);
        Equal(root.Scale, new(7, 8, 9), "restore after unrestricted scaling");

        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -1f })
        {
            try { WeldEngine.ApplyVehicleScale(source, new float3(1, invalid, 1)); }
            catch (ArgumentOutOfRangeException) { continue; }
            throw new Exception("invalid scale accepted");
        }
        Equal(root.Scale, new(7, 8, 9), "invalid scale cannot partially mutate source");
        CheckAnimations(source, root);
        CheckKitten();

        WeldEngine.ApplyVehicleScale(source, 2f);
        source.IsDisposed = true;
        restoredWrites = root.Writes;
        WeldEngine.RestoreVehicleScale(source);
        WeldEngine.ApplyVehicleScale(source, 3f);
        Require(root.Writes == restoredWrites, "disposed sources are never touched");
        Console.WriteLine("PASS: authored/nested scales, identity unweld, edits, animation, topology, recapture and kitten restoration");
    }

    private static void CheckAnimations(Vehicle source, Part root)
    {
        var weld = new WeldEntry { Source = source };
        var manager = new WeldAnimationManager();
        var animation = new WeldAnimation(default, default, WeldScale.Identity,
            default, default, new float3(3, 2, .5f), 1, EasingType.Linear);
        manager.Enqueue(weld, animation);
        manager.Enqueue(weld, new WeldAnimation(default, default, WeldScale.Identity,
            default, default, WeldScale.Identity, 1, EasingType.Linear));
        manager.Update(.5);
        Equal(root.Scale, new(14, 12, 6.75), "animation midpoint uses original scales");
        manager.Update(.5);
        Equal(root.Scale, new(21, 16, 4.5), "animation completion uses original scales");
        manager.Update(1);
        Equal(root.Scale, new(7, 8, 9), "queued identity animation restores original proportions");
        manager.Enqueue(weld, new WeldAnimation(default, default, WeldScale.Identity,
            default, default, new float3(1f / 128f, 32f, 128f), 1, EasingType.Linear));
        manager.Update(.5);
        Equal(root.Scale, new(7 * (1 + 1d / 128) / 2, 132, 580.5), "animation crosses former limits");
        manager.Update(.5);
        Equal(root.Scale, new(7d / 128, 256, 1152), "animation completes outside former limits");
        manager.Clear();
        WeldEngine.RestoreVehicleScale(source);
    }

    private static void CheckKitten()
    {
        var kitten = new KittenEva();
        kitten.Renderable.Avatar!.Core.Scale = .025f;
        _ = kitten.Renderable.ReadMatrix(); // Warm up before installing the real correction patch.
        var harmony = new Harmony("garrys-torch.scale.tests");
        KittenScalePatches.Apply(harmony);
        try
        {
            WeldEngine.ApplyVehicleScale(kitten, new float3(2, 3, 4));
            Require(kitten.Renderable.Avatar.Core.Scale == .05f, "kitten uses captured scalar, not stock .01");
            var expected = float4x4.CreateScale(new float3(1, 1.5f, 2)) *
                float4x4.CreateScale(new float3(.05f));
            Require(kitten.Renderable.ReadMatrix().Equals(expected), "kitten retains XYZ render correction");
            WeldEngine.RestoreVehicleScale(kitten);
            Require(kitten.Renderable.Avatar.Core.Scale == .025f, "kitten restores original scalar");
            Require(kitten.Renderable.ReadMatrix().Equals(float4x4.CreateScale(new float3(.025f))),
                "unweld removes anisotropic correction");
        }
        finally { KittenScalePatches.Remove(harmony); }

        kitten.Renderable.Avatar = null;
        try { WeldEngine.CaptureVehicleScale(kitten); }
        catch (InvalidOperationException)
        {
            kitten.Renderable.Avatar = new();
            WeldEngine.CaptureVehicleScale(kitten); // Failed capture must not leave a broken snapshot.
            WeldEngine.RestoreVehicleScale(kitten);
            return;
        }
        throw new Exception("unready kitten must fail before mutating scales");
    }

    private static void Equal(double3 actual, double3 expected, string message) => Require(
        Math.Abs(actual.X - expected.X) < 1e-10 && Math.Abs(actual.Y - expected.Y) < 1e-10 &&
        Math.Abs(actual.Z - expected.Z) < 1e-10, message);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
