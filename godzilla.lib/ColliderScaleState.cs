using System;
using Brutal.Numerics;
using KSA;

namespace MeowSci.GodzillaLib;

/// <summary>Describes a collision-only view of a vessel without changing its parts or modules.</summary>
internal sealed class ColliderScaleState
{
    private readonly VesselScaleSnapshot _snapshot;
    private readonly bool _scaleColliders;
    private readonly float3 _factor;
    private readonly double _largestAxis;
    public Vehicle Vehicle => _snapshot.Vehicle;
    public ColliderModule[] Modules { get; }

    public ColliderScaleState(VesselScaleSnapshot snapshot, bool scaleColliders, float3 factor)
    {
        _snapshot = snapshot;
        _scaleColliders = scaleColliders;
        _factor = factor;
        _largestAxis = Math.Max(factor.X, Math.Max(factor.Y, factor.Z));
        Modules = Vehicle.Parts.Modules.Get<ColliderModule>().ToArray();
    }

    public ScaleFactors GetScale(ColliderModule collider)
    {
        var original = _scaleColliders ? collider.Parent.ScaleTotal : _snapshot.OriginalScaleTotal(collider.Parent);
        double scale = new ScaleFactors(original).Scale * (_scaleColliders ? _largestAxis : 1);
        if (!double.IsFinite(scale) || scale <= 0 || scale > float.MaxValue)
            throw new InvalidOperationException("The requested collider scale exceeds the collision engine's range.");
        return new ScaleFactors(scale);
    }

    public double3 GetPosition(ColliderModule collider)
    {
        if (!_scaleColliders)
            return collider.PositionPartAsmb.Transform(collider.Parent.Asmb2VehicleAsmb) +
                _snapshot.OriginalPartMatrix(collider.Parent).Translation;

        // Shapes use the game's largest-axis approximation; centers follow the visual XYZ transform.
        // PositionPartAsmb already contains that shape multiplier, so remove it before scaling the
        // original center around the physical COM. Never scale a previously scaled center again.
        var position = (collider.PositionPartAsmb / _largestAxis).Transform(collider.Parent.Asmb2VehicleAsmb) +
            collider.Parent.PositionVehicleAsmb;
        var pivot = Vehicle.CenterOfMassAsmb;
        var offset = position - pivot;
        return pivot + new double3(offset.X * _factor.X, offset.Y * _factor.Y, offset.Z * _factor.Z);
    }

    public void RefreshShapes()
    {
        // Preflight all scales before replacing any owned native shape.
        foreach (var collider in Modules) _ = GetScale(collider);
        foreach (var collider in Modules)
        {
            var scale = GetScale(collider);
            collider.SetScale(in scale);
            collider.NeedsColliderUpdate = true;
        }
    }
}
