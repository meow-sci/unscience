using System;
using System.Collections.Generic;
using System.Linq;
using BepuPhysics.Collidables;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.DohLib.Placement;

/// <summary>A synchronous view of the world, used exclusively at PhysicsFrameHook's handoff.</summary>
internal sealed partial class SurfaceScene
{
    public const double Range = 2000;
    public Camera Camera { get; }
    public Vehicle[] Vehicles { get; }
    public IReadOnlyDictionary<ConstraintSim, Vehicle> Simulations => _simulations;
    private readonly Dictionary<ConstraintSim, Vehicle> _simulations = new();

    public SurfaceScene(Camera camera)
    {
        Camera = camera;
        Vehicles = VehicleProvider.GetAllVehicles(includeDebris: true).Where(v => !v.IsDisposed).ToArray();
        foreach (var vehicle in Vehicles)
            if (vehicle.TryGetClutterStatics(out _, out var sim) && sim != null)
                _simulations.TryAdd(sim, vehicle);
    }

    public SurfaceHit? Pick(Ray ray)
    {
        if (!SurfaceCapsule.Finite(ray.Origin) || !SurfaceCapsule.Finite(ray.Direction)
            || Math.Abs(ray.Direction.LengthSquared() - 1) > .001)
            throw new InvalidOperationException("Invalid viewport ray.");
        SurfaceHit? best = null;
        double distance = Range;
        foreach (var vehicle in Vehicles)
        {
            var matrix = vehicle.GetMatrixAsmb2Ego(Camera);
            foreach (var collider in vehicle.Parts.Modules.Get<ColliderModule>())
            {
                if (!collider.RayCastEgo(in matrix, ray, out double t, out var normalAsmb, out _)
                    || t <= 0 || t >= distance) continue;
                var normal = normalAsmb.Transform(vehicle.Asmb2Ego).NormalizeOrZero();
                if (!SurfaceCapsule.Finite(normal) || normal.LengthSquared() < .5) continue;
                if (double3.Dot(normal, ray.Direction) > 0) normal = -normal;
                distance = t;
                best = new(ray.Origin + ray.Direction * t, normal, t, vehicle, collider.Parent, null);
            }
            // KSA uses its geometry box for vehicles without authored collider modules.
            if (vehicle.Parts.Modules.Get<ColliderModule>().Length == 0)
            {
                var states = vehicle.GetPhysicsStates();
                var centre = Camera.GetPositionEgo(vehicle) + double3.Unpack(states.Props.MassToGeometryAsmb).Transform(vehicle.Asmb2Ego);
                var inverse = vehicle.Asmb2Ego.Inverse();
                if (states.Props.BoundingBoxAsmb.RayTest(BepuPhysics.RigidPose.Identity,
                    SurfaceCapsule.Small((ray.Origin - centre).Transform(inverse)),
                    SurfaceCapsule.Small(ray.Direction.Transform(inverse)), out float t, out var n)
                    && t > 0 && t < distance)
                {
                    distance = t;
                    var normal = SurfaceCapsule.Double(n).Transform(vehicle.Asmb2Ego);
                    if (double3.Dot(normal, ray.Direction) > 0) normal = -normal;
                    best = new(ray.Origin + ray.Direction * t, normal,
                        t, vehicle, vehicle.Parts.Root, null);
                }
            }
        }
        var terrain = PickTerrain(ray, distance);
        if (terrain != null) { best = terrain; distance = terrain.Distance; }
        var statics = PickStatics(ray, distance);
        return statics ?? best;
    }

    public double3 ToPhys(Vehicle anchor, double3 ego) =>
        (ego - Camera.GetPositionEgo(anchor)).Transform(anchor.BubbleOrigin.GetBub2Cce().Inverse())
        + anchor.KinematicStates.PositionPhys;

    public double3 FromPhys(Vehicle anchor, double3 phys) =>
        Camera.GetPositionEgo(anchor) + (phys - anchor.KinematicStates.PositionPhys).Transform(anchor.BubbleOrigin.GetBub2Cce());
}

internal sealed record SurfaceHit(double3 PointEgo, double3 NormalEgo, double Distance,
    Vehicle? Vehicle, Part? Part, Celestial? Body);
