using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.PebblesLib;

public sealed partial class PebblesSubmod : ISaveParticipantSource
{
    public sealed class ClutterSave
    {
        public string BodyId { get; set; } = "";
        public PebblesRecipe Recipe { get; set; } = new();
    }
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get
        {
            yield return new SaveParticipant<ClutterSave[]>("pebbles", CaptureClutter, ResetClutter, RestoreClutter, 60, ValidateClutter);
            // Separate v1 record (added for KSA 5482 native clutter saves): removed/displaced clutter on override
            // grids whose spacing differs from stock. Restores after "pebbles" has reapplied the recipes.
            yield return new SaveParticipant<ClutterBodyState[]>("pebbles.clutter-state", _controller.CaptureGridStates, ResetGridState, RestoreGridState, 61, ClutterGridValidation.Validate);
        }
    }
    private ClutterSave[] CaptureClutter() => _controller.Live.Select(e => new ClutterSave { BodyId = e.BodyId, Recipe = RecipeCopy.Clone(e.Recipe) }).ToArray();
    private void ResetClutter()
    {
        _releaseImports = false;
        _workshop.Release(); _workshop.Update();
        _controller.ResetForSaveLoad();
        _assets.ReleaseGlbImports(); _glbOptions = [];
    }
    private static void ValidateClutter(ClutterSave[] entries)
    {
        if (entries.Length > 256) throw new InvalidOperationException("Too many saved clutter bodies.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in entries)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.BodyId) || !ids.Add(s.BodyId)) throw new InvalidOperationException("Invalid or duplicate clutter body.");
            RecipeValidation.Validate(s.Recipe);
        }
    }
    // Grid memory lives on live records, which the "pebbles" reset (ResetForSaveLoad) already discards.
    private static void ResetGridState() { }
    private void RestoreGridState(ClutterBodyState[] entries, SaveRestoreContext context)
    {
        foreach (var s in entries)
            try { _controller.RestoreGridState(s); }
            catch (Exception ex) { context.Warn($"Clutter state on {s.BodyId}: {ex.Message}"); }
    }
    private void RestoreClutter(ClutterSave[] entries, SaveRestoreContext context)
    {
        if (entries.Length == 0) return;
        _assets.Refresh(); _controller.Refresh();
        foreach (var s in entries)
            try { _controller.ApplyForSaveLoad(s.BodyId, s.Recipe); }
            catch (Exception ex) { context.Warn($"Clutter on {s.BodyId}: {ex.Message}"); }
    }
}
