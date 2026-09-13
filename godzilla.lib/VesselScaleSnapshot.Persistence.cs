using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.GodzillaLib;

public sealed class SavedVesselScaleBaseline
{
    public double3 Pivot { get; set; }
    public float AvatarScale { get; set; }
    public List<SavedVesselPartScale> Parts { get; set; } = new();
}

public sealed class SavedVesselPartScale
{
    public SavedPartReference Part { get; set; } = new();
    public double3 Scale { get; set; }
    public double3 Position { get; set; }
    public double3 CurrentScale { get; set; }
    public bool FullPart { get; set; }
}

internal sealed partial class VesselScaleSnapshot
{
    internal SavedVesselScaleBaseline CaptureSaved() => new()
    {
        Pivot = _pivot, AvatarScale = _avatarScale,
        Parts = _originals.Select(item => new SavedVesselPartScale
        {
            Part = SavedPartReference.Capture(Vehicle, item.Part), Scale = item.Scale,
            Position = item.Position, FullPart = item.FullPart, CurrentScale = item.Part.Scale
        }).ToList()
    };

    internal VesselScaleSnapshot(Vehicle vehicle, SavedVesselScaleBaseline saved) : this(vehicle)
    {
        var parts = saved.Parts.Select(item => (Saved: item, Part: item.Part.Resolve())).ToArray();
        if (parts.Any(item => item.Part == null) || parts.Select(item => item.Part).Distinct().Count() != parts.Length
            || !CurrentParts().SetEquals(parts.Select(item => item.Part!)))
            throw new InvalidOperationException("Saved size baseline does not match this vessel's parts.");
        _pivot = saved.Pivot;
        _avatarScale = saved.AvatarScale;
        _originals.Clear(); _byPart.Clear();
        foreach (var (item, part) in parts)
        {
            if (item.FullPart != !part!.IsSubPart)
                throw new InvalidOperationException("Saved full-part/subpart scale classification changed.");
            var original = new Original(part, item.Scale, item.Position, item.FullPart);
            _originals.Add(original); _byPart.Add(part, original);
        }
    }

    internal void RestoreSavedSubpartScales(SavedVesselScaleBaseline saved)
    {
        // Native serialization omits subpart transforms. Keep the saved effective child values.
        foreach (var item in saved.Parts)
            if (!item.FullPart) item.Part.Resolve()!.Scale = item.CurrentScale;
    }
}
