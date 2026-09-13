using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using MeowSci.GarrysTorchLib;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.GodzillaLib;

public sealed class GodzillaSaveState
{
    public bool Physics { get; set; } = true;
    public bool Colliders { get; set; } = true;
    public List<SavedVesselSize> Vessels { get; set; } = new();
}

public sealed class SavedVesselSize
{
    public string VehicleId { get; set; } = "";
    public bool Smart { get; set; }
    public float3 Factor { get; set; }
    public bool Physics { get; set; }
    public bool Colliders { get; set; }
    public SavedVesselScaleBaseline Baseline { get; set; } = new();
}

public sealed partial class GodzillaSubmod : ISaveParticipantSource
{
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<GodzillaSaveState>("godzilla", CaptureSizes, ResetSavedSizes,
            RestoreSizes, 50, ValidateSizes); }
    }

    private GodzillaSaveState CaptureSizes() => new()
    {
        Physics = ScalePhysics, Colliders = ScaleColliders,
        Vessels = _sessions.Select(pair => new SavedVesselSize
        {
            VehicleId = pair.Key.Id, Smart = pair.Value.Smart, Factor = pair.Value.Factor,
            Physics = pair.Value.ScalePhysics, Colliders = pair.Value.ScaleColliders,
            Baseline = pair.Value.Snapshot.CaptureSaved()
        }).ToList()
    };

    private void ResetSavedSizes()
    {
        foreach (var vehicle in _sessions.Keys.ToArray()) Restore(vehicle);
        if (_sessions.Count != 0) throw new InvalidOperationException("Could not release all Godzilla size ownership.");
        ScalePhysics = ScaleColliders = true;
    }

    private static void ValidateSizes(GodzillaSaveState state)
    {
        if (state.Vessels == null || state.Vessels.Count > 10000
            || state.Vessels.Select(v => v.VehicleId).Distinct().Count() != state.Vessels.Count)
            throw new InvalidOperationException("Invalid saved vessel-size list.");
        foreach (var v in state.Vessels)
        {
            if (!WeldScale.IsValid(v.Factor) || string.IsNullOrWhiteSpace(v.VehicleId) || v.Baseline == null
                || v.Baseline.Parts == null || v.Baseline.Parts.Count > 100000 || !Finite(v.Baseline.Pivot)
                || !float.IsFinite(v.Baseline.AvatarScale) || v.Baseline.AvatarScale < 0
                || v.Baseline.Parts.Any(p => p.Part == null || p.Part.VehicleId != v.VehicleId
                    || !Finite(p.Scale) || !Finite(p.CurrentScale) || !Finite(p.Position)))
                throw new InvalidOperationException("Invalid saved size or original geometry.");
        }
    }

    private static bool Finite(double3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    private void RestoreSizes(GodzillaSaveState state, SaveRestoreContext context)
    {
        ScalePhysics = state.Physics; ScaleColliders = state.Colliders;
        foreach (var saved in state.Vessels)
        {
            try
            {
                var vehicle = VehicleProvider.FindVehicle(saved.VehicleId);
                context.Require(vehicle != null, "Vessel is unavailable.");
                var snapshot = new VesselScaleSnapshot(vehicle!, saved.Baseline);
                snapshot.ValidateApply(saved.Factor, saved.Physics, saved.Colliders);
                context.Require(VehicleScaleOwnership.TryAcquire(vehicle!, Owner), "Vessel size is owned by another feature.");
                try
                {
                    snapshot.RestoreSavedSubpartScales(saved.Baseline);
                    snapshot.Apply(saved.Smart, saved.Factor, saved.Physics, saved.Colliders);
                    _sessions[vehicle!] = new(snapshot, saved.Smart, saved.Factor, saved.Physics, saved.Colliders);
                }
                catch { snapshot.Restore(); VehicleScaleOwnership.Release(vehicle!, Owner); throw; }
            }
            catch (Exception ex) { context.Warn($"{saved.VehicleId}: {ex.Message}"); }
        }
    }
}
