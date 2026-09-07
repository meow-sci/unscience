using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BepuPhysics.Collidables;
using KSA;
using MeowSci.PebblesLib;
using RenderCore;

namespace MeowSci.SphinxLib;

/// <summary>One owned global shape, shared by this entry's statics in separate physics bubbles.</summary>
internal sealed class StaticCollider : IDisposable
{
    public TypedIndex Shape { get; private set; }
    public Vector3 Center { get; private init; }
    public Quaternion Rotation { get; private init; } = Quaternion.Identity;
    public float Radius { get; private init; }
    public int TriangleCount { get; private init; }
    public string Description { get; private init; } = "";

    // Caller has waited for vehicle/cloth solvers before constructing or retiring any shape.
    public static StaticCollider? Build(ClutterAssets assets, SphinxEntry entry, CollisionMode mode,
        Vector3 scale, Vector3 rotation, Vector3 offset, int remainingTriangles)
    {
        if (mode == CollisionMode.Off) return null;
        var min = entry.Model.Min; var max = entry.Model.Max;
        var matrix = PlacementMath.GroundedLocal(min, max, scale, rotation, offset);
        var center = Vector3.Transform((min + max) * .5f, matrix);
        var size = (max - min) * scale;
        if (!float.IsFinite(size.Length()) || size.Length() > 20_000 || center.Length() > 100_000)
            throw new ArgumentException("Colliders support up to 20 km diagonal and 100 km local offsets. Reduce scale/offset or choose Off.");
        var mesh = assets.ResolveMesh(entry.MeshId);
        int count = mesh.HostPrimitives.Sum(p => p.IndexBuffer!.Count / 3);
        var triangles = new List<CollisionTriangle>();
        if (mode != CollisionMode.Box && count <= CollisionGeometry.MaxTriangles)
        {
            foreach (var primitive in mesh.HostPrimitives)
            {
                var positions = primitive.GetVertexSpan<Vector3>(MeshAttribute.Position);
                var indices = primitive.IndexBuffer!.AsSpan<uint>();
                for (int i = 0; i < indices.Length; i += 3)
                    triangles.Add(new(positions[(int)indices[i]], positions[(int)indices[i+1]], positions[(int)indices[i+2]]));
            }
        }
        bool fallback = mode == CollisionMode.Auto && count > CollisionGeometry.MaxTriangles;
        bool box = mode == CollisionMode.Box || fallback || (mode == CollisionMode.Auto && CollisionGeometry.IsBox(triangles, min, max));
        using var unlock = ConstraintSim.UnlockShapes();
        if (box)
        {
            Matrix4x4.Decompose(matrix, out _, out var orientation, out _);
            var shape = new Box(Math.Max(.001f, size.X), Math.Max(.001f, size.Y), Math.Max(.001f, size.Z));
            return new StaticCollider { Shape = unlock.Shapes.Add(in shape), Center = center, Rotation = orientation,
                Radius = size.Length() * .5f, Description = fallback
                    ? "Fitted box fallback: over 100,000 triangles; openings are blocked. Simplify the GLB for mesh collision."
                    : mode == CollisionMode.Auto ? "Auto-detected closed box" : "Fitted box (fills openings)" };
        }
        if (count > CollisionGeometry.MaxTriangles)
            throw new ArgumentException("Mesh collision exceeds 100,000 triangles. Choose Auto, Fitted box or simplify the GLB.");
        var transformed = CollisionGeometry.Transform(triangles, matrix, center);
        if (transformed.Length > remainingTriangles)
            throw new ArgumentException("Sphinx has reached 500,000 collision triangles. Remove statics or use Fitted box.");
        // Bepu triangles are one-sided. Both windings make imported surfaces solid from either side.
        unlock.BufferPool.Take<Triangle>(checked(transformed.Length * 2), out var buffer);
        Mesh native = default;
        bool ownsMesh = false;
        try
        {
            for (int i = 0; i < transformed.Length; i++)
            {
                var t = transformed[i];
                buffer[i*2] = new Triangle(t.A, t.B, t.C);
                buffer[i*2+1] = new Triangle(t.A, t.C, t.B);
            }
            native = new Mesh(buffer, Vector3.One, unlock.BufferPool); ownsMesh = true;
            var handle = unlock.Shapes.Add(in native);
            return new StaticCollider { Shape = handle, Center = center, Radius = size.Length() * .5f,
                TriangleCount = transformed.Length, Description = $"Mesh: {transformed.Length:N0} triangles (two-sided)" };
        }
        catch
        {
            if (ownsMesh) native.Dispose(unlock.BufferPool); else unlock.BufferPool.Return(ref buffer);
            throw;
        }
    }
    public void Dispose()
    {
        if (!Shape.Exists) return;
        using var unlock = ConstraintSim.UnlockShapes();
        unlock.Shapes.RemoveAndDispose(Shape, unlock.BufferPool);
        Shape = default;
    }
}
