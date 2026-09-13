using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.BloominOnionLib;

public sealed partial class BloominOnionSubmod : ISaveParticipantSource
{
    public sealed class BodyRingSave
    {
        public string BodyId { get; set; } = "";
        public RingDefinition Definition { get; set; } = new();
    }
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<BodyRingSave[]>("bloomin-onion", () => _controller.Applied.Select(e =>
            new BodyRingSave { BodyId = e.BodyId, Definition = e.Definition.Clone() }).ToArray(),
            _controller.ResetForSaveLoad, RestoreRings, 30, ValidateRings); }
    }
    private static void ValidateRings(BodyRingSave[] entries)
    {
        if (entries.Length > 256) throw new InvalidOperationException("Too many saved rings.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in entries)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.BodyId) || !ids.Add(s.BodyId) || s.Definition == null)
                throw new InvalidOperationException("Invalid or duplicate saved ring body.");
            var d = s.Definition;
            if (d.Name == null || d.ObjectsName == null || d.Lods == null || d.Lods.Count is < 1 or > 5
                || d.Stripes == null || d.Stripes.Count > 1024 || d.Lods.Any(l => l == null || l.MeshId == null || l.MinScreenSizePixels < 0)
                || d.Stripes.Any(l => l == null || l.Start < 0 || l.End > 1 || l.End < l.Start || l.Feather < 0)
                || !Enum.IsDefined(d.BandSource) || d.BandTextureId == null || d.ControlTextureId == null || d.DiffuseId == null || d.NormalId == null || d.PbrId == null
                || d.InnerRadiusKm <= 0 || d.OuterRadiusKm <= d.InnerRadiusKm || d.DetailScale <= 0
                || d.ObjectSizeM <= 0 || d.ObjectDensityPerKm3 <= 0 || d.ObjectRenderDistanceKm <= 0 || d.ObjectThicknessKm <= 0
                || d.VolumeMinThicknessKm < 0 || d.VolumeMaxThicknessKm < d.VolumeMinThicknessKm
                || d.VolumeMinRenderDistanceKm < 0 || d.VolumeMaxRenderDistanceKm < d.VolumeMinRenderDistanceKm
                || d.StepScale <= 0 || d.StepMinSizeKm <= 0 || d.StepMaxSizeKm < d.StepMinSizeKm
                || d.NoiseAmount is < 0 or > 1 || d.NoiseScale <= 0 || d.MeshCoverageThreshold is < 0 or > 1)
                throw new InvalidOperationException("Invalid saved ring definition.");
        }
    }
    private void RestoreRings(BodyRingSave[] entries, SaveRestoreContext context)
    {
        if (entries.Length == 0) return;
        _controller.RefreshAssets();
        foreach (var s in entries)
            try
            {
                var targets = CelestialProvider.GetAllCelestials().Where(b => b.Id == s.BodyId).ToArray();
                if (targets.Length != 1) { context.Warn($"Ring body unavailable or ambiguous: {s.BodyId}."); continue; }
                if (!_controller.Apply(targets[0], s.Definition, out var message)) context.Warn($"Ring on {s.BodyId}: {message}");
            }
            catch (Exception ex) { context.Warn($"Ring on {s.BodyId}: {ex.Message}"); }
    }
}
