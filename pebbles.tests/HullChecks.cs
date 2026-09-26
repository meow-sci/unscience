using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MeowSci.PebblesLib;

/// <summary>Volume and recentring math ported from KSA 5447 ConvexHullColliderTemplate for Pebbles' private hulls.</summary>
internal static class HullChecks
{
    public static void Run()
    {
        Near(HullMath.Volume(Box(Vector3.Zero, Vector3.One)), 1, "unit cube volume");
        Near(HullMath.Volume(Box(new(-1, -1.5f, -2), new(1, 1.5f, 2))), 24, "2x3x4 box volume");
        Near(HullMath.Volume(Box(new(40, -30, 25), new(41, -29, 26))), 1, "closed hull volume is independent of the origin");
        Near(HullMath.Volume(Box(Vector3.Zero, Vector3.One).Select(f => f.Reverse().ToArray())), 1, "inward winding still yields a positive volume");
        var tetra = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
        Near(HullMath.Volume([[tetra[0], tetra[2], tetra[1]], [tetra[0], tetra[1], tetra[3]], [tetra[0], tetra[3], tetra[2]], [tetra[1], tetra[2], tetra[3]]]), 1.0 / 6, "tetrahedron volume");
        const int sides = 8; const float radius = 2, height = 3;
        var bottom = Enumerable.Range(0, sides).Select(i => new Vector3(radius * MathF.Cos(i * MathF.Tau / sides), 0, radius * MathF.Sin(i * MathF.Tau / sides))).ToArray();
        var top = bottom.Select(p => p + new Vector3(0, height, 0)).ToArray();
        var prism = new List<Vector3[]> { bottom, top.Reverse().ToArray() };
        for (var i = 0; i < sides; i++) prism.Add([bottom[i], top[i], top[(i + 1) % sides], bottom[(i + 1) % sides]]);
        Near(HullMath.Volume(prism), 0.5 * sides * radius * radius * Math.Sin(2 * Math.PI / sides) * height, "polygon faces are fanned, not truncated to triangles");
        Check(HullMath.FaceFan([Vector3.One, Vector3.UnitX]) == 0, "degenerate faces contribute nothing");
        Check(HullMath.BoundsCentre([new(-1, 2, 10), new(3, 4, 12), new(1, 3, 11)]) == new Vector3(1, 3, 11), "recentring uses the axis-aligned bounds centre");
        Check(Throws(() => HullMath.BoundsCentre(ReadOnlySpan<Vector3>.Empty)), "an empty hull is rejected");
        Console.WriteLine("PASS: Pebbles hull volume (box, offset, winding, tetrahedron, polygon fan) and bounds recentring.");
    }

    /// <summary>Axis-aligned box as six outward-wound quads.</summary>
    private static Vector3[][] Box(Vector3 min, Vector3 max)
    {
        Vector3 P(int x, int y, int z) => new(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
        return
        [
            [P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0)], [P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1)],
            [P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1)], [P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0)],
            [P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0)], [P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1)]
        ];
    }
    private static void Near(double actual, double expected, string message)
    { if (Math.Abs(actual - expected) > 1e-4 * Math.Max(1, Math.Abs(expected))) throw new Exception($"{message}: expected {expected}, got {actual}"); }
    private static bool Throws(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
