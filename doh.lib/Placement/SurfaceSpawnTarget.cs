using System;
using System.Numerics;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.DohLib.Placement;

internal sealed class SurfaceSpawnTarget
{
    private readonly CelestialSystem _world;
    private readonly SurfaceScene _scene;
    private readonly SurfaceHit _hit;
    private readonly SavedPartReference? _partReference;
    private readonly double3 _referencePositionCci;
    private readonly double3 _referenceVelocityCci;
    private readonly double3 _angularVelocityCci;
    private double3 _previewPositionLocal;
    private doubleQuat _previewRotationLocal;
    public IParentBody Parent { get; }
    public UniverseTime Time { get; }
    public double3 HitCci { get; }
    public doubleQuat Body2Cce { get; }
    public doubleQuat Body2Cci { get; }
    public double3 BodyRates => _angularVelocityCci.Transform(Body2Cci.Inverse());
    public SurfaceCapsule Capsule { get; private set; }
    public double3 CentreEgo { get; private set; }
    public Quaternion CapsuleRotation { get; private set; }
    public string Description => _hit.Vehicle?.Id ?? _hit.Body?.Id ?? "surface";

    public SurfaceSpawnTarget(SurfaceScene scene, SurfaceHit hit, SurfaceCapsule capsule, double3 heading)
    {
        _world = Universe.CurrentSystem ?? throw new InvalidOperationException("No current scene.");
        _scene = scene; _hit = hit;
        Body2Cce = SurfaceCapsule.Double(CapsulePlacementMath.BodyOrientation(
            SurfaceCapsule.Small(hit.NormalEgo), SurfaceCapsule.Small(heading)));
        if (hit.Vehicle is { } vehicle && hit.Part is { } part)
        {
            _partReference = SavedPartReference.Capture(vehicle, part);
            var states = vehicle.GetPhysicsStates();
            states.GetStatesCci(out _referencePositionCci, out _referenceVelocityCci, out var rotation);
            Parent = vehicle.Parent; Time = states.Time;
            _angularVelocityCci = states.GetBodyRates().Transform(rotation);
            HitCci = _referencePositionCci
                + (hit.PointEgo - scene.Camera.GetPositionEgo(vehicle)).Transform(Parent.GetCce2Cci());
        }
        else if (hit.Body is { } body)
        {
            Parent = body; Time = Universe.GetElapsedTime();
            var pointCcf = (hit.PointEgo - scene.Camera.GetPositionEgo(body)).Transform(body.GetCce2Ccf());
            VehicleReferenceFrameEx.CcfStatesToCci(body, Time, pointCcf, double3.Zero,
                out _referencePositionCci, out _referenceVelocityCci);
            HitCci = _referencePositionCci;
            _angularVelocityCci = Parent.GetAngularVelocityCci();
        }
        else throw new InvalidOperationException("The hit has no physical owner.");
        Body2Cci = Body2Cce.Concatenate(Parent.GetCce2Cci()).Normalized();
        SetCapsule(capsule);
    }

    public bool IsClear(KittenEva? kitten = null)
    {
        ValidateIdentity();
        if (kitten != null) SetCapsule(SurfaceCapsule.FromKitten(kitten));
        return _scene.IsClear(Capsule, CentreEgo, CapsuleRotation, Parent as Celestial, kitten);
    }

    private void SetCapsule(SurfaceCapsule capsule)
    {
        Capsule = capsule;
        CapsuleRotation = SurfaceCapsule.Small(capsule.RotationAsmb.Concatenate(Body2Cce));
        CentreEgo = _hit.PointEgo + SurfaceCapsule.Double(CapsulePlacementMath.CentreOffset(
            capsule.Shape, CapsuleRotation, SurfaceCapsule.Small(_hit.NormalEgo)));
        var anchor = _hit.Vehicle;
        var frame = anchor?.Asmb2Ego ?? _hit.Body!.GetCcf2Cce();
        var origin = anchor != null ? _scene.Camera.GetPositionEgo(anchor) : _scene.Camera.GetPositionEgo(_hit.Body!);
        _previewPositionLocal = (CentreEgo - origin).Transform(frame.Inverse());
        _previewRotationLocal = SurfaceCapsule.Double(CapsuleRotation).Concatenate(frame.Inverse());
    }

    public void PreviewPose(Camera camera, out double3 centre, out doubleQuat rotation)
    {
        var anchor = _hit.Vehicle;
        var frame = anchor?.Asmb2Ego ?? _hit.Body!.GetCcf2Cce();
        var origin = anchor != null ? camera.GetPositionEgo(anchor) : camera.GetPositionEgo(_hit.Body!);
        centre = origin + _previewPositionLocal.Transform(frame);
        rotation = _previewRotationLocal.Concatenate(frame);
    }

    public double3 ComPosition(KittenEva kitten)
    {
        var centreOffset = CentreEgo - _hit.PointEgo;
        var offsetEgo = centreOffset - (Capsule.CentreAsmb - kitten.CenterOfMassAsmb).Transform(Body2Cce);
        return HitCci + offsetEgo.Transform(Parent.GetCce2Cci());
    }

    public double3 Velocity(double3 comCci) =>
        _referenceVelocityCci + double3.Cross(_angularVelocityCci, comCci - _referencePositionCci);

    public Orbit InitialOrbit()
    {
        var position = HitCci + (CentreEgo - _hit.PointEgo).Transform(Parent.GetCce2Cci());
        return Orbit.CreateFromStateCci(Parent, Time, position, Velocity(position), new byte4(255, 200, 0, 255));
    }

    private void ValidateIdentity()
    {
        if (!ReferenceEquals(_world, Universe.CurrentSystem)) throw new InvalidOperationException("The scene changed; placement was cancelled.");
        if (_hit.Vehicle is { } vehicle && (vehicle.IsDisposed
            || !ReferenceEquals(VehicleProvider.FindVehicle(vehicle.Id), vehicle)
            || !ReferenceEquals(_partReference?.Resolve(), _hit.Part)))
            throw new InvalidOperationException("The selected part no longer exists or changed ownership.");
        if (_hit.Body is { } body && (!_world.All.TryGet(body.Id, out Astronomical? current) || !ReferenceEquals(current, body)))
            throw new InvalidOperationException("The selected celestial body no longer exists.");
        if (!SurfaceCapsule.Finite(HitCci) || !SurfaceCapsule.Finite(_referenceVelocityCci)
            || !SurfaceCapsule.Finite(_angularVelocityCci)) throw new InvalidOperationException("The target has invalid motion data.");
    }
}
