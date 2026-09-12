using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.ZippoLib;

namespace MeowSci.ItsSoShinyLib;

public sealed class ShinySaveState
{
    public bool RenderParts { get; set; }
    public List<SavedShinyGrid> Grids { get; set; } = new();
}

public sealed class SavedShinyGrid
{
    public string VehicleId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Owned { get; set; }
    public bool PendingDestroy { get; set; }
    public float3 Color { get; set; }
    public float Intensity { get; set; }
    public List<SavedShinyCell> Cells { get; set; } = new();
    public (int x, int y)[] Active { get; set; } = Array.Empty<(int, int)>();
    public (int x, int y)[] ScrollPixels { get; set; } = Array.Empty<(int, int)>();
    public bool Scrolling { get; set; }
    public float Speed { get; set; }
    public float Offset { get; set; }
}

public sealed class SavedShinyCell
{
    public int Row { get; set; }
    public int Col { get; set; }
    public SavedPartReference Host { get; set; } = new();
    public SavedPartReference Light { get; set; } = new();
}

public sealed partial class ItsSoShinySubmod : ISaveParticipantSource
{
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<ShinySaveState>("its-so-shiny", CaptureGrids,
            ResetSavedGrids, RestoreGrids, 30, ValidateGrids); }
    }

    private ShinySaveState CaptureGrids() => new()
    {
        RenderParts = ShinyPatchState.RenderShinyParts,
        Grids = ShinyGridManager.Grids.Values.Select(g => new SavedShinyGrid
        {
            VehicleId = g.VehicleId, Name = g.GridName, PendingDestroy = _pendingDestroy.Contains((g.VehicleId, g.GridName)), Owned = g.ShinyGrid.IsOwned,
            Color = g.Color, Intensity = g.Intensity,
            Cells = g.ShinyGrid.Grid.Cells.Values.Select(cell => new SavedShinyCell
            {
                Row = cell.Row, Col = cell.Col,
                Host = SavedPartReference.Capture(g.Vehicle, cell.HostPart),
                Light = SavedPartReference.Capture(g.Vehicle, cell.LightPart)
            }).ToList(),
            Active = g.ActivePixels.Select(p => (p.col, p.row)).ToArray(),
            Scrolling = g.Scroll.IsActive, ScrollPixels = g.Scroll.SavedPixels,
            Speed = g.Scroll.ScrollSpeed, Offset = g.Scroll.SavedOffset
        }).ToList()
    };

    private void ResetSavedGrids()
    {
        ShinyGridManager.Clear(); ShinyPatchState.RenderShinyParts = false;
        _deferredActions.Clear(); _pendingDestroy.Clear(); _deferredTimer = 0;
        _selectedVehicleIndex = -1;
    }

    private static void ValidateGrids(ShinySaveState state)
    {
        if (state.Grids == null || state.Grids.Count > 10000
            || state.Grids.Select(g => (g.VehicleId, g.Name)).Distinct().Count() != state.Grids.Count)
            throw new InvalidOperationException("Invalid saved Shiny grid list.");
        foreach (var g in state.Grids)
        {
            if (!ShinyPixelGrid.IsValidGridName(g.Name) || g.Cells == null || g.Cells.Count > 100000
                || g.Active == null || g.Active.Length > 1000000 || g.ScrollPixels == null || g.ScrollPixels.Length > 1000000
                || !float.IsFinite(g.Speed) || g.Speed < 0 || !float.IsFinite(g.Offset) || g.Offset < 0
                || !float.IsFinite(g.Color.X) || !float.IsFinite(g.Color.Y) || !float.IsFinite(g.Color.Z)
                || !float.IsFinite(g.Intensity) || g.Intensity < 0
                || g.Cells.Select(c => (c.Row, c.Col)).Distinct().Count() != g.Cells.Count
                || g.Cells.Any(c => c.Row < 0 || c.Row > 10000 || c.Col < 0 || c.Col > 10000
                    || c.Host == null || c.Light == null || c.Host.VehicleId != g.VehicleId || c.Light.VehicleId != g.VehicleId)
                || g.Active.Any(p => p.x < 0 || p.x > 10000 || p.y < 0 || p.y > 10000)
                || g.ScrollPixels.Any(p => p.x < 0 || p.x > 1000000 || p.y < 0 || p.y > 10000))
                throw new InvalidOperationException("Invalid Shiny cells, appearance or scroll settings.");
        }
    }

    private void RestoreGrids(ShinySaveState state, SaveRestoreContext context)
    {
        ShinyPatchState.RenderShinyParts = state.RenderParts;
        var claimed = new HashSet<Part>();
        foreach (var saved in state.Grids)
        {
            try
            {
                var vehicle = VehicleProvider.FindVehicle(saved.VehicleId);
                context.Require(vehicle != null, "Grid vessel is unavailable.");
                var cells = new List<ShinyPixelCell>();
                var owned = new List<Part>();
                foreach (var c in saved.Cells)
                {
                    var host = c.Host.Resolve(); var light = c.Light.Resolve();
                    context.Require(host != null && light != null && !owned.Contains(host!) && !claimed.Contains(host!)
                        && LightController.HasLights(light!.Template), "Grid light parts are missing or duplicated.");
                    cells.Add(new(c.Row, c.Col, host!, light!)); owned.Add(host!);
                }
                var grid = new ShinyBuiltGrid(ShinyPixelGrid.FromSavedCells(cells), saved.Owned ? owned : new());
                var restored = ShinyGridManager.Register(vehicle!, saved.Name, grid, saved.Color, saved.Intensity);
                claimed.UnionWith(owned);
                // Native switches retain their saved state; restore only the manager metadata.
                foreach (var pixel in saved.Active) restored.ActivePixels.Add((pixel.y, pixel.x));
                if (saved.PendingDestroy) ScheduleDestroy(restored);
                else if (saved.Scrolling && saved.ScrollPixels.Length > 0)
                {
                    restored.Scroll.Start(restored, saved.ScrollPixels, saved.Speed);
                    restored.Scroll.RestoreOffset(saved.Offset);
                }
            }
            catch (Exception ex) { context.Warn($"{saved.VehicleId}/{saved.Name}: {ex.Message}"); }
        }
    }
}

public static partial class ShinyGridManager
{
    private static readonly HashSet<Part> PixelParts = new();
    internal static bool IsPixelPart(Part part) => part.Id.StartsWith("shiny_", StringComparison.Ordinal) || PixelParts.Contains(part);
    private static void RebuildMembership()
    {
        PixelParts.Clear();
        foreach (var grid in _grids.Values)
            foreach (var cell in grid.ShinyGrid.Grid.Cells.Values) PixelParts.Add(cell.HostPart);
    }
}
