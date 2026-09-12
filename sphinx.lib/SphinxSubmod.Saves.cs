using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Brutal.Numerics;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.PebblesLib;

namespace MeowSci.SphinxLib;

public sealed partial class SphinxSubmod : ISaveParticipantSource
{
    public sealed class StaticSave
    {
        public string BodyId { get; set; } = "";
        public string MeshId { get; set; } = "";
        public string? Png { get; set; }
        public double3 PositionCcf, NormalCcf;
        public float3 Scale, Rotation, Offset;
        public Vector2 UvScale, UvOffset;
        public bool Visible, Align;
        public int Collision;
    }
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<StaticSave[]>("sphinx", CaptureStatics, ResetStatics, RestoreStatics, 150, ValidateStatics); }
    }
    private StaticSave[] CaptureStatics() => _entries.Select(e => new StaticSave { BodyId = e.Anchor.Body.Id,
        MeshId = e.MeshId, Png = e.Png, PositionCcf = e.Anchor.PositionCcf, NormalCcf = e.Anchor.NormalCcf,
        Scale = e.Scale, Rotation = e.Rotation, Offset = e.Offset, UvScale = e.Mapping.Scale, UvOffset = e.Mapping.Offset,
        Visible = e.Visible, Align = e.Align, Collision = (int)e.Collision }).ToArray();
    private void ResetStatics()
    {
        _pending.Clear(); WaitForPhysics(); Clear(); _selectedId = 0;
    }
    private static void ValidateStatics(StaticSave[] entries)
    {
        if (entries.Length > 32) throw new InvalidOperationException("Too many saved Sphinx statics.");
        foreach (var s in entries)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.BodyId) || string.IsNullOrWhiteSpace(s.MeshId)
                || !Enum.IsDefined((CollisionMode)s.Collision) || s.PositionCcf.LengthSquared() <= 0 || s.NormalCcf.LengthSquared() < .5
                || s.Scale.X <= 0 || s.Scale.Y <= 0 || s.Scale.Z <= 0)
                throw new InvalidOperationException("Invalid saved static placement.");
            _ = GlbIdentity.Parse(s.MeshId);
            new TextureMapping(s.UvScale, s.UvOffset).Validate();
        }
    }
    private void RestoreStatics(StaticSave[] entries, SaveRestoreContext context)
    {
        foreach (var s in entries)
        {
            try
            {
                var matches = CelestialProvider.GetAllCelestials().Where(b => b.Id == s.BodyId).ToArray();
                if (matches.Length != 1) { context.Warn($"Static body unavailable or ambiguous: {s.BodyId}."); continue; }
                if (!SphinxPatches.Ready) throw new InvalidOperationException("Sphinx render hooks are unavailable.");
                var mapping = new TextureMapping(s.UvScale, s.UvOffset);
                var resource = new StaticModelResources(_assets, s.MeshId, s.Png, 8_000_000 - _entries.Sum(e => e.Model.VertexCount), mapping);
                try
                {
                    _ = PlacementMath.GroundedLocal(resource.Min, resource.Max, SphinxEntry.Vector(s.Scale), SphinxEntry.Vector(s.Rotation), SphinxEntry.Vector(s.Offset));
                    var entry = new SphinxEntry { Id = _nextId++, MeshId = s.MeshId, Png = s.Png,
                        Anchor = new(matches[0], s.PositionCcf, s.NormalCcf), Model = resource, Mapping = mapping,
                        Scale = s.Scale, Rotation = s.Rotation, Offset = s.Offset, Align = s.Align, Visible = s.Visible,
                        Collision = (CollisionMode)s.Collision };
                    entry.Collider = BuildCollider(entry, entry.Collision, entry.Scale, entry.Rotation, entry.Offset);
                    _entries.Add(entry);
                }
                catch { resource.Dispose(); throw; }
            }
            catch (Exception ex) { context.Warn($"Static on {s.BodyId}: {ex.Message}"); }
        }
    }
}
