using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Brutal.Numerics;

namespace KSA;

public readonly record struct UniverseTime(double Seconds);
public class Celestial
{
    public doubleQuat PlacementRotation = doubleQuat.CreateFromAxisAngle(double3.UnitY, 0.7);
    public double3 WorldAngularVelocity = new(0.01, 0.02, -0.03);
}
public class Orbit { public double SurfaceClearance; }

public partial class Vehicle
{
    public struct InitialKinematicState
    {
        public Orbit Orbit;
        public doubleQuat Body2Cce;
        public double3 BodyRates;
    }

    public sealed record PlacementCall(Celestial Celestial, UniverseTime Time, double Latitude, double Longitude,
        double3 Min, double3 Max, double3 Center, byte4 Color, InitialKinematicState Result);

    public static readonly List<PlacementCall> PlacementCalls = new();
    public double3 TeleportBoundsMin = new(-2, -3, -5);
    public double3 TeleportBoundsMax = new(7, 11, 13);
    public double3 TeleportCenter = double3.Zero;
    public UniverseTime TeleportTime = new(1234.5);
    public byte4 TeleportColor = new(11, 22, 33, 44);
    public doubleQuat CurrentTeleportRotation = doubleQuat.Identity;
    public double3 CurrentTeleportRates;
    public Orbit? CurrentTeleportOrbit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static InitialKinematicState GetInitialKinematicStateForLocation(Celestial celestial, UniverseTime time,
        double latitude, double longitude, double3 boundsMinAsmb, double3 boundsMaxAsmb,
        double3 centerMassAsmb, byte4 orbitLineColor)
    {
        // Deterministic native-service output. Terrain/orbit integration is intentionally not simulated.
        var state = new InitialKinematicState
        {
            Orbit = new Orbit { SurfaceClearance = centerMassAsmb.X - boundsMinAsmb.X },
            Body2Cce = celestial.PlacementRotation,
            BodyRates = celestial.WorldAngularVelocity.Transform(celestial.PlacementRotation.Inverse())
        };
        PlacementCalls.Add(new(celestial, time, latitude, longitude, boundsMinAsmb, boundsMaxAsmb,
            centerMassAsmb, orbitLineColor, state));
        return state;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void TeleportToLocation(Celestial celestial, double lat, double lon)
    {
        InitialKinematicState state = GetInitialKinematicStateForLocation(celestial, TeleportTime,
            lat, lon, TeleportBoundsMin, TeleportBoundsMax, TeleportCenter, TeleportColor);
        InputEvents.TeleportInputBuffer.Add(new()
        {
            Vehicle = this,
            Orbit = state.Orbit,
            Body2Cce = state.Body2Cce,
            BodyRates = state.BodyRates
        });
    }

    public void OtherTeleport(Orbit orbit, doubleQuat rotation, double3 rates) =>
        InputEvents.TeleportInputBuffer.Add(new() { Vehicle = this, Orbit = orbit, Body2Cce = rotation, BodyRates = rates });
}

public static partial class InputEvents
{
    public struct TeleportInputData
    {
        public Vehicle Vehicle;
        public Orbit Orbit;
        public doubleQuat Body2Cce;
        public double3 BodyRates;
        public void Apply()
        {
            Vehicle.CurrentTeleportOrbit = Orbit;
            Vehicle.CurrentTeleportRotation = Body2Cce;
            Vehicle.CurrentTeleportRates = BodyRates;
        }
    }
    public static readonly List<TeleportInputData> TeleportInputBuffer = new();
}
