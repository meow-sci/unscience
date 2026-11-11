using System;
using System.Collections.Generic;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities;
using BepuUtilities.Collections;
using Brutal.Numerics;
using KSA;

namespace MeowSci.DohLib.Placement;

internal sealed partial class SurfaceScene
{
    public bool IsClear(SurfaceCapsule capsule, double3 centreEgo, Quaternion rotation,
        Celestial? body, Vehicle? skip = null)
    {
        using var query = new CapsuleCollisionQuery(ConstraintSim.ReadOnlyShapes, capsule.Shape, rotation);
        capsule.Shape.ComputeBounds(rotation, out var min, out var max);
        foreach (var vehicle in Vehicles)
        {
            if (ReferenceEquals(vehicle, skip) || vehicle.IsDisposed) continue;
            var matrix = vehicle.GetMatrixAsmb2Ego(Camera);
            var colliders = vehicle.Parts.Modules.Get<ColliderModule>();
            foreach (var collider in colliders)
            {
                var position = SurfaceCapsule.Small(collider.PositionVehicleAsmb.Transform(matrix) - centreEgo);
                var orientation = SurfaceCapsule.Small(collider.Collider2VehicleAsmb.Concatenate(vehicle.Asmb2Ego));
                if (!collider.Handle.Exists) throw new InvalidOperationException("A nearby collider is not ready.");
                var pose = new RigidPose(position, orientation);
                ConstraintSim.ReadOnlyShapes[collider.Handle.Type].ComputeBounds(collider.Handle.Index, pose, out var a, out var b);
                if (Overlaps(min, max, a, b)) query.Add(collider.Handle, pose);
            }
            if (colliders.Length == 0)
            {
                var states = vehicle.GetPhysicsStates();
                var p = Camera.GetPositionEgo(vehicle)
                    + double3.Unpack(states.Props.MassToGeometryAsmb).Transform(vehicle.Asmb2Ego) - centreEgo;
                var pose = new RigidPose(SurfaceCapsule.Small(p), SurfaceCapsule.Small(vehicle.Asmb2Ego));
                states.Props.BoundingBoxAsmb.ComputeBounds(pose.Orientation, out var a, out var b);
                if (Overlaps(min, max, a + pose.Position, b + pose.Position)) query.Add(states.Props.BoundingBoxAsmb, pose);
            }
        }
        AddStaticContacts(query, capsule.Shape, centreEgo, rotation);
        if (body != null) AddTerrainContacts(query, body, capsule.Shape, centreEgo);
        return !query.Penetrates();
    }

    private void AddStaticContacts(CapsuleCollisionQuery query, Capsule capsule, double3 centreEgo, Quaternion rotation)
    {
        foreach (var (sim, anchor) in _simulations)
        {
            var centrePhys = ToPhys(anchor, centreEgo);
            var bub2Cce = anchor.BubbleOrigin.GetBub2Cce();
            var orientation = Quaternion.Concatenate(rotation, SurfaceCapsule.Small(bub2Cce.Inverse()));
            capsule.ComputeBounds(orientation, out var min, out var max);
            var enumerator = new StaticCandidates(new List<StaticHandle>());
            var centre = SurfaceCapsule.Small(centrePhys);
            sim.Simulation.BroadPhase.GetOverlaps(min + centre, max + centre, sim.BufferPool, ref enumerator);
            foreach (var handle in enumerator.Handles)
            {
                var obstacle = sim.Simulation.Statics[handle];
                var position = (SurfaceCapsule.Double(obstacle.Pose.Position) - centrePhys).Transform(bub2Cce);
                var obstacleRotation = Quaternion.Concatenate(obstacle.Pose.Orientation, SurfaceCapsule.Small(bub2Cce));
                query.Add(obstacle.Shape, new RigidPose(SurfaceCapsule.Small(position), obstacleRotation));
            }
        }
    }

    private struct StaticCandidates(List<StaticHandle> handles) : IBreakableForEach<CollidableReference>
    {
        public List<StaticHandle> Handles = handles;
        public bool LoopBody(CollidableReference item)
        {
            if (item.Mobility == CollidableMobility.Static) Handles.Add(item.StaticHandle);
            return true;
        }
    }

    private static bool Overlaps(Vector3 min, Vector3 max, Vector3 a, Vector3 b) =>
        a.X <= max.X && a.Y <= max.Y && a.Z <= max.Z && b.X >= min.X && b.Y >= min.Y && b.Z >= min.Z;

    private void AddTerrainContacts(CapsuleCollisionQuery query, Celestial body, Capsule capsule, double3 centreEgo)
    {
        var centreCcf = (centreEgo - Camera.GetPositionEgo(body)).Transform(body.GetCce2Ccf());
        double depth = Depth(body, centreCcf);
        double extent = capsule.Radius + capsule.HalfLength + .15;
        if (!double.IsFinite(depth) || depth < 0) throw new InvalidOperationException("The kitten would be underground.");
        // Query a local accurate-height mesh in addition to any active native terrain statics.
        // Both windings prevent backface misses; active native terrain is tested separately above.
        int cells = (int)Math.Ceiling(extent * 2 / .1);
        if (cells > 64) throw new InvalidOperationException("This kitten collider is too large for terrain placement.");
        double step = extent * 2 / cells;
        var radial = centreCcf.NormalizeOrZero();
        var east = double3.Cross(Math.Abs(radial.Z) < .9 ? double3.UnitZ : double3.UnitY, radial).NormalizeOrZero();
        var north = double3.Cross(radial, east);
        var points = new Vector3[cells + 1, cells + 1];
        var ccf2Cce = body.GetCcf2Cce();
        var bodyEgo = Camera.GetPositionEgo(body);
        for (int x = 0; x <= cells; x++)
            for (int y = 0; y <= cells; y++)
            {
                var point = Surface(body, centreCcf + east * (x * step - extent) + north * (y * step - extent));
                var local = point.Transform(ccf2Cce) + bodyEgo - centreEgo;
                if (!SurfaceCapsule.Finite(local)) throw new InvalidOperationException("Terrain data is unavailable.");
                points[x, y] = SurfaceCapsule.Small(local);
            }
        for (int x = 0; x < cells; x++)
            for (int y = 0; y < cells; y++)
            {
                AddTriangle(query, points[x, y], points[x + 1, y], points[x, y + 1], (float)extent);
                AddTriangle(query, points[x + 1, y], points[x + 1, y + 1], points[x, y + 1], (float)extent);
            }
    }

    private static void AddTriangle(CapsuleCollisionQuery query, Vector3 a, Vector3 b, Vector3 c, float extent)
    {
        // A high centre point alone cannot rule out a nearby cliff. Sample the whole footprint,
        // then discard triangles whose bounds cannot touch even the capsule's enclosing sphere.
        if (!Overlaps(new Vector3(-extent), new Vector3(extent), Vector3.Min(a, Vector3.Min(b, c)),
                Vector3.Max(a, Vector3.Max(b, c)))) return;
        query.Add(new Triangle(a, b, c), RigidPose.Identity);
        query.Add(new Triangle(a, c, b), RigidPose.Identity);
    }
}
