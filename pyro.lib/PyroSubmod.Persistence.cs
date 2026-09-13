using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;
using KSA;

namespace MeowSci.PyroLib;

public sealed partial class PyroSubmod
{
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get
        {
            yield return new SaveParticipant<Dictionary<string, SavedPlumeTemplate>>("pyro.templates",
                CaptureTemplateChanges, ResetTemplateChanges, RestoreTemplateChanges, order: 30,
                validate: saved =>
                {
                    if (saved.Count > 10000 || saved.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null))
                        throw new InvalidOperationException("Invalid saved plume template table.");
                    foreach (var template in saved.Values) template.Validate();
                });
            yield return new SaveParticipant<List<SavedPlume>>("pyro", CapturePlumes, ResetPlumes, RestorePlumes,
                validate: saved =>
                {
                    if (saved.Count > 100000 || saved.Any(p => p == null || p.Anchor == null || p.Settings == null
                        || p.Settings.Nozzle == null || string.IsNullOrWhiteSpace(p.Settings.TemplateId)
                        || p.CycleOnSeconds is < .05f or > 3600 || p.CycleOffSeconds is < .05f or > 3600
                        || p.CycleRemaining < 0)) throw new InvalidOperationException("Invalid saved plume records.");
                });
        }
    }

    private void ResetPlumes()
    {
        _plumes.Clear();
        _partsVehicle = null; _subPartsOwner = null;
        _topParts.Clear(); _subParts.Clear();
        _topPartLabels = _subPartLabels = Array.Empty<string>();
        _pendingVehicleIndex = _pendingPartIndex = -1;
        _pendingSubPartIndex = 0;
    }

    private List<SavedPlume> CapturePlumes()
    {
        PruneDeadPlumes();
        var result = new List<SavedPlume>();
        foreach (var plume in _plumes)
            result.Add(new()
            {
                Anchor = SavedPartReference.Capture(plume.Vehicle, plume.Part),
                Settings = PlumePreset.FromPlume(plume), Enabled = plume.Enabled,
                CycleOnSeconds = plume.Cycle.OnSeconds, CycleOffSeconds = plume.Cycle.OffSeconds,
                CycleRunning = plume.Cycle.Running, CycleIsOn = plume.Cycle.IsOn,
                CycleRemaining = plume.Cycle.RemainingSeconds
            });
        return result;
    }

    private void RestorePlumes(List<SavedPlume> saved, SaveRestoreContext context)
    {
        foreach (var item in saved)
        {
            var part = item.Anchor.Resolve();
            var vehicle = VehicleProvider.FindVehicle(item.Anchor.VehicleId);
            if (part == null || vehicle == null) { context.Warn($"Pyro anchor missing: {item.Anchor.VehicleId}."); continue; }
            var p = item.Settings;
            var (plume, error) = CreatePlume(vehicle, part, p.TemplateId, p.Position, p.Rotation,
                p.Nozzle, p.Throttle, p.AbsorptionDensityScale, p.RefractionIntensity);
            if (plume == null) { context.Warn(error ?? "Pyro could not create a plume."); continue; }
            plume.Enabled = item.Enabled;
            plume.Cycle.OnSeconds = item.CycleOnSeconds;
            plume.Cycle.OffSeconds = item.CycleOffSeconds;
            if (item.CycleRunning)
                plume.Cycle.RestorePhase(Universe.GetElapsedTime().Seconds(), item.CycleIsOn, item.CycleRemaining);
        }
    }

    public sealed class SavedPlume
    {
        public SavedPartReference Anchor { get; set; } = new();
        public PlumePreset Settings { get; set; } = new();
        public bool Enabled { get; set; }
        public float CycleOnSeconds { get; set; } = 1;
        public float CycleOffSeconds { get; set; } = 1;
        public bool CycleRunning { get; set; }
        public bool CycleIsOn { get; set; }
        public double CycleRemaining { get; set; }
    }
}
