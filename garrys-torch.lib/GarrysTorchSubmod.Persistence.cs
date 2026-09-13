using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.GarrysTorchLib;

public sealed class SavedWeld
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public SavedPartReference? TargetPart { get; set; }
    public float3 Position { get; set; }
    public float3 Rotation { get; set; }
    public float3 Scale { get; set; } = new(1);
    public bool LockRotation { get; set; }
    public bool Collisions { get; set; }
    public bool Enabled { get; set; }
    public List<SavedWeldAnimation> Animations { get; set; } = new();
    public SavedWeldScale Baseline { get; set; } = new();
}

public sealed partial class GarrysTorchSubmod : ISaveParticipantSource
{
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<List<SavedWeld>>("garrys-torch", CaptureWelds, ResetSavedWelds,
            RestoreWelds, 60, ValidateWelds); }
    }

    private List<SavedWeld> CaptureWelds() => _welds.Select(w => new SavedWeld
    {
        Source = w.Source.Id, Target = w.Target.Id,
        TargetPart = w.TargetPart == null ? null : SavedPartReference.Capture(w.Target, w.TargetPart),
        Position = w.Position, Rotation = w.Rotation, Scale = w.Scale,
        LockRotation = w.LockRotation, Collisions = w.Collisions, Enabled = w.WeldEnabled,
        Animations = _animationManager.CaptureSaved(w),
        Baseline = WeldEngine.CaptureSavedScale(w.Source)
    }).ToList();

    private void ResetSavedWelds()
    {
        foreach (var weld in _welds.ToArray()) RemoveWeld(weld);
        _animationManager.Clear();
        _targetParts.Clear(); _targetPartIndex = _prevTargetIndex = -1;
        _pendingSourceIndex = _pendingTargetIndex = -1; _weldError = null;
    }

    private static void ValidateWelds(List<SavedWeld> saved)
    {
        if (saved.Count > 10000 || saved.Select(w => w.Source).Distinct().Count() != saved.Count)
            throw new InvalidOperationException("Too many or duplicate weld sources.");
        foreach (var w in saved)
        {
            if (string.IsNullOrWhiteSpace(w.Source) || string.IsNullOrWhiteSpace(w.Target) || w.Source == w.Target
                || (w.TargetPart != null && w.TargetPart.VehicleId != w.Target)
                || !WeldScale.IsValid(w.Scale) || !Finite(w.Position) || !Finite(w.Rotation)
                || w.Animations == null || w.Animations.Count > 10000
                || w.Baseline == null || w.Baseline.Parts == null || w.Baseline.Parts.Count > 100000
                || !float.IsFinite(w.Baseline.AvatarScale) || w.Baseline.AvatarScale < 0
                || w.Baseline.Parts.Any(p => p.Part == null || p.Part.VehicleId != w.Source
                    || !double.IsFinite(p.Scale.X) || !double.IsFinite(p.Scale.Y) || !double.IsFinite(p.Scale.Z)))
                throw new InvalidOperationException("Invalid weld settings or scale baseline.");
            foreach (var animation in w.Animations) animation.Validate();
            var visited = new HashSet<string> { w.Source };
            string target = w.Target;
            while (true)
            {
                if (!visited.Add(target)) throw new InvalidOperationException("Saved weld graph contains a cycle.");
                var next = saved.FirstOrDefault(entry => entry.Source == target);
                if (next == null) break;
                target = next.Target;
            }
        }
    }

    private static bool Finite(float3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private void RestoreWelds(List<SavedWeld> saved, SaveRestoreContext context)
    {
        foreach (var w in saved)
        {
            try
            {
                var source = VehicleProvider.FindVehicle(w.Source);
                var target = VehicleProvider.FindVehicle(w.Target);
                var part = w.TargetPart?.Resolve();
                context.Require(source != null && target != null && (w.TargetPart == null || part != null),
                    $"Unresolved weld {w.Source} → {w.Target}.");
                context.Require(source!.Parent == target!.Parent, "Weld vehicles have different parent bodies.");
                // Import BEFORE applying a multiplier: native save already contains the effective scale.
                var (entry, error) = CreateWeld(w.Source, w.Target, w.Position, w.Rotation, new float3(1), w.LockRotation, part, w.Collisions);
                context.Require(entry != null, error ?? "Weld creation failed.");
                try
                {
                    WeldEngine.ImportSavedScale(source, w.Baseline, w.Scale);
                    entry!.Scale = w.Scale;
                    entry.WeldEnabled = w.Enabled;
                    _animationManager.RestoreSaved(entry, w.Animations);
                }
                catch { RemoveWeld(w.Source); throw; }
            }
            catch (Exception ex) { context.Warn($"{w.Source}: {ex.Message}"); }
        }
        SortWelds();
    }
}
