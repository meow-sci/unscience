using System;
using System.Linq;
using System.Numerics;
using BepuPhysics.Collidables;
using Brutal.Numerics;
using KSA;
using RenderCore;

namespace MeowSci.PebblesLib;

internal static class ClutterColliders
{
    public static ColliderTemplate Build(ColliderRecipe recipe, ClutterAssets assets, bool stockHull = false)
    {
        var d = recipe.Dimensions;
        ColliderTemplate result = recipe.Kind switch
        {
            ColliderKind.Box => new BoxColliderTemplate { LengthX = new(d.X), LengthY = new(d.Y), LengthZ = new(d.Z) },
            ColliderKind.Sphere => new SphereColliderTemplate { Radius = new(d.X / 2) },
            ColliderKind.Capsule => new CapsuleColliderTemplate { Radius = new(d.X / 2), LengthY = new(Math.Max(0, d.Y - d.X)) },
            ColliderKind.Cylinder => new CylinderColliderTemplate { Radius = new(d.X / 2), LengthY = new(d.Y) },
            ColliderKind.ConvexHull => new OwnedHull(assets.ResolveMesh(recipe.HullMeshId), recipe.HullScale, stockHull),
            _ => throw new NotSupportedException("Unknown collider kind.")
        };
        result.Id = recipe.Id;
        result.LocationAsmb = new Vector3Reference(new double3(recipe.Position.X, recipe.Position.Y, recipe.Position.Z));
        var rotation = recipe.RotationDegrees;
        result.Collider2Asmb = new Vector3Reference(new double3(rotation.X, rotation.Y, rotation.Z) * (Math.PI / 180));
        return result;
    }

    /// <summary>Actual proxy reach including local center; independent of visual replacement size.</summary>
    public static double Reach(ColliderTemplate collider)
    {
        var location = collider.LocationAsmb.ToDouble3().Length();
        return location + (collider switch
        {
            BoxColliderTemplate b => Math.Sqrt(Math.Pow(b.LengthX.InMeters(), 2) + Math.Pow(b.LengthY.InMeters(), 2) + Math.Pow(b.LengthZ.InMeters(), 2)) / 2,
            SphereColliderTemplate s => s.Radius.InMeters(),
            CapsuleColliderTemplate c => c.Radius.InMeters() + c.LengthY.InMeters() / 2,
            CylinderColliderTemplate c => Math.Sqrt(Math.Pow(c.Radius.InMeters(), 2) + Math.Pow(c.LengthY.InMeters() / 2, 2)),
            OwnedHull h => h.Radius,
            MeshColliderTemplate h => h.Mesh.Get().HostPrimitives.Max(p =>
                new double3(Math.Max(Math.Abs(p.PositionMinimum.X), Math.Abs(p.PositionMaximum.X)) * Math.Abs(h.ScaleValue.X),
                    Math.Max(Math.Abs(p.PositionMinimum.Y), Math.Abs(p.PositionMaximum.Y)) * Math.Abs(h.ScaleValue.Y),
                    Math.Max(Math.Abs(p.PositionMinimum.Z), Math.Abs(p.PositionMaximum.Z)) * Math.Abs(h.ScaleValue.Z)).Length()),
            _ => throw new NotSupportedException($"Cannot establish collider bounds for {collider.GetType().Name}.")
        });
    }

    private sealed class OwnedHull : ColliderTemplate
    {
        private readonly Vector3[] _points; // Recentred on their bounds centre, as stock hulls are (KSA 5447).
        private readonly double3 _center;
        private readonly double _volume;
        public double Radius { get; }
        public override double3 ShapeOffsetCollider => _center;
        // KSA 5447 weights compound child masses and centre of mass by volume; the base getter returns 0.
        public override double VolumeCubicMetres => _volume;
        public OwnedHull(MeshReference mesh, Vec3 scale, bool stockHull)
        {
            var points = (stockHull ? mesh.HostPrimitives.Take(1) : mesh.HostPrimitives).SelectMany(p => p.GetVertexSpan<float3>(MeshAttribute.Position).ToArray())
                .Select(p => new Vector3(p.X * scale.X, p.Y * scale.Y, p.Z * scale.Z)).Distinct().ToArray();
            if (points.Length is < 4 or > 65536) throw new InvalidOperationException("Convex hulls need 4–65,536 unique points.");
            Radius = points.Max(p => (double)p.Length());
            var boundsCentre = HullMath.BoundsCentre(points);
            _points = points.Select(p => p - boundsCentre).ToArray();
            using var unlock = ConstraintSim.UnlockShapes();
            if (!ConvexHullHelper.CreateShape(_points, unlock.BufferPool, out var center, out var hull))
                throw new InvalidOperationException("Convex hull is degenerate; choose closed geometry with volume.");
            try
            {
                var centroid = center + boundsCentre;
                _center = new double3(centroid.X, centroid.Y, centroid.Z);
                _volume = Volume(in hull);
            }
            finally { hull.Dispose(unlock.BufferPool); }
        }
        private static double Volume(in ConvexHull hull)
        {
            var faces = new Vector3[hull.FaceToVertexIndicesStart.Length][];
            for (var f = 0; f < faces.Length; f++)
            {
                hull.GetVertexIndicesForFace(f, out var indices);
                faces[f] = new Vector3[indices.Length];
                for (var v = 0; v < indices.Length; v++) hull.GetPoint(indices[v], out faces[f][v]);
            }
            return HullMath.Volume(faces);
        }
        protected override void CreateShapeInto(in ShapesUnlock unlock, double scale, out TypedIndex handle)
        {
            var points = _points.Select(p => p * (float)scale).ToArray();
            if (!ConvexHullHelper.CreateShape(points, unlock.BufferPool, out _, out var hull)) throw new InvalidOperationException("Scaled convex hull is degenerate.");
            handle = unlock.Shapes.Add(in hull);
        }
    }
}
