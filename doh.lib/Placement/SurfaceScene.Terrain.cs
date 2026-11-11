using System;
using BepuPhysics;
using Brutal.Numerics;
using KSA;

namespace MeowSci.DohLib.Placement;

internal sealed partial class SurfaceScene
{
    private SurfaceHit? PickTerrain(Ray ray, double range)
    {
        if (Camera.NearbyCelestial is not { } body) return null;
        var rotation = body.GetCce2Ccf();
        var origin = (ray.Origin - Camera.GetPositionEgo(body)).Transform(rotation);
        var direction = ray.Direction.Transform(rotation);
        if (!SurfaceRayMarch.TryFind(t => Depth(body, origin + direction * t), range, out double distance)) return null;
        var point = origin + direction * distance;
        var normal = TerrainNormal(body, point).Transform(body.GetCcf2Cce());
        return new(ray.Origin + ray.Direction * distance, normal, distance, null, null, body);
    }

    internal static double Depth(Celestial body, double3 point)
    {
        double radius = point.Length();
        return radius > 0 && double.IsFinite(radius)
            ? radius - body.MeanRadius - body.GetTerrainHeightFromDirCcf(point / radius, accurate: true)
            : double.NaN;
    }

    private static double3 Surface(Celestial body, double3 point)
    {
        var direction = point.NormalizeOrZero();
        return direction * (body.MeanRadius + body.GetTerrainHeightFromDirCcf(direction, accurate: true));
    }

    internal static double3 TerrainNormal(Celestial body, double3 point)
    {
        var radial = point.NormalizeOrZero();
        var east = double3.Cross(Math.Abs(radial.Z) < .9 ? double3.UnitZ : double3.UnitY, radial).NormalizeOrZero();
        var north = double3.Cross(radial, east);
        var a = Surface(body, point + east * .25) - Surface(body, point - east * .25);
        var b = Surface(body, point + north * .25) - Surface(body, point - north * .25);
        var normal = double3.Cross(a, b).NormalizeOrZero();
        if (!SurfaceCapsule.Finite(normal) || normal.LengthSquared() < .5)
            throw new InvalidOperationException("The terrain has no usable surface normal.");
        return double3.Dot(normal, radial) < 0 ? -normal : normal;
    }

    private SurfaceHit? PickStatics(Ray ray, double range)
    {
        SurfaceHit? best = null;
        foreach (var (sim, anchor) in _simulations)
        {
            if (anchor.Parent is not Celestial body) continue;
            var handler = new StaticRayHit { Distance = (float)range };
            var rotation = anchor.BubbleOrigin.GetBub2Cce().Inverse();
            sim.Simulation.RayCast(SurfaceCapsule.Small(ToPhys(anchor, ray.Origin)),
                SurfaceCapsule.Small(ray.Direction.Transform(rotation)), (float)range, sim.BufferPool, ref handler);
            if (!handler.Found) continue;
            var normal = SurfaceCapsule.Double(handler.Normal).Transform(anchor.BubbleOrigin.GetBub2Cce()).NormalizeOrZero();
            if (!SurfaceCapsule.Finite(normal) || normal.LengthSquared() < .5) continue;
            if (double3.Dot(normal, ray.Direction) > 0) normal = -normal;
            range = handler.Distance;
            best = new(ray.Origin + ray.Direction * range, normal, range, null, null, body);
        }
        return best;
    }
}
