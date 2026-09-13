using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using Brutal.VulkanApi;
using KSA;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.GraffitiLib;

public sealed partial class GraffitiSubmod : ISaveParticipantSource
{
    public sealed class DecalSave
    {
        public string ImageName { get; set; } = "";
        public string BodyId { get; set; } = "";
        public SavedPartReference? Part { get; set; }
        public DecalAnchorKind Kind;
        public int CanopyIndex, ClothNodeA, ClothNodeB, ClothNodeC;
        public double3 ClothBarycentric, Position, Normal;
        public double ClothNormalSign, RotationDeg, Width, Height, Depth, Alpha, Brightness;
        public bool Visible;
    }
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<DecalSave[]>("graffiti", CaptureDecals, ResetDecals, RestoreDecals, 160, ValidateDecals); }
    }
    private DecalSave[] CaptureDecals() => _decals.Select(e =>
    {
        ResolveAnchor(e);
        SavedPartReference? target = null;
        if (e.Kind != DecalAnchorKind.Terrain)
        {
            if (e.Vehicle != null && e.Part != null) e.SaveTarget = SavedPartReference.Capture(e.Vehicle, e.Part);
            if (e.SaveTarget == null)
                throw new InvalidOperationException($"Decal #{e.Id} has an unresolved part; its prior save payload must be retained.");
            target = e.SaveTarget;
        }
        return new DecalSave { ImageName = e.ImageName, BodyId = e.Kind == DecalAnchorKind.Terrain ? e.TargetId : "",
            Part = target, Kind = e.Kind, CanopyIndex = e.ParachuteCanopyIndex,
            ClothNodeA = e.ClothNodeA, ClothNodeB = e.ClothNodeB, ClothNodeC = e.ClothNodeC,
            ClothBarycentric = e.ClothBarycentric, ClothNormalSign = e.ClothNormalSign,
            Position = e.Position, Normal = e.Normal, RotationDeg = e.RotationDeg, Width = e.Width, Height = e.Height,
            Depth = e.Depth, Alpha = e.Alpha, Brightness = e.Brightness, Visible = e.Visible };
    }).ToArray();
    private void ResetDecals()
    {
        Disarm("Placement cancelled by save load.");
        _renderActive = false; _published = Array.Empty<DecalEntry>();
        Program.GetRenderer()?.GraphicsAndCompute?.WaitIdle();
        _renderer?.Dispose(); _renderer = null;
        _textures.DisposeAll(); _decals.Clear();
        _dirty = false; _gpuFailed = false; _drawFaultLogged = false;
    }
    private static void ValidateDecals(DecalSave[] entries)
    {
        if (entries.Length > 10000) throw new InvalidOperationException("Too many saved decals.");
        foreach (var s in entries)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.ImageName) || !Enum.IsDefined(s.Kind)
                || s.Kind == DecalAnchorKind.Terrain && string.IsNullOrWhiteSpace(s.BodyId)
                || s.Kind != DecalAnchorKind.Terrain && s.Part == null
                || s.Width <= 0 || s.Height <= 0 || s.Depth <= 0 || s.Alpha is < 0 or > 1 || s.Brightness is <= 0 or > 8)
                throw new InvalidOperationException("Invalid saved decal.");
            if (s.Kind == DecalAnchorKind.Parachute && (s.CanopyIndex < 0 || s.ClothNodeA is < 0 or > 4096
                || s.ClothNodeB is < 0 or > 4096 || s.ClothNodeC is < 0 or > 4096
                || Math.Abs(s.ClothNormalSign) != 1 || s.ClothBarycentric.X < 0 || s.ClothBarycentric.Y < 0 || s.ClothBarycentric.Z < 0
                || Math.Abs(s.ClothBarycentric.X + s.ClothBarycentric.Y + s.ClothBarycentric.Z - 1) > .00001))
                throw new InvalidOperationException("Invalid saved canopy anchor.");
        }
    }
    private void RestoreDecals(DecalSave[] entries, SaveRestoreContext context)
    {
        foreach (var s in entries)
        {
            try
            {
                Part? part = s.Part?.Resolve();
                if (s.Kind != DecalAnchorKind.Terrain && part == null) { context.Warn($"Decal target unavailable: {s.Part?.VehicleId}."); continue; }
                var entry = new DecalEntry { ImageName = s.ImageName, Kind = s.Kind,
                    SaveTarget = s.Part,
                    TargetId = s.Kind == DecalAnchorKind.Terrain ? s.BodyId : s.Part!.VehicleId,
                    PartInstanceId = part?.InstanceId ?? 0, ParachuteCanopyIndex = s.CanopyIndex,
                    ClothNodeA = s.ClothNodeA, ClothNodeB = s.ClothNodeB, ClothNodeC = s.ClothNodeC,
                    ClothBarycentric = s.ClothBarycentric, ClothNormalSign = s.ClothNormalSign, Position = s.Position,
                    Normal = s.Normal, RotationDeg = s.RotationDeg, Width = s.Width, Height = s.Height, Depth = s.Depth,
                    Alpha = s.Alpha, Brightness = s.Brightness, Visible = s.Visible };
                ResolveAnchor(entry);
                if (s.Kind == DecalAnchorKind.Terrain && entry.Body == null) { context.Warn($"Decal body unavailable: {s.BodyId}."); continue; }
                if (s.Kind == DecalAnchorKind.Parachute && entry.Parachute == null)
                    context.Warn($"Canopy {s.CanopyIndex} unavailable on {entry.TargetId}; its decal remains dormant.");
                entry.TextureHandle = _textures.Resolve(entry.ImageName, out var textureState) ?? -1;
                entry.TextureState = textureState;
                if (textureState != DecalTextureState.Ready) context.Warn($"Decal image {s.ImageName}: {textureState}; rescan PNGs after restoring the file.");
                _decals.Add(entry); _dirty = true;
            }
            catch (Exception ex) { context.Warn($"Decal {s.ImageName}: {ex.Message}"); }
        }
    }
}
