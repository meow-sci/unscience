using System;
using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using MeowSci.DohLib.Placement;

internal static partial class Checks
{
    private static void PlacementCollisionChecks()
    {
        var pool = new BufferPool();
        var shapes = new Shapes(pool, 16);
        try
        {
            var capsule = new Capsule(.35f, .45f);
            bool BoxHit(Box box, Vector3 position, Quaternion orientation)
            {
                using var query = new CapsuleCollisionQuery(shapes, capsule, Quaternion.Identity);
                query.Add(box, new RigidPose(position, orientation));
                return query.Penetrates();
            }
            Require(!BoxHit(new Box(10, 1, 10), new Vector3(0, -.5f - .595f, 0), Quaternion.Identity),
                "real Bepu accepts 2 cm capsule/floor clearance");
            Require(BoxHit(new Box(10, 1, 10), new Vector3(0, -.5f - .555f, 0), Quaternion.Identity),
                "real Bepu rejects 2 cm floor penetration");
            Require(BoxHit(new Box(.2f, 2, 2), new Vector3(.39f, 0, 0), Quaternion.Identity),
                "real capsule query detects a wall beside a valid floor hit");
            Require(BoxHit(new Box(2, .2f, 2), new Vector3(0, 1.2f - .595f, 0), Quaternion.Identity),
                "real capsule query detects a low ceiling despite foot clearance");
            Require(!BoxHit(new Box(.2f, 2, 2), new Vector3(.5f, 0, 0), Quaternion.Identity),
                "nearby nonpenetrating objects do not falsely reject a placement");
            for (int i = -3; i <= 3; i++)
            {
                var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, i * .3f);
                var normal = Vector3.Transform(Vector3.UnitY, rotation);
                using var query = new CapsuleCollisionQuery(shapes, capsule, rotation);
                query.Add(new Box(10, 1, 10), new RigidPose(-normal * 1.095f, rotation));
                Require(!query.Penetrates(), "rotated slope has the same capsule clearance as a flat floor");
            }
            var sharedBox = shapes.Add(new Box(.2f, 2, 2));
            using (var query = new CapsuleCollisionQuery(shapes, capsule, Quaternion.Identity))
            {
                query.Add(sharedBox, new RigidPose(new Vector3(.39f, 0, 0)));
                Require(query.Penetrates(), "production raw shared-shape query detects a scaled/native collider");
            }
            Require(shapes.GetShape<Box>(sharedBox.Index).HalfWidth == .1f,
                "detached queries neither mutate nor dispose borrowed native shapes");
            pool.Take<CompoundChild>(1, out var children);
            children[0] = new CompoundChild { ShapeIndex = sharedBox, LocalPosition = new Vector3(.39f, 0, 0), LocalOrientation = Quaternion.Identity };
            var compound = shapes.Add(new Compound(children));
            using (var query = new CapsuleCollisionQuery(shapes, capsule, Quaternion.Identity))
            {
                query.Add(compound, RigidPose.Identity);
                Require(query.Penetrates(), "native compound static children participate in the full volume query");
            }
            shapes.RemoveAndDispose(compound, pool);
            shapes.RemoveAndDispose(sharedBox, pool);
            using (var query = new CapsuleCollisionQuery(shapes, capsule, Quaternion.Identity))
            {
                query.Add(capsule, new RigidPose(new Vector3(.5f, 0, 0)));
                Require(query.Penetrates(), "another kitten is an obstruction");
            }
            using (var query = new CapsuleCollisionQuery(shapes, capsule, Quaternion.Identity))
            {
                query.Add(new Triangle(new Vector3(-2, -.6f, -2), new Vector3(2, -.6f, -2), new Vector3(0, -.6f, 2)), RigidPose.Identity);
                query.Add(new Triangle(new Vector3(-2, -.6f, -2), new Vector3(0, -.6f, 2), new Vector3(2, -.6f, -2)), RigidPose.Identity);
                Require(!query.Penetrates(), "two-sided sampled terrain below the capsule remains clear");
            }
            pool.Take<Triangle>(2, out var triangles);
            triangles[0] = new Triangle(new Vector3(-2, -.55f, -2), new Vector3(2, -.55f, -2), new Vector3(0, -.55f, 2));
            triangles[1] = new Triangle(triangles[0].A, triangles[0].C, triangles[0].B);
            var mesh = shapes.Add(new Mesh(triangles, Vector3.One, pool));
            using (var query = new CapsuleCollisionQuery(shapes, capsule, Quaternion.Identity))
            {
                query.Add(mesh, RigidPose.Identity);
                Require(query.Penetrates(), "native mesh statics and terrain triangles reject penetration");
            }
            shapes.RemoveAndDispose(mesh, pool);
        }
        finally { shapes.Dispose(); pool.Clear(); }
    }
}
