using System;
using System.Collections.Generic;
using System.Numerics;

namespace MeowSci.PebblesLib;

/// <summary>Game-independent convex-hull math mirroring KSA 5447's ConvexHullColliderTemplate.</summary>
internal static class HullMath
{
    /// <summary>Axis-aligned bounds centre. Stock recentres hull points on it before building, for float precision.</summary>
    public static Vector3 BoundsCentre(ReadOnlySpan<Vector3> points)
    {
        if (points.IsEmpty) throw new ArgumentException("A hull needs points.", nameof(points));
        Vector3 min = points[0], max = points[0];
        foreach (var point in points) { min = Vector3.Min(min, point); max = Vector3.Max(max, point); }
        return 0.5f * (min + max);
    }

    /// <summary>Six times one planar face's signed volume about the origin, fanned from its first vertex.</summary>
    public static double FaceFan(ReadOnlySpan<Vector3> face)
    {
        if (face.Length < 3) return 0;
        double sum = 0;
        var previous = face[1];
        for (var i = 2; i < face.Length; i++)
        {
            sum += Vector3.Dot(face[0], Vector3.Cross(previous, face[i]));
            previous = face[i];
        }
        return sum;
    }

    /// <summary>Volume of a closed, consistently wound polyhedron (stock ComputeHullVolume: |Σ fans| / 6).</summary>
    public static double Volume(IEnumerable<Vector3[]> faces)
    {
        double sum = 0;
        foreach (var face in faces) sum += FaceFan(face);
        return Math.Abs(sum) / 6.0;
    }
}
