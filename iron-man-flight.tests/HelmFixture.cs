using System;
using System.Runtime.CompilerServices;
using Brutal.Numerics;

namespace KSA;

// Mirrors of the KSA 2026.10.19.5638 helm / thrust-frame chain; line numbers are under decomp/KSA/.

/// <summary>FlightComputerNavigation.cs:19; built from ReadOnlyVehicle.Helm2Body at PhysicsBubble.cs:1372,1508,1888.</summary>
public struct FlightComputerNavigation
{
    public required doubleQuat Helm2Body;
    public required double Time;
}

public partial class FlightComputer
{
    public double? LastControlTime;                     // :109 (UniverseTime?)
    public doubleQuat Ctrl2Asmb = doubleQuat.Identity;  // :131
    public doubleQuat Helm2Asmb = doubleQuat.Identity;  // :133
    public doubleQuat Tnm2Asmb = doubleQuat.Identity;   // :135
    /// <summary>Fixture input for GetDesiredControlFrame() == Tnm (:1433-1452).</summary>
    public bool IsTnmActive;
    /// <summary>Fixture input for TvcAllocation.ComputeNetThrustAsmb (:1511).</summary>
    public double3 NetThrustAsmb;

    /// <summary>:265-317, frame/timing members only. Like the game, never copies Helm2Asmb or Ctrl2Asmb.</summary>
    public void CopyFrom(FlightComputer existing)
    {
        Tnm2Asmb = existing.Tnm2Asmb;
        LastControlTime = existing.LastControlTime;
    }

    /// <summary>The frame chain of ComputeControl (:501-530).</summary>
    public void ComputeControl(ref readonly FlightComputerNavigation nav)
    {
        Helm2Asmb = nav.Helm2Body;
        Ctrl2Asmb = Helm2Asmb;
        Tnm2Asmb = Helm2Asmb;
        LastControlTime = nav.Time;
        UpdateTnmFrame();
        Ctrl2Asmb = IsTnmActive ? Tnm2Asmb : Helm2Asmb;
    }

    // :1494-1516: helm rotated minimally so +X follows the trimmed net thrust.
    private void UpdateTnmFrame()
    {
        double3 vector = NetThrustAsmb.NormalizeOrZero();
        if (vector == double3.Zero)
            Tnm2Asmb = Helm2Asmb;
        else
            Tnm2Asmb = doubleQuat.Concatenate(Helm2Asmb,
                QuaternionEx.FromToRotation(double3.UnitX.Transform(Helm2Asmb), vector));
    }
}

public static class QuaternionEx
{
    /// <summary>QuaternionEx.cs:231-259; the antiparallel fallback is not exercised.</summary>
    public static doubleQuat FromToRotation(double3 fromDirection, double3 toDirection)
    {
        double num = double3.Dot(double3.Normalize(fromDirection), double3.Normalize(toDirection));
        if (num > 0.999999) return doubleQuat.Identity;
        if (num < -0.999999) throw new NotSupportedException("fixture omits the antiparallel fallback");
        double3 axis = double3.Cross(fromDirection, toDirection);
        return doubleQuat.Normalize(new doubleQuat(axis.X, axis.Y, axis.Z, 1.0 + num));
    }
}

/// <summary>NavBallMarkerData.cs:5-35, thrust members only.</summary>
public struct NavBallMarkerData
{
    public double3 ThrustDirHelm = double3.Zero;
    public bool ThrustValid = false;
    public NavBallMarkerData() { }
}

public partial class Vehicle
{
    private NavBallMarkerData _navBallMarkerData;                                       // :261
    public ref readonly NavBallMarkerData NavBallMarkerData => ref _navBallMarkerData;  // :641
    public FlightComputer FlightComputer { get; } = new();                              // :496

    /// <summary>Main-thread result application: CopyFrom (:2598), then the per-frame markers (:2739).</summary>
    public void ApplyWorkerResults(FlightComputer newFlightComputer)
    {
        FlightComputer.CopyFrom(newFlightComputer);
        UpdateNavBallMarkers();
    }

    /// <summary>Thrust-marker tail of :2846-2944, verbatim (code size 0x33e, so never inlined).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void UpdateNavBallMarkers()
    {
        double? lastControlTime = FlightComputer.LastControlTime;
        if (lastControlTime.HasValue)
        {
            double3 value2 = double3.UnitX.Transform(FlightComputer.Tnm2Asmb);
            _navBallMarkerData.ThrustDirHelm = value2.Transform(FlightComputer.Helm2Asmb.Inverse()).NormalizeOrZero();
            _navBallMarkerData.ThrustValid = true;
        }
        else
        {
            _navBallMarkerData.ThrustDirHelm = double3.Zero;
            _navBallMarkerData.ThrustValid = false;
        }
    }
}
