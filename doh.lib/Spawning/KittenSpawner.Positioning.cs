using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.DohLib.Spawning;

/// <summary>Spawn position/orientation resolution (vehicle-relative or absolute orbital state).</summary>
public sealed partial class KittenSpawner
{
    private PositionResult ResolvePositioning(SpawnRequest request)
    {
        if (request.ReferenceVehicle != null || !string.IsNullOrEmpty(request.ReferenceVehicleId))
            return ResolveVehicleRelative(request);
        if (request.PositionCci.HasValue && request.VelocityCci.HasValue)
            return ResolveAbsolute(request);

        return new PositionResult { Error = "Either ReferenceVehicleId or PositionCci+VelocityCci required." };
    }

    private PositionResult ResolveVehicleRelative(SpawnRequest request)
    {
        // Prefer the exact vehicle object; an ID is only resolved when unambiguous, so a
        // renamed or duplicate-named craft can never silently become the spawn anchor.
        var refVehicle = request.ReferenceVehicle ?? VehicleProvider.FindVehicle(request.ReferenceVehicleId!);
        if (refVehicle == null)
            return new PositionResult { Error = $"Vehicle '{request.ReferenceVehicleId}' not found (or ambiguous)." };
        if (refVehicle.IsDisposed)
            return new PositionResult { Error = $"Vehicle '{refVehicle.Id}' no longer exists." };

        var sv = refVehicle.Orbit.StateVectors;
        var body2Cci = refVehicle.GetAsmb2Cci();
        var offsetCci = request.OffsetBodyFrame.Transform(body2Cci);

        return new PositionResult
        {
            // Position, velocity and epoch must come from the same state (as EVADoor.CreateKittenEva
            // does); pairing this position with a different clock time shifts the kitten by v*dt.
            StateTime = sv.StateTime,
            BasePositionCci = sv.PositionCci,
            OffsetCci = offsetCci,
            VelocityCci = sv.VelocityCci,
            Body2Cce = refVehicle.Body2Cce,
            BodyRates = SafeBodyRates(refVehicle.BodyRates),
            Parent = refVehicle.Parent,
            ReferenceOrbit = refVehicle.Orbit
        };
    }

    private PositionResult ResolveAbsolute(SpawnRequest request)
    {
        if (string.IsNullOrEmpty(request.ParentBodyName))
            return new PositionResult { Error = "ParentBodyName required for absolute positioning." };

        var celestials = CelestialProvider.GetAllCelestials();
        var parent = celestials.FirstOrDefault(c => c.Id == request.ParentBodyName);
        if (parent == null)
            return new PositionResult { Error = $"Celestial body '{request.ParentBodyName}' not found." };

        // Create a reference orbit from the absolute position
        var simTime = Universe.GetElapsedTime();
        var tempOrbit = Orbit.CreateFromStateCci(
            parent, simTime, request.PositionCci!.Value, request.VelocityCci!.Value, new byte4(255, 200, 0, 255));

        return new PositionResult
        {
            StateTime = simTime,
            BasePositionCci = request.PositionCci!.Value,
            OffsetCci = double3.Zero,
            VelocityCci = request.VelocityCci!.Value,
            Body2Cce = doubleQuat.Identity,
            BodyRates = double3.Zero,
            Parent = parent,
            ReferenceOrbit = tempOrbit
        };
    }

    private static double3 SafeBodyRates(double3 rates)
    {
        if (double.IsNaN(rates.X) || double.IsNaN(rates.Y) || double.IsNaN(rates.Z))
            return double3.Zero;
        return rates;
    }

    private class PositionResult
    {
        public UniverseTime StateTime;
        public double3 BasePositionCci;
        public double3 OffsetCci;
        public double3 VelocityCci;
        public doubleQuat Body2Cce;
        public double3 BodyRates;
        public IParentBody? Parent;
        public Orbit? ReferenceOrbit;
        public string? Error;
    }
}
