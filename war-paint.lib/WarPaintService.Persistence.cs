using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.WarPaintLib;

public sealed partial class WarPaintService
{
    public ISaveParticipant SaveParticipant => new SaveParticipant<SavedWarPaint>("war-paint",
        Capture, Reset, Restore, order: 85, validate: ValidateSaved);

    private SavedWarPaint Capture() => new()
    {
        Kittens = GetActivePaints().Select(p => new SavedPaintedKitten
        {
            VehicleId = p.VehicleId, CharacterId = p.CharacterId, PresetName = p.PresetName, Paint = Snapshot(p.Kitten)!
        }).ToList()
    };

    /// <summary>Runs before KSA destroys the old world. Keep only detached, reusable GPU sets.</summary>
    public void Reset()
    {
        SceneRevision++;
        foreach (var paint in _active.Values.ToArray())
        {
            if (!paint.Kitten.IsDisposed) _backend.Restore(paint.Bindings, paint.MaterialSet);
            string key = CacheKey(paint.VehicleId, paint.CharacterId);
            if (_detached.Remove(key, out var old) && !ReferenceEquals(old, paint.MaterialSet)) _backend.Release(old);
            _detached[key] = paint.MaterialSet;
        }
        _active.Clear();
        LastError = null;
    }

    private static void ValidateSaved(SavedWarPaint saved)
    {
        if (saved.Kittens == null || saved.Kittens.Count > 10000
            || saved.Kittens.Any(k => k == null || string.IsNullOrWhiteSpace(k.VehicleId)
                || string.IsNullOrWhiteSpace(k.CharacterId) || k.Paint == null)
            || saved.Kittens.Select(k => k.VehicleId).Distinct(StringComparer.Ordinal).Count() != saved.Kittens.Count)
            throw new InvalidOperationException("Invalid War Paint target identities.");
        foreach (var kitten in saved.Kittens)
        {
            kitten.Paint.Validate();
            if (kitten.PresetName != null && (string.IsNullOrWhiteSpace(kitten.PresetName)
                || kitten.PresetName.Length > 128 || kitten.PresetName.Contains('\0')))
                throw new InvalidOperationException("Invalid War Paint preset name.");
        }
    }

    private void Restore(SavedWarPaint saved, SaveRestoreContext context)
    {
        // This record follows DOH and Humble's v1 migrations. New recipes override their
        // legacy values on the same exact target without creating extra sets.
        foreach (var item in saved.Kittens)
        {
            if (VehicleProvider.FindVehicle(item.VehicleId) is not KittenEva kitten || kitten.IsDisposed)
            { context.Warn($"Painted kitten missing or ambiguous: {item.VehicleId}."); continue; }
            if (kitten.Character.Id != item.CharacterId)
            { context.Warn($"Painted kitten character changed: {item.VehicleId}."); continue; }
            // The optional name is additive to v1. Earlier snapshots become named presets;
            // a missing global entry is recreated from the scene recipe without replacing others.
            string? name = item.PresetName;
            if (name != null && !Presets.TryGet(name, out _) && !Presets.Save(name, item.Paint))
            { context.Warn($"Could not recover paint preset '{name}': {Presets.LastError}"); continue; }
            if (Apply(kitten, item.Paint, name) == null)
                context.Warn($"Could not restore paint for {item.VehicleId}: {LastError}");
        }
        ReleaseDetached();
    }

    /// <summary>Migrate old scene recipes through normal private ownership, with failure diagnostics.</summary>
    public bool ApplyLegacy(KittenEva kitten, PaintSettings paint, SaveRestoreContext context)
    {
        if (Apply(kitten, paint) != null) return true;
        context.Warn($"Could not migrate kitten paint for {kitten.Id}: {LastError}");
        return false;
    }

    public sealed class SavedWarPaint
    {
        public List<SavedPaintedKitten> Kittens { get; set; } = new();
    }

    public sealed class SavedPaintedKitten
    {
        public string VehicleId { get; set; } = "";
        public string CharacterId { get; set; } = "";
        public string? PresetName { get; set; }
        public PaintSettings Paint { get; set; } = new();
    }
}
