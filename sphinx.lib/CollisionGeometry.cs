using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MeowSci.SphinxLib;

internal enum CollisionMode { Auto, Mesh, Box, Off }
internal readonly record struct CollisionTriangle(Vector3 A, Vector3 B, Vector3 C);

/// <summary>Conservative geometry detection: only a complete closed bounds box becomes a box.</summary>
internal static class CollisionGeometry
{
    public const int MaxTriangles = 100_000;
    public static bool IsBox(IReadOnlyList<CollisionTriangle> triangles, Vector3 min, Vector3 max)
    {
        var size = max - min;
        if (size.X <= 0 || size.Y <= 0 || size.Z <= 0) return false;
        var tolerance = Vector3.Max(size * .00001f, new Vector3(.000001f));
        var faces = Enumerable.Range(0, 6).Select(_ => new HashSet<int>()).ToArray();
        foreach (var triangle in triangles)
        {
            int a = Corner(triangle.A), b = Corner(triangle.B), c = Corner(triangle.C);
            if (a < 0 || b < 0 || c < 0 || a == b || b == c || a == c) return false;
            int face = -1;
            for (int axis = 0; axis < 3; axis++)
                if ((a & (1 << axis)) == (b & (1 << axis)) && (a & (1 << axis)) == (c & (1 << axis)))
                    face = axis * 2 + ((a >> axis) & 1);
            if (face < 0) return false;
            faces[face].Add((1 << a) | (1 << b) | (1 << c));
            if (faces[face].Count > 2) return false;
        }
        foreach (var face in faces)
        {
            if (face.Count != 2) return false;
            var pair = face.ToArray();
            int shared = pair[0] & pair[1];
            var corners = Enumerable.Range(0, 8).Where(i => (shared & (1 << i)) != 0).ToArray();
            // Two triangles must share the face diagonal, not an edge (which could overlap).
            if (corners.Length != 2 || BitOperations.PopCount((uint)(corners[0] ^ corners[1])) != 2) return false;
        }
        return true;

        int Corner(Vector3 point)
        {
            if (!Finite(point)) return -1;
            int corner = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                if (MathF.Abs(point[axis] - min[axis]) <= tolerance[axis]) continue;
                if (MathF.Abs(point[axis] - max[axis]) > tolerance[axis]) return -1;
                corner |= 1 << axis;
            }
            return corner;
        }
    }

    public static CollisionTriangle[] Transform(IEnumerable<CollisionTriangle> triangles, Matrix4x4 matrix, Vector3 center)
    {
        var result = new List<CollisionTriangle>();
        foreach (var t in triangles)
        {
            var a = Vector3.Transform(t.A, matrix) - center;
            var b = Vector3.Transform(t.B, matrix) - center;
            var c = Vector3.Transform(t.C, matrix) - center;
            if (!Finite(a) || !Finite(b) || !Finite(c)) throw new ArgumentException("Collider coordinates must be finite.");
            if (Vector3.Cross(b - a, c - a).LengthSquared() <= 1e-16f) continue;
            result.Add(new(a, b, c));
            if (result.Count > MaxTriangles) throw new ArgumentException("Mesh collision exceeds 100,000 triangles. Choose Fitted box or simplify the GLB.");
        }
        if (result.Count == 0) throw new ArgumentException("The model has no nondegenerate collision triangles. Choose Fitted box or Off.");
        return result.ToArray();
    }
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}
