using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.RockyMcRockFaceLib;

public sealed partial class RockyMcRockFaceSubmod : ISaveParticipantSource
{
    public sealed class RingSwapSave
    {
        public string BodyId { get; set; } = "";
        public string[] LodMeshes { get; set; } = [];
        public string Diffuse { get; set; } = "";
        public string Normal { get; set; } = "";
        public string Pbr { get; set; } = "";
        public string Band { get; set; } = "";
        public bool OverrideField;
        public double SizeM, DensityPerKm3, RenderDistanceKm, ThicknessKm;
        internal RingSelection Selection()
        {
            var result = new RingSelection { DiffuseId = Diffuse, NormalId = Normal, PbrId = Pbr, BandTextureId = Band,
                OverrideFieldSettings = OverrideField, SizeM = SizeM, DensityPerKm3 = DensityPerKm3,
                RenderDistanceKm = RenderDistanceKm, ThicknessKm = ThicknessKm };
            Array.Copy(LodMeshes, result.LodMeshIds, RingSelection.MaxLods); return result;
        }
    }
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<RingSwapSave[]>("rocky-mcrock-face", CaptureRingSwaps,
            () => { _controller.ResetForSaveLoad(); _selections.Clear(); }, RestoreRingSwaps, 40, ValidateRingSwaps); }
    }
    private RingSwapSave[] CaptureRingSwaps() => _controller.AppliedSelections
        .Where(e => ReferenceEquals(e.Body.Celestial.BodyTemplate.RingsReference, e.Body.Rings))
        .Select(e => new RingSwapSave { BodyId = e.Body.Id, LodMeshes = e.Selection.LodMeshIds.ToArray(),
            Diffuse = e.Selection.DiffuseId, Normal = e.Selection.NormalId, Pbr = e.Selection.PbrId, Band = e.Selection.BandTextureId,
            OverrideField = e.Selection.OverrideFieldSettings, SizeM = e.Selection.SizeM, DensityPerKm3 = e.Selection.DensityPerKm3,
            RenderDistanceKm = e.Selection.RenderDistanceKm, ThicknessKm = e.Selection.ThicknessKm }).ToArray();
    private static void ValidateRingSwaps(RingSwapSave[] entries)
    {
        if (entries.Length > 256) throw new InvalidOperationException("Too many ring swaps.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in entries)
            if (s == null || string.IsNullOrWhiteSpace(s.BodyId) || !ids.Add(s.BodyId) || s.LodMeshes == null
                || s.LodMeshes.Length != RingSelection.MaxLods || s.LodMeshes.Any(x => x == null)
                || s.Diffuse == null || s.Normal == null || s.Pbr == null || s.Band == null
                || s.OverrideField && (s.SizeM <= 0 || s.DensityPerKm3 <= 0 || s.RenderDistanceKm <= 0 || s.ThicknessKm <= 0))
                throw new InvalidOperationException("Invalid saved ring swap.");
    }
    private void RestoreRingSwaps(RingSwapSave[] entries, SaveRestoreContext context)
    {
        if (entries.Length == 0) return;
        _controller.Catalog.Refresh(); _controller.RefreshBodies();
        bool changed = false;
        foreach (var s in entries)
        {
            try
            {
                var targets = _controller.Bodies.Where(b => b.Id == s.BodyId).ToArray();
                if (targets.Length != 1) { context.Warn($"Ring swap body unavailable or ambiguous: {s.BodyId}."); continue; }
                var selection = s.Selection();
                if (!_controller.Apply(targets[0], selection, out var message)) { context.Warn($"Ring swap on {s.BodyId}: {message}"); continue; }
                _selections[s.BodyId] = selection.Clone(); changed = true;
            }
            catch (Exception ex) { context.Warn($"Ring swap on {s.BodyId}: {ex.Message}"); }
        }
        if (changed && !_controller.RebuildRenderer(out var error)) throw new InvalidOperationException(error);
    }
}
