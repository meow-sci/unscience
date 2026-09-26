using System;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.HumbleArteestLib;

/// <summary>
/// KSA 5482 Vehicle Paint: paint bits are ORed into each tree's cached state flags by postfixes on
/// the private state writers, and a paint change invalidates those caches exactly once.
/// </summary>
internal static class VehiclePaintChecks
{
    internal static void Run()
    {
        var harmony = new Harmony("ksa-upgrade.tests.vehicle-paint");
        VehiclePaintPatches.Apply(harmony);
        try
        {
            Require(VehiclePaintPatches.AppliedPatchCount == VehiclePaintPatches.RequiredPatchCount,
                "all paint seams attach, including the private nested-batch writers");

            var owner = new PartTree();
            var tree = new PartTreeRenderData();
            var hull = new Part("hull", 1);
            var engine = new Part("engine", 2);
            tree.AddStatic(new PartModel(), hull);
            tree.AddDynamic(new PartModelDynamic(), new PartModelDynamicModule(engine));
            VehiclePaint.Enable();

            tree.EnsureBuilt(owner, 1);
            Require(tree.StaticFlags(0, 0) == 0b101 && tree.DynamicFlags(0, 0) == 0b11, "unpainted parts keep stock bits");
            int rewrites = tree.StateRewrites;
            tree.EnsureBuilt(owner, 2);
            Require(tree.StateRewrites == rewrites, "an unchanged paint state never invalidates the cache");

            var red = new float3(1f, 0f, 0f);
            VehiclePaint.SetPart(hull, red);
            tree.EnsureBuilt(owner, 3);
            Require(tree.StaticFlags(0, 0) == (0b101 | VehiclePaint.EncodeBits(red)), "per-part paint reaches static slots");
            Require(tree.DynamicFlags(0, 0) == 0b11, "other parts stay unpainted");

            var green = new float3(0f, 1f, 0f);
            VehiclePaint.SetTemplate("engine", green);
            tree.EnsureBuilt(owner, 4);
            Require(tree.DynamicFlags(0, 0) == (0b11 | VehiclePaint.EncodeBits(green)), "template paint reaches dynamic slots");

            rewrites = tree.StateRewrites;
            tree.EnsureBuilt(owner, 5);
            Require(tree.StateRewrites == rewrites, "each change invalidates exactly once");

            VehiclePaint.ClearPart(hull);
            tree.EnsureBuilt(owner, 6);
            Require(tree.StaticFlags(0, 0) == 0b101, "clearing paint rewrites stock bits");

            VehiclePaint.Disable();
            tree.EnsureBuilt(owner, 7);
            Require(tree.DynamicFlags(0, 0) == 0b11, "uninstalled shaders clear cached paint");

            var late = new PartTreeRenderData();
            late.AddStatic(new PartModel(), hull);
            late.EnsureBuilt(owner, 8);
            Require(late.StateRewrites == 1, "a newly seen tree is built once");
        }
        finally
        {
            VehiclePaint.ClearAllPaint();
            VehiclePaintPatches.Remove(harmony);
        }
        Require(VehiclePaintPatches.AppliedPatchCount == 0, "remove detaches every seam");
        Console.WriteLine("Vehicle Paint: cached state paint, template/part/global resolution and invalidation passed.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Vehicle Paint: " + message);
    }
}
