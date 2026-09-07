using System;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.SphinxLib;

internal readonly record struct GroundAnchor(Celestial Body, double3 PositionCcf, double3 NormalCcf);

internal static class GroundPlacement
{
    public static GroundAnchor At(Celestial body, double3 nearCcf)
    {
        var radial = nearCcf.NormalizeOrZero();
        if (!Finite(radial) || radial.LengthSquared() < .5) throw new InvalidOperationException("No valid surface direction.");
        double3 point = Surface(body, radial);
        var east = East(radial);
        var north = double3.Cross(radial, east).NormalizeOrZero();
        // One metre samples capture local terrain slope using the current accurate terrain chain.
        var a = Surface(body, (point + east).NormalizeOrZero()) - Surface(body, (point - east).NormalizeOrZero());
        var b = Surface(body, (point + north).NormalizeOrZero()) - Surface(body, (point - north).NormalizeOrZero());
        var normal = double3.Cross(a, b).NormalizeOrZero();
        if (double3.Dot(normal, radial) < 0) normal = -normal;
        if (!Finite(normal) || normal.LengthSquared() < .5) normal = radial;
        return new(body, point, normal);
    }

    public static GroundAnchor BesideControlled(double clearance)
    {
        var vessel = VehicleProvider.GetControlledVehicle() ?? throw new InvalidOperationException("Control a vessel first.");
        if (vessel.Parent is not Celestial body) throw new InvalidOperationException("The vessel must be near a celestial surface.");
        var camera = Program.GetMainCamera();
        var ccf = (camera.GetPositionEgo(vessel) - camera.GetPositionEgo(body)).Transform(body.GetCce2Ccf());
        return At(body, ccf + East(ccf.NormalizeOrZero()) * clearance);
    }

    public static bool TryCursor(double range, out GroundAnchor anchor)
    {
        anchor = default;
        var camera = Program.GetMainCamera();
        if (camera?.NearbyCelestial is not { } body) return false;
        var ray = Cursor.GetEgoRay(Program.MainViewport);
        var rotation = body.GetCce2Ccf();
        var origin = (ray.Origin - camera.GetPositionEgo(body)).Transform(rotation);
        var direction = ray.Direction.Transform(rotation).NormalizeOrZero();
        if (!Finite(origin) || !Finite(direction) || direction.LengthSquared() < .5 || Depth(body, origin) <= 0) return false;
        double above = 0, below = double.NaN;
        for (int i = 1; i <= 128; i++)
        {
            double t = range * i / 128;
            if (Depth(body, origin + direction * t) <= 0) { below = t; break; }
            above = t;
        }
        if (!double.IsFinite(below)) return false;
        for (int i = 0; i < 24; i++)
        {
            double mid = (above + below) * .5;
            if (Depth(body, origin + direction * mid) <= 0) below = mid; else above = mid;
        }
        anchor = At(body, origin + direction * below);
        return true;
    }

    public static double4x4 Frame(GroundAnchor anchor, bool align, Camera camera)
    {
        var up = align ? anchor.NormalCcf : anchor.PositionCcf.NormalizeOrZero();
        var x = East(anchor.PositionCcf.NormalizeOrZero());
        x = (x - up * double3.Dot(x, up)).NormalizeOrZero();
        var z = double3.Cross(x, up).NormalizeOrZero();
        var toEgo = anchor.Body.GetCcf2Cce();
        x = x.Transform(toEgo); up = up.Transform(toEgo); z = z.Transform(toEgo);
        var p = camera.GetPositionEgo(anchor.Body) + anchor.PositionCcf.Transform(toEgo);
        return new double4x4(x.X,x.Y,x.Z,0, up.X,up.Y,up.Z,0, z.X,z.Y,z.Z,0, p.X,p.Y,p.Z,1);
    }

    private static double3 East(double3 radial)
    {
        var east = double3.Cross(double3.UnitZ, radial).NormalizeOrZero();
        return east.LengthSquared() > .5 ? east : double3.Cross(double3.UnitY, radial).NormalizeOrZero();
    }
    private static double3 Surface(Celestial body, double3 direction)
    {
        double radius = body.MeanRadius + body.GetTerrainHeightFromDirCcf(direction, accurate: true);
        if (!double.IsFinite(radius) || radius <= 0) throw new InvalidOperationException("Surface height is unavailable on this body.");
        return direction * radius;
    }
    private static double Depth(Celestial body, double3 p) => p.Length() - Surface(body, p.NormalizeOrZero()).Length();
    private static bool Finite(double3 p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);
}
