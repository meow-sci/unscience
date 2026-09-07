using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MeowSci.SphinxLib;

internal static class CollisionGeometryChecks
{
    public static void Run()
    {
        var min = new Vector3(-2, 3, -7); var max = new Vector3(5, 9, 1);
        var box = Box(min, max);
        Check(CollisionGeometry.IsBox(box, min, max), "closed box detected");
        Check(CollisionGeometry.IsBox(box.Concat(box.Select(t => new CollisionTriangle(t.A, t.C, t.B))).ToArray(), min, max), "duplicated backfaces still a box");
        Check(!CollisionGeometry.IsBox(box.Skip(1).ToArray(), min, max), "open box retains opening");
        var dented = box.ToArray();
        dented[0] = dented[0] with { A = (min + max) / 2 };
        Check(!CollisionGeometry.IsBox(dented, min, max), "interior vertex cannot become a solid box");
        var nan = box.ToArray(); nan[0] = nan[0] with { A = new Vector3(float.NaN) };
        Check(!CollisionGeometry.IsBox(nan, min, max), "nonfinite vertices rejected");
        Check(!CollisionGeometry.IsBox(Array.Empty<CollisionTriangle>(), min, max), "empty mesh is not box");
        // A four-corner face can still be two overlapping triangles if the common edge is not a diagonal.
        var overlap = box.ToArray(); overlap[1] = new(box[0].A, box[0].B, box[1].C);
        Check(!CollisionGeometry.IsBox(overlap, min, max), "overlapping face triangles rejected");
        var flat = Box(new(-1, 0, -1), new(1, 0, 1));
        Check(!CollisionGeometry.IsBox(flat, new(-1, 0, -1), new(1, 0, 1)), "planar mesh not a closed box");

        var random = new Random(315);
        for (int test = 0; test < 200; test++)
        {
            var scale = new Vector3(Next(.01f, 50), Next(.01f, 50), Next(.01f, 50));
            var rotation = new Vector3(Next(-360, 360), Next(-360, 360), Next(-360, 360));
            var offset = new Vector3(Next(-100, 100), Next(-100, 100), Next(-100, 100));
            var matrix = PlacementMath.GroundedLocal(min, max, scale, rotation, offset);
            var center = Vector3.Transform((min + max) / 2, matrix);
            var transformed = CollisionGeometry.Transform(box, matrix, center);
            Check(transformed.Length == box.Length, "triangles preserved");
            for (int i = 0; i < box.Length; i++)
            {
                Near(transformed[i].A + center, Vector3.Transform(box[i].A, matrix));
                Near(transformed[i].B + center, Vector3.Transform(box[i].B, matrix));
                Near(transformed[i].C + center, Vector3.Transform(box[i].C, matrix));
            }
            Matrix4x4.Decompose(matrix, out _, out var orientation, out _);
            // Fitted box pose must agree with the rendered box under independent XYZ scale/rotation.
            Near(Vector3.Transform((box[0].A - (min + max) / 2) * scale, orientation) + center,
                Vector3.Transform(box[0].A, matrix));
            Check(box.SequenceEqual(Box(min, max)), "original geometry not mutated");
        }
        var degenerate = new CollisionTriangle(Vector3.Zero, Vector3.Zero, Vector3.Zero);
        Check(CollisionGeometry.Transform(box.Append(degenerate), Matrix4x4.Identity, Vector3.Zero).Length == 12, "degenerate triangles skipped");
        Reject(() => CollisionGeometry.Transform(new[] { degenerate }, Matrix4x4.Identity, Vector3.Zero));
        Reject(() => CollisionGeometry.Transform(nan, Matrix4x4.Identity, Vector3.Zero));
        Reject(() => CollisionGeometry.Transform(Enumerable.Repeat(box[0], CollisionGeometry.MaxTriangles + 1), Matrix4x4.Identity, Vector3.Zero));
        Console.WriteLine("PASS: Sphinx closed-box detection, openings, duplicated/degenerate geometry, collider budget and 200 collider/render transform cases.");
        float Next(float a, float b) => a + (b-a) * (float)random.NextDouble();
    }
    private static CollisionTriangle[] Box(Vector3 min, Vector3 max)
    {
        Vector3 Corner(int i) => new((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
        var result = new List<CollisionTriangle>();
        foreach (var face in new[] { new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 } })
        {
            result.Add(new(Corner(face[0]), Corner(face[1]), Corner(face[2])));
            result.Add(new(Corner(face[0]), Corner(face[2]), Corner(face[3])));
        }
        return result.ToArray();
    }
    private static void Check(bool success, string message) { if (!success) throw new Exception(message); }
    private static void Near(Vector3 a, Vector3 b) => Check(Vector3.Distance(a, b) < .003f, $"Collider/render mismatch: {a} vs {b}");
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("Invalid collision geometry accepted.");
    }
}
