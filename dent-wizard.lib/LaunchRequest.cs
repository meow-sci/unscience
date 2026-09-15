using System;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.DentWizardLib;

/// <summary>A click snapshot. Offsets use CCI for vessels, CCF for terrain; never absolute ECL positions.</summary>
internal sealed record LaunchRequest(Vehicle Source, Vehicle? Target, IParentBody Parent,
    double3 CameraOffset, double3 HitOffset, double Speed)
{
    public string Execute(UniverseTime stateTime)
    {
        if (Program.EditorFlag || Source.IsDisposed || Source.IsEditedVehicle
            || !VehicleProvider.GetAllVehicles(true).Contains(Source))
            throw new InvalidOperationException("The source is no longer available in flight.");
        double3 position, direction, velocity;
        if (Target != null)
        {
            if (Target.IsDisposed || Target.IsEditedVehicle || Target == Source || Target.Parent != Parent
                || !VehicleProvider.GetAllVehicles(true).Contains(Target))
                throw new InvalidOperationException("The target disappeared or changed parent body; select it again.");
            // Advect the click snapshot with the target's committed position. This avoids a frame
            // of orbital drift between the rendered click and the next safe physics handoff.
            position = Target.GetPositionCci() + CameraOffset;
            direction = HitOffset - CameraOffset;
            velocity = LaunchMath.Velocity(direction, Speed, Target.GetVelocityCci(),
                double3.Transform(Target.BodyRates, Target.GetBody2Cci()), HitOffset);
        }
        else
        {
            var ccf2Cci = Parent.GetCcf2Cci(stateTime);
            position = double3.Transform(CameraOffset, ccf2Cci);
            var hit = double3.Transform(HitOffset, ccf2Cci);
            direction = hit - position;
            velocity = LaunchMath.Velocity(direction, Speed, double3.Zero, Parent.GetAngularVelocityCci(), hit);
        }
        if (!LaunchMath.IsFinite(position) || position.LengthSquared() <= 0)
            throw new InvalidOperationException("The camera launch position is invalid.");
        var orbit = Orbit.CreateFromStateCci(Parent, stateTime, position, velocity, Source.Orbit.OrbitLineColor);
        var oldPlan = Source.FlightPlan;
        // Null attitude/rates preserve source orientation and spin, including EVA conventions.
        // Teleport also rebuilds the flight plan, moves parent membership and leaves its old bubble.
        Source.Teleport(orbit, null, null);
        if (ReferenceEquals(oldPlan, Source.FlightPlan))
            throw new InvalidOperationException("KSA rejected the launch trajectory; the source was not moved.");
        Source.UpdatePerFrameData();
        return $"Fired {Source.Id} at {Target?.Id ?? Parent.Id}, {Speed:G6} m/s relative to the target.";
    }
}
