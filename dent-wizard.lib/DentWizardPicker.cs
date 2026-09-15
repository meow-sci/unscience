using System;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.DentWizardLib;

/// <summary>Graffiti-style world picking without taking a dependency on its renderer or decal state.</summary>
internal static class DentWizardPicker
{
    internal const double Range = 10000;

    public static LaunchRequest? Pick(Vehicle source, double speed)
    {
        var camera = Program.GetMainCamera();
        if (camera == null) return null;
        var ray = Cursor.GetEgoRay(Program.MainViewport);
        if (!LaunchMath.IsFinite(ray.Direction) || !LaunchMath.IsFinite(ray.Origin)
            || ray.Direction.LengthSquared() <= 0) return null;

        double best = Range;
        Vehicle? target = null;
        foreach (var vehicle in VehicleProvider.GetAllVehicles(includeDebris: true))
        {
            if (vehicle == source || vehicle.IsDisposed || vehicle.IsEditedVehicle) continue;
            var matrix = vehicle.GetMatrixAsmb2Ego(camera);
            if (vehicle is KittenEva)
            {
                // EVA avatars have no ordinary part mesh. Mirror stock/Graffiti sphere picking.
                var root = vehicle.Parts.Root;
                if (root == null) continue;
                double radius = vehicle.BoundingSphereRadiusBody * Double3Ex.GetAbsoluteLargestElement(root.ScaleTotal);
                if (!double.IsFinite(radius) || radius <= 0) continue;
                if (ray.Raycast(new BoundingSphere3D(root.PositionEgo(in matrix), radius), out var distance, out _)
                    && distance > 0 && distance < best)
                {
                    best = distance;
                    target = vehicle;
                }
                continue;
            }
            foreach (var part in vehicle.Parts.Parts)
            {
                if (part.RayCastEgo(in matrix, ray, out var distance, out _, out _, out _, out _, out _, out _, out _)
                    && distance > 0 && distance < best)
                {
                    best = distance;
                    target = vehicle;
                }
            }
        }

        // Terrain competes by distance: a hull behind a hill must not win the click.
        if (camera.NearbyCelestial is { } body && TryTerrain(camera, ray, body, best, out var hitCcf))
        {
            var cameraCcf = (-camera.GetPositionEgo(body)).Transform(body.GetCce2Ccf());
            return new LaunchRequest(source, null, body, cameraCcf, hitCcf, speed);
        }
        if (target == null) return null;
        var cce2Cci = target.Parent.GetCce2Cci();
        var targetEgo = camera.GetPositionEgo(target);
        var cameraOffset = (-targetEgo).Transform(cce2Cci);
        var hitOffset = (ray.Origin + ray.Direction * best - targetEgo).Transform(cce2Cci);
        return new LaunchRequest(source, target, target.Parent, cameraOffset, hitOffset, speed);
    }

    private static bool TryTerrain(Camera camera, Ray ray, Celestial body, double range, out double3 hit)
    {
        hit = default;
        var cce2Ccf = body.GetCce2Ccf();
        var origin = (ray.Origin - camera.GetPositionEgo(body)).Transform(cce2Ccf);
        var direction = ray.Direction.Transform(cce2Ccf);
        if (!LaunchMath.IsFinite(origin) || !LaunchMath.IsFinite(direction) || !(Depth(body, origin) > 0))
            return false;
        double above = 0;
        // Same CPU heightfield/bisection approach as Graffiti. Fine near-camera steps avoid
        // skipping small nearby hills when the far end of the ray is kilometres away.
        for (int i = 1; i <= 128; i++)
        {
            double fraction = i / 128.0;
            double below = range * fraction * fraction;
            if (Depth(body, origin + direction * below) <= 0)
            {
                for (int j = 0; j < 24; j++)
                {
                    double middle = (above + below) * 0.5;
                    if (Depth(body, origin + direction * middle) <= 0) below = middle;
                    else above = middle;
                }
                hit = origin + direction * below;
                return true;
            }
            above = below;
        }
        return false;
    }

    private static double Depth(Celestial body, double3 point)
    {
        double radius = point.Length();
        return radius > 0 && double.IsFinite(radius)
            ? radius - body.MeanRadius - body.GetTerrainHeightFromDirCcf(point / radius, accurate: true)
            : double.NaN;
    }
}
