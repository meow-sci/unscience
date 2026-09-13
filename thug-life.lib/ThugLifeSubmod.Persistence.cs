using System.Collections.Generic;
using System;
using System.Linq;
using Brutal.Numerics;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.ThugLifeLib;

public sealed partial class ThugLifeSubmod
{
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get
        {
            yield return new SaveParticipant<List<SavedQuad>>("thug-life", CaptureQuads, ResetQuads, RestoreQuads,
                validate: saved =>
                {
                    if (saved.Count > 100000 || saved.Any(q => q == null || q.Anchor == null || q.Width <= 0 || q.Height <= 0))
                        throw new InvalidOperationException("Invalid saved sunglasses quad records.");
                });
        }
    }

    private List<SavedQuad> CaptureQuads()
    {
        var result = new List<SavedQuad>();
        if (_manager == null) return result;
        foreach (var entry in _manager.Entries)
        {
            if (entry.Vehicle.IsDisposed) continue;
            result.Add(new()
            {
                Anchor = SavedPartReference.Capture(entry.Vehicle, entry.Part), Position = entry.Position,
                Rotation = entry.Rotation, Width = entry.Width, Height = entry.Height, Visible = entry.Visible
            });
        }
        return result;
    }

    private void ResetQuads()
    {
        // Shared renderer resources remain valid across a world load. Only their old anchors
        // and animations need clearing; normal unload still owns GPU resource disposal.
        if (_manager != null)
            foreach (var entry in new List<ThugLifeEntry>(_manager.Entries)) _manager.Remove(entry);
        _topLevelParts.Clear();
        _subParts.Clear();
        _pendingVehicleIndex = _pendingPartIndex = _pendingSubPartIndex = -1;
        _prevVehicleIndex = _prevPartIndex = -2;
    }

    private void RestoreQuads(List<SavedQuad> entries, SaveRestoreContext context)
    {
        foreach (var item in entries)
        {
            var part = item.Anchor.Resolve();
            var vehicle = VehicleProvider.FindVehicle(item.Anchor.VehicleId);
            if (part == null || vehicle == null) { context.Warn($"Sunglasses anchor missing: {item.Anchor.VehicleId}."); continue; }
            context.Require(item.Width > 0 && item.Height > 0, "Sunglasses dimensions must be positive.");
            var entry = new ThugLifeEntry
            {
                Vehicle = vehicle, Part = part, Position = item.Position, Rotation = item.Rotation,
                Width = item.Width, Height = item.Height, Visible = item.Visible
            };
            if (_manager == null || !_manager.Add(entry)) context.Warn(_manager?.LastError ?? "Sunglasses renderer unavailable.");
        }
    }

    public sealed class SavedQuad
    {
        public SavedPartReference Anchor { get; set; } = new();
        public float3 Position { get; set; }
        public float3 Rotation { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public bool Visible { get; set; }
    }
}
