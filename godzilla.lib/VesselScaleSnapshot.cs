using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.GarrysTorchLib;
using MeowSci.KsaAbstractions;

namespace MeowSci.GodzillaLib;

/// <summary>Captured once: edits always derive from the original, never the last applied scale.</summary>
internal sealed class VesselScaleSnapshot
{
    private sealed record Original(Part Part, double3 Scale, double3 Position, bool FullPart);
    private readonly List<Original> _originals = new();
    private readonly double3 _pivot;
    private readonly CharacterAvatar? _avatar;
    private readonly float _avatarScale;
    private bool _basic;
    public Vehicle Vehicle { get; }

    public VesselScaleSnapshot(Vehicle vehicle)
    {
        Vehicle = vehicle;
        _pivot = vehicle.CenterOfMassAsmb;
        foreach (var part in vehicle.Parts.Parts) Capture(part, true);
        if (vehicle is KittenEva kitten)
        {
            _avatar = ReflectionHelpers.GetFieldValue(kitten.Renderable, "_characterAvatar") as CharacterAvatar
                ?? throw new InvalidOperationException("The kitten's character is not ready. Try again after it has spawned.");
            _avatarScale = _avatar.Core.Scale;
        }
    }

    private void Capture(Part part, bool full)
    {
        _originals.Add(new(part, part.Scale, part.PositionParentAsmb, full));
        foreach (var child in part.SubParts) Capture(child, false);
    }

    private HashSet<Part> CurrentParts()
    {
        var parts = new HashSet<Part>();
        void Add(Part part)
        {
            parts.Add(part);
            foreach (var child in part.SubParts) Add(child);
        }
        foreach (var part in Vehicle.Parts.Parts) Add(part);
        return parts;
    }

    public bool TopologyMatches() => CurrentParts().SetEquals(_originals.Select(o => o.Part));

    public void Apply(bool smart, float3 factor)
    {
        if (!WeldScale.IsValid(factor)) throw new ArgumentOutOfRangeException(nameof(factor), "Scale must be finite and between 0.05 and 20.");
        if (!TopologyMatches()) throw new InvalidOperationException("The vessel's parts changed. Restore before scaling again.");
        // Undo Basic's child scales on a mode switch, but do not reset animation-owned child
        // transforms on repeated Smart edits or Smart restore.
        foreach (var original in _originals)
        {
            if (original.FullPart)
            {
                original.Part.PositionParentAsmb = smart
                    ? _pivot + (original.Position - _pivot) * factor.X : original.Position;
                original.Part.Scale = smart ? original.Scale * factor.X : new double3(factor.X, factor.Y, factor.Z);
            }
            else if (!smart)
                original.Part.Scale = new double3(factor.X, factor.Y, factor.Z);
            else if (_basic)
                original.Part.Scale = original.Scale;
        }
        _basic = !smart;
        SetCharacterScale(smart ? new float3(factor.X) : factor);
        Refresh();
    }

    public void Restore()
    {
        if (Vehicle.IsDisposed) return;
        var current = CurrentParts();
        foreach (var original in _originals)
        {
            // A staged/destroyed part belongs to a different tree; never dereference its modules.
            if (!current.Contains(original.Part)) continue;
            if (original.FullPart) original.Part.PositionParentAsmb = original.Position;
            if (original.FullPart || _basic) original.Part.Scale = original.Scale;
        }
        SetCharacterScale(new float3(1));
        Refresh();
    }

    private void SetCharacterScale(float3 factor)
    {
        if (_avatar == null || Vehicle is not KittenEva kitten) return;
        _avatar.Core.Scale = _avatarScale * factor.X;
        KittenScalePatches.SetScale(kitten.Renderable, factor);
    }

    private void Refresh()
    {
        // Parent setters do not invalidate descendant world-matrix caches in KSA.
        var parts = CurrentParts();
        foreach (var part in parts) part.ResetCachedPosMatrixValues();
        foreach (var part in Vehicle.Parts.Parts) part.RefreshScale();
        foreach (var part in parts) part.UpdateBounds();
        Vehicle.Parts.RecomputeAllDerivedData();
        Vehicle.UpdateAfterPartTreeModification();
    }
}
