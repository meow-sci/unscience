using System;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.GodzillaLib;

internal static class VisualScaleChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Near(double3 actual, double3 expected, string message) =>
        Check((actual - expected).Length() < 0.001, message);

    public static void Run()
    {
        var harmony = new Harmony("godzilla.visual.tests");
        VisualScalePatches.Apply(harmony);
        try
        {
            var viewport = new TestViewport();
            var vessel = new Vehicle { CenterOfMassAsmb = new(3, 4, 5) };
            var part = new Part { Scale = new(2), PositionParentAsmb = new(8, 9, 10) };
            vessel.Parts.Parts.Add(part);
            var snapshot = new VesselScaleSnapshot(vessel);
            snapshot.Apply(true, new(10_000_000), true);
            Check(part.Scale == new double3(2) && vessel.Refreshes == 0 && part.Refreshes == 0,
                "Planet-sized visual edits must not touch part geometry or rebuild physics");
            Check(vessel.MeanRadius == 1, "Physical bounds stay original");
            viewport.Camera.PixelFactor = 0.000001;
            vessel.UpdateRenderData(viewport, 0);
            Check(vessel.Parts.Draws == 1, "Enlarged vessel survives render pixel culling");
            var rendered = vessel.Parts.RenderMatrix;
            Near(double3.Transform(vessel.CenterOfMassAsmb, rendered), viewport.Camera.Position,
                "Visual scale keeps the center of mass at the physical position");
            Near(double3.Transform(vessel.CenterOfMassAsmb + new double3(1, 0, 0), rendered),
                viewport.Camera.Position + new double3(10_000_000, 0, 0), "Apply visual scale once");
            var kittenMatrix = vessel.GetWorldMatrix(viewport.Camera);
            Check(kittenMatrix.HasValue, "Enlarged avatar survives render pixel culling");
            Check(kittenMatrix!.Value.Translation == float3.Pack(viewport.Camera.Position),
                "Avatar rendering must preserve world translation");
            var other = new Vehicle();
            other.UpdateRenderData(viewport, 0);
            Check(other.Parts.Draws == 0 && other.GetWorldMatrix(viewport.Camera) == null,
                "Unregistered vehicles keep native culling");

            snapshot.Apply(false, new(2, 3, 4), true);
            var matrix = vessel.GetMatrixAsmb2Ego(viewport.Camera);
            var originalMatrix = matrix;
            vessel.Parts.UpdateRenderData(in matrix, false, viewport, 0);
            Check(matrix == originalMatrix, "Restore caller's readonly matrix after submission");
            Near(double3.Transform(vessel.CenterOfMassAsmb + new double3(1), vessel.Parts.RenderMatrix),
                viewport.Camera.Position + new double3(2, 3, 4), "Whole-craft visual XYZ scales spacing and geometry");
            vessel.Parts.ThrowOnDraw = true;
            try { vessel.Parts.UpdateRenderData(in matrix, false, viewport, 0); }
            catch (InvalidOperationException) { }
            Check(matrix == originalMatrix, "Restore caller's matrix even when draw throws");
            vessel.Parts.ThrowOnDraw = false;

            snapshot.Restore();
            Check(vessel.Refreshes == 0 && vessel.GetWorldMatrix(viewport.Camera) == null,
                "Restoring visual-only session removes scaling without rebuilding physics");
            snapshot.Apply(false, new(8, 9, 10));
            snapshot.Apply(true, new(5), true);
            Check(part.Scale == new double3(2) && part.PositionParentAsmb == new double3(8, 9, 10),
                "Physical-to-visual toggle restores captured physics first");
            int refreshes = vessel.Refreshes;
            snapshot.Apply(true, new(7), true);
            Check(vessel.Refreshes == refreshes, "Repeated visual edits never refresh physics");
            snapshot.Apply(true, new(3));
            Check(part.Scale == new double3(6) && vessel.GetWorldMatrix(viewport.Camera) == null,
                "Visual-to-physical toggle removes render multiplier before physical scaling");
            snapshot.Restore();

            var kitten = new KittenEva();
            kitten.Renderable.Avatar.Core.Scale = .025f;
            var kittenSnapshot = new VesselScaleSnapshot(kitten);
            kittenSnapshot.Apply(true, new(10_000_000), true);
            Check(kitten.Renderable.Avatar.Core.Scale == .025f && kitten.Refreshes == 0,
                "Visual kitten size never enters character locomotion/collision scale");
            kittenSnapshot.Apply(true, new(2));
            kittenSnapshot.Apply(true, new(4), true);
            Check(kitten.Renderable.Avatar.Core.Scale == .025f && kitten.Renderable.Correction == new float3(1),
                "Toggling restores both avatar scale and shared Torch correction");
            kittenSnapshot.Restore();

            // A prefix supplied by another mod may skip the original GetWorldMatrix.
            var target = AccessTools.Method(typeof(Vehicle), nameof(Vehicle.GetWorldMatrix));
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(VisualScaleChecks), nameof(ExternalWorldPrefix)));
            snapshot.Apply(true, new(2), true);
            var external = vessel.GetWorldMatrix(viewport.Camera)!.Value;
            Check(external.Translation == new float3(42, 43, 44), "Compose with another mod's render position");
            Check(float3.Transform(new float3(1, 0, 0), external) == new float3(44, 43, 44),
                "Postfix scales an external prefix result once");
            harmony.Unpatch(target, AccessTools.Method(typeof(VisualScaleChecks), nameof(ExternalWorldPrefix)));

            VisualScalePatches.Remove(harmony);
            Check(!VisualScalePatches.IsApplied && vessel.GetWorldMatrix(viewport.Camera) == null,
                "Unload removes render hooks and all registrations");
            VisualScalePatches.Apply(harmony);
            Check(vessel.GetWorldMatrix(viewport.Camera) == null, "Reapply starts with no stale scales");
            snapshot.Apply(true, new(4), true);
            snapshot.Restore();
            Console.WriteLine("PASS: visual-only physics isolation, planet scale, culling, COM/XYZ transforms, exception cleanup, transitions, kitten scale, patch coexistence and reload");
        }
        finally { VisualScalePatches.Remove(harmony); }
    }

    private static bool ExternalWorldPrefix(ref float4x4? __result)
    {
        __result = float4x4.CreateTranslation(new float3(42, 43, 44));
        return false;
    }
}
