using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.GarrysTorchLib;

public sealed class SavedWeldScale
{
    public List<SavedWeldPartScale> Parts { get; set; } = new();
    public float AvatarScale { get; set; }
}

public sealed class SavedWeldPartScale
{
    public SavedPartReference Part { get; set; } = new();
    public double3 Scale { get; set; }
}

internal sealed partial class WeldScaleSnapshot
{
    internal SavedWeldScale CaptureSaved() => new()
    {
        AvatarScale = _avatarScale,
        Parts = _vehicle.Parts.Parts.ToArray().Select(part => new SavedWeldPartScale
        {
            Part = SavedPartReference.Capture(_vehicle, part),
            Scale = _originals.TryGetValue(part, out var original) ? original.Scale : part.Scale
        }).ToList()
    };

    internal WeldScaleSnapshot(Vehicle vehicle, SavedWeldScale saved) : this(vehicle)
    {
        var resolved = saved.Parts.Select(item => (Part: item.Part.Resolve(), item.Scale)).ToArray();
        if (resolved.Any(item => item.Part == null) || resolved.Select(item => item.Part).Distinct().Count() != resolved.Length
            || resolved.Length != vehicle.Parts.Parts.ToArray().Length)
            throw new InvalidOperationException("Saved weld scale baseline does not match this vessel's parts.");
        _originals.Clear();
        foreach (var item in resolved) _originals.Add(item.Part!, new(item.Scale) { Modified = true });
        _avatarScale = saved.AvatarScale;
        _avatarModified = _avatar != null;
    }

    internal void RestoreSavedAvatar(float3 factor)
    {
        // Full-part effective scales are already native save data. Only the separate kitten
        // avatar/render correction needs replay before the next animation edit.
        if (_avatar == null || _vehicle is not KittenEva kitten) return;
        _avatar.Core.Scale = _avatarScale * factor.X;
        KittenScalePatches.SetScale(kitten.Renderable, factor);
        _avatarModified = true;
    }
}

public static partial class WeldEngine
{
    internal static SavedWeldScale CaptureSavedScale(Vehicle vehicle) =>
        ScaleSnapshots.GetValue(vehicle, source => new WeldScaleSnapshot(source)).CaptureSaved();

    internal static void ImportSavedScale(Vehicle vehicle, SavedWeldScale saved, float3? effectiveFactor = null)
    {
        var snapshot = new WeldScaleSnapshot(vehicle, saved);
        ScaleSnapshots.Remove(vehicle);
        ScaleSnapshots.Add(vehicle, snapshot);
        if (effectiveFactor.HasValue) snapshot.RestoreSavedAvatar(effectiveFactor.Value);
    }
}
