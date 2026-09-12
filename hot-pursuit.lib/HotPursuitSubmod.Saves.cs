using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.HotPursuitLib;

public sealed partial class HotPursuitSubmod : ISaveParticipantSource
{
    public sealed class MountedCameraSave
    {
        public SavedPartReference Target { get; set; } = new();
        public double3 MountPoint, SurfaceNormal, MountTangent, Translation, RotationDeg;
        public float FieldOfView;
        public int Width, Height;
        public bool Visible, ViewportOpen;
    }
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<MountedCameraSave[]>("hot-pursuit", CaptureCameras, ResetCameras, RestoreCameras, 180, ValidateCameras); }
    }
    private MountedCameraSave[] CaptureCameras() => _cameras.Select(entry =>
    {
        ResolveTarget(entry);
        if (entry.Vehicle != null && entry.Part != null) entry.SaveTarget = SavedPartReference.Capture(entry.Vehicle, entry.Part);
        if (entry.SaveTarget == null)
            throw new InvalidOperationException($"Camera #{entry.Id} has an unresolved part; its previous saved state must be retained.");
        return new MountedCameraSave { Target = entry.SaveTarget,
            MountPoint = entry.MountPoint, SurfaceNormal = entry.SurfaceNormal, MountTangent = entry.MountTangent,
            Translation = entry.Translation, RotationDeg = entry.RotationDeg, FieldOfView = entry.FieldOfView,
            Width = entry.Width, Height = entry.Height, Visible = entry.Visible,
            ViewportOpen = ViewportRegistry.TryGetOwned(entry.Owner, out _) };
    }).ToArray();
    private void ResetCameras()
    {
        Disarm("Placement cancelled by save load.");
        foreach (var entry in _cameras) ReleaseLease(entry);
        _cameras.Clear();
    }
    private static void ValidateCameras(MountedCameraSave[] entries)
    {
        if (entries.Length > 256) throw new InvalidOperationException("Too many saved cameras.");
        foreach (var s in entries)
            if (s == null || s.Target == null || s.Width is < 64 or > 2048 || s.Height is < 64 or > 2048
                || s.FieldOfView is < 1 or > 179 || s.SurfaceNormal.LengthSquared() < .5 || s.MountTangent.LengthSquared() < .5)
                throw new InvalidOperationException("Invalid mounted camera settings.");
    }
    private void RestoreCameras(MountedCameraSave[] entries, SaveRestoreContext context)
    {
        foreach (var s in entries)
        {
            try
            {
                var part = s.Target.Resolve(); var vehicle = VehicleProvider.FindVehicle(s.Target.VehicleId);
                if (part == null || vehicle == null) { context.Warn($"Camera target unavailable: {s.Target.VehicleId}."); continue; }
                var entry = new HotPursuitCamera { VehicleId = vehicle.Id, PartInstanceId = part.InstanceId, Vehicle = vehicle, Part = part, SaveTarget = s.Target,
                    MountPoint = s.MountPoint, SurfaceNormal = s.SurfaceNormal, MountTangent = s.MountTangent,
                    Translation = s.Translation, RotationDeg = s.RotationDeg, FieldOfView = s.FieldOfView,
                    Width = s.Width, Height = s.Height, Visible = s.Visible, LeaseLost = !s.ViewportOpen };
                _cameras.Add(entry);
                if (s.ViewportOpen && !TryOpenViewport(entry)) context.Warn($"Camera on {vehicle.Id} restored; no secondary viewport slot is available. Reopen it in Hot Pursuit.");
            }
            catch (Exception ex) { context.Warn("Mounted camera: " + ex.Message); }
        }
    }
}
