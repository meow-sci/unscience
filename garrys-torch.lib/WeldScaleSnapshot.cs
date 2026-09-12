using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.GarrysTorchLib;

/// <summary>One source's original scales; edits never accumulate and never own child animation.</summary>
internal sealed partial class WeldScaleSnapshot
{
    private sealed class Original(double3 scale)
    {
        public readonly double3 Scale = scale;
        public bool Modified;
    }

    private readonly Vehicle _vehicle;
    private readonly Dictionary<Part, Original> _originals = new();
    private readonly CharacterAvatar? _avatar;
    private readonly float _avatarScale;
    private bool _avatarModified;

    public WeldScaleSnapshot(Vehicle vehicle)
    {
        _vehicle = vehicle;
        foreach (var part in vehicle.Parts.Parts) _originals.Add(part, new(part.Scale));
        if (vehicle is KittenEva kitten)
        {
            _avatar = ReflectionHelpers.GetFieldValue<CharacterAvatar>(kitten.Renderable, "_characterAvatar")
                ?? throw new InvalidOperationException("The kitten's character is not ready. Try again after it has spawned.");
            _avatarScale = _avatar.Core.Scale;
        }
    }

    public void Apply(float3 factor)
    {
        // Only full parts: KSA composes each subpart's matrix with its parent's matrix.
        // Multiplying child scales too would apply the factor again at every nesting level.
        foreach (var part in _vehicle.Parts.Parts)
        {
            // Parts added during a weld enter the session at their current local scale.
            if (!_originals.TryGetValue(part, out var original))
                _originals.Add(part, original = new(part.Scale));
            var scale = new double3(original.Scale.X * factor.X,
                original.Scale.Y * factor.Y, original.Scale.Z * factor.Z);
            if (part.Scale == scale) continue;
            original.Modified = true;
            part.Scale = scale;
            InvalidateSubtree(part);
        }

        if (_avatar != null && _vehicle is KittenEva kitten)
        {
            // Preserve nonstandard starting avatar sizes too; 0.01 is only the stock default.
            if (!_avatarModified && WeldScale.Equals(factor, WeldScale.Identity)) return;
            _avatarModified = true;
            _avatar.Core.Scale = _avatarScale * factor.X;
            KittenScalePatches.SetScale(kitten.Renderable, factor);
        }
    }

    public void Restore()
    {
        if (_vehicle.IsDisposed) return;
        // Only visit parts still in this source's full-part list. Detached/destroyed parts
        // may now belong to another vehicle and must not be mutated by this weld.
        foreach (var part in _vehicle.Parts.Parts)
        {
            if (!_originals.TryGetValue(part, out var original) || !original.Modified) continue;
            part.Scale = original.Scale;
            InvalidateSubtree(part);
        }
        if (_avatarModified && _avatar != null && _vehicle is KittenEva kitten)
        {
            _avatar.Core.Scale = _avatarScale;
            KittenScalePatches.SetScale(kitten.Renderable, WeldScale.Identity);
        }
    }

    private static void InvalidateSubtree(Part part)
    {
        // The Scale setter only invalidates its own matrices, not descendant world caches.
        part.ResetCachedPosMatrixValues();
        foreach (var child in part.SubParts) InvalidateSubtree(child);
    }
}
