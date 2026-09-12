using System;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.GodzillaLib;

internal static class ColliderScaleChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Near(double3 a, double3 b, string message) => Check((a - b).Length() < 1e-5, message);

    public static void Run()
    {
        var harmony = new Harmony("godzilla.colliders.tests");
        VisualScalePatches.Apply(harmony);
        ColliderScalePatches.Apply(harmony);
        try
        {
            var vessel = new Vehicle();
            var root = new Part { Scale = new(2), PositionParentAsmb = new(-1, 2, 0) };
            var child = new Part { PartParent = root, Scale = new(.5), PositionParentAsmb = new(1, 0, 0) };
            root.SubParts.Add(child);
            vessel.Parts.Parts.Add(root);
            var collider = new ColliderModule { Parent = child };
            child.Modules.Items.Add(collider);
            vessel.Parts.Modules.Items.Add(collider);
            root.RefreshScale();
            vessel.UpdateAfterPartTreeModification();
            ref var props = ref vessel.GetPhysicsStatesMutable().Props;
            props.Mass = 12; props.Inertia = 34; props.Aero = 56;
            var originalBounds = props;
            int physicsRefreshes = vessel.Refreshes;
            var snapshot = new VesselScaleSnapshot(vessel);

            snapshot.Apply(false, new(2, 3, 4), scalePhysics: false, scaleColliders: true);
            Check(root.Scale == new double3(2) && child.Scale == new double3(.5) && vessel.Refreshes == physicsRefreshes,
                "Collider-only edits must never rescale parts or refresh full physics");
            Check(collider.Shape.Radius == 4 && collider.NeedsColliderUpdate,
                "Collider dimensions use largest axis and request a worker broad-phase refresh");
            Near(collider.PositionVehicleAsmb, new(4, 6, 0), "Collider centers follow visual XYZ about COM");
            Check(props.BoundingSphereRadiusBody == originalBounds.BoundingSphereRadiusBody &&
                props.BoundingBoxAsmb.Width == originalBounds.BoundingBoxAsmb.Width &&
                props.GeometricCenterAsmb == originalBounds.GeometricCenterAsmb,
                "Collider rebuild must preserve all nominal bounds fields");
            Check(props.Mass == 12 && props.Inertia == 34 && props.Aero == 56, "Mass, inertia and aero stay untouched");
            var refresh = new ScaleFactors(99);
            collider.SetScale(in refresh);
            Check(refresh.Scale == 99, "Collider scaling must not overwrite the readonly input shared with other modules");
            Check(collider.Shape.Radius == 4, "Native module refresh cannot overwrite independent collider scale");
            collider.ThrowOnScale = true;
            try { collider.SetScale(in refresh); }
            catch (InvalidOperationException) { }
            collider.ThrowOnScale = false;
            Check(refresh.Scale == 99, "Restore shared scale input even when native shape creation throws");
            child.PositionParentAsmb = new(2, 0, 0);
            Near(collider.PositionVehicleAsmb, new(8, 6, 0), "Collider-only centers follow animated children");
            child.PositionParentAsmb = new(1, 0, 0);

            snapshot.Apply(true, new(5), scalePhysics: false, scaleColliders: false);
            Check(collider.Shape.Radius == 1 && collider.NeedsColliderUpdate, "Both off restores collider size and invalidates bounds");
            Near(collider.PositionVehicleAsmb, new(2, 2, 0), "Both off uses native collider positions");
            Check(vessel.Refreshes == physicsRefreshes, "Switching only colliders never rebuilds mass or modules");

            snapshot.Apply(true, new(3), scalePhysics: true, scaleColliders: false);
            Check(root.Scale == new double3(6) && collider.Shape.Radius == 1, "Physics on with colliders off keeps original shapes");
            Near(collider.PositionVehicleAsmb, new(2, 2, 0), "Physics-only preserves original collider layout");
            Check(props.BoundingSphereRadiusBody > originalBounds.BoundingSphereRadiusBody,
                "Physical bounds follow physics scaling independently of original colliders");
            child.PositionParentAsmb = new(2, 0, 0);
            Near(collider.PositionVehicleAsmb, new(4, 2, 0), "Original collider layout keeps child animation");
            child.PositionParentAsmb = new(1, 0, 0);

            snapshot.Apply(false, new(4, 5, 6), scalePhysics: true, scaleColliders: false);
            Check(collider.Shape.Radius == 1 && child.Scale == new double3(4, 5, 6),
                "Basic raw scales do not leak into original collider dimensions");
            Near(collider.PositionVehicleAsmb, new(2, 2, 0), "Basic restores authored child scales when reconstructing collider centers");
            snapshot.Apply(true, new(3), scalePhysics: true, scaleColliders: true);
            Check(collider.Shape.Radius == 3 && collider.NeedsColliderUpdate, "Both on restores native scaling and updates stationary bounds");
            Near(collider.PositionVehicleAsmb, new(6, 6, 0), "Both on uses native scaled layout");
            snapshot.Restore();
            Check(root.Scale == new double3(2) && collider.Shape.Radius == 1, "Restore removes both channels");

            vessel.ThrowOnColliderRebuild = true;
            var beforeFailure = props;
            try { snapshot.Apply(true, new(5), scalePhysics: false, scaleColliders: true); }
            catch (InvalidOperationException) { }
            Check(props.BoundingSphereRadiusBody == beforeFailure.BoundingSphereRadiusBody &&
                props.GeometricCenterAsmb == beforeFailure.GeometricCenterAsmb, "Exceptions restore nominal bounds");
            try { snapshot.Restore(); }
            catch (InvalidOperationException) { }
            vessel.ThrowOnColliderRebuild = false;
            snapshot.Restore();
            Check(collider.Shape.Radius == 1, "Failed restoration retains state for retry");

            snapshot.Apply(true, new(2), scalePhysics: false, scaleColliders: true);
            var added = new ColliderModule { Parent = root };
            vessel.Parts.Modules.Items.Add(added);
            Check(!ColliderScalePatches.TopologyMatches(vessel), "Detect collider module changes even with unchanged part topology");
            snapshot.Restore();
            Check(added.Shape.Radius == 1, "Restoration leaves newly added collider shapes alone");
            vessel.Parts.Modules.Items.Remove(added);

            var fallback = new VesselScaleSnapshot(new Vehicle());
            try
            {
                fallback.Apply(true, new(3), scalePhysics: false, scaleColliders: true);
                throw new Exception("Accepted unsupported fallback collider");
            }
            catch (InvalidOperationException) { }
            snapshot.Apply(true, new(2), scalePhysics: false, scaleColliders: true);
            ColliderScalePatches.Remove(harmony);
            Check(collider.Shape.Radius == 1 && !ColliderScalePatches.IsApplied, "Unload restores colliders before removing hooks");
            ColliderScalePatches.Apply(harmony);
            collider.SetScale(in refresh);
            Check(collider.Shape.Radius == 99, "Reload leaves no stale collider registrations");
            snapshot.Restore();
            Console.WriteLine("PASS: independent physics/collider channels, XYZ centers, animation, physical bounds preservation, broad-phase flags, rollback and unload/reload");
        }
        finally
        {
            ColliderScalePatches.Remove(harmony);
            VisualScalePatches.Remove(harmony);
        }
    }
}
