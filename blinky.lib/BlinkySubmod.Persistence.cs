using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.BlinkyLib;

public sealed class BlinkySaveState
{
    public bool RenderParts { get; set; }
    public List<SavedBlinkyGrid> Grids { get; set; } = new();
}

public sealed class SavedBlinkyGrid
{
    public string VehicleId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Owned { get; set; }
    public bool PendingDestroy { get; set; }
    public List<SavedBlinkyCell> Cells { get; set; } = new();
    public (int x, int y)[] Active { get; set; } = Array.Empty<(int, int)>();
    public (int x, int y)[] ScrollPixels { get; set; } = Array.Empty<(int, int)>();
    public bool Scrolling { get; set; }
    public float Speed { get; set; }
    public float Offset { get; set; }
}

public sealed class SavedBlinkyCell
{
    public int Row { get; set; }
    public int Col { get; set; }
    public SavedPartReference A { get; set; } = new();
    public SavedPartReference B { get; set; } = new();
}

public sealed partial class BlinkySubmod : ISaveParticipantSource
{
    public IEnumerable<ISaveParticipant> SaveParticipants
    {
        get { yield return new SaveParticipant<BlinkySaveState>("blinky", CaptureGrids,
            ResetSavedGrids, RestoreGrids, 30, ValidateGrids); }
    }

    private BlinkySaveState CaptureGrids() => new()
    {
        RenderParts = BlinkyPatchState.RenderPixelParts,
        Grids = BlinkyGridManager.Grids.Values.Select(g => new SavedBlinkyGrid
        {
            VehicleId = g.VehicleId, Name = g.GridName, PendingDestroy = _pendingDestroy.Contains((g.VehicleId, g.GridName)), Owned = g.BlinkyGrid.IsOwned,
            Cells = g.BlinkyGrid.Grid.Grid.Select(cell => new SavedBlinkyCell
            {
                Row = cell.Key.row, Col = cell.Key.col,
                A = SavedPartReference.Capture(g.Vehicle, cell.Value.a),
                B = SavedPartReference.Capture(g.Vehicle, cell.Value.b)
            }).ToList(),
            Active = g.ActivePixels.Select(p => (p.col, p.row)).ToArray(),
            Scrolling = g.Scroll.IsActive, ScrollPixels = g.Scroll.SavedPixels,
            Speed = g.Scroll.ScrollSpeed, Offset = g.Scroll.SavedOffset
        }).ToList()
    };

    private void ResetSavedGrids()
    {
        BlinkyGridManager.Clear(); BlinkyPatchState.RenderPixelParts = false;
        _deferredActions.Clear(); _pendingDestroy.Clear(); _deferredTimer = 0;
        _selectedVehicleIndex = -1;
    }

    private static void ValidateGrids(BlinkySaveState state)
    {
        if (state.Grids == null || state.Grids.Count > 10000
            || state.Grids.Select(g => (g.VehicleId, g.Name)).Distinct().Count() != state.Grids.Count)
            throw new InvalidOperationException("Invalid saved Blinky grid list.");
        foreach (var g in state.Grids)
        {
            if (!PixelGrid.IsValidGridName(g.Name) || g.Cells == null || g.Cells.Count > 100000
                || g.Active == null || g.Active.Length > 1000000 || g.ScrollPixels == null || g.ScrollPixels.Length > 1000000
                || !float.IsFinite(g.Speed) || g.Speed < 0 || !float.IsFinite(g.Offset) || g.Offset < 0
                || g.Cells.Select(c => (c.Row, c.Col)).Distinct().Count() != g.Cells.Count
                || g.Cells.Any(c => c.Row < 0 || c.Row > 10000 || c.Col < 0 || c.Col > 10000
                    || c.A == null || c.B == null || c.A.VehicleId != g.VehicleId || c.B.VehicleId != g.VehicleId)
                || g.Active.Any(p => p.x < 0 || p.x > 10000 || p.y < 0 || p.y > 10000)
                || g.ScrollPixels.Any(p => p.x < 0 || p.x > 1000000 || p.y < 0 || p.y > 10000))
                throw new InvalidOperationException("Invalid Blinky cells or scroll settings.");
        }
    }

    private void RestoreGrids(BlinkySaveState state, SaveRestoreContext context)
    {
        BlinkyPatchState.RenderPixelParts = state.RenderParts;
        var claimed = new HashSet<Part>();
        foreach (var saved in state.Grids)
        {
            try
            {
                var vehicle = VehicleProvider.FindVehicle(saved.VehicleId);
                context.Require(vehicle != null, "Grid vessel is unavailable.");
                var cells = new Dictionary<(int row, int col), (Part a, Part b)>();
                var owned = new List<Part>();
                foreach (var c in saved.Cells)
                {
                    var a = c.A.Resolve(); var b = c.B.Resolve();
                    context.Require(a != null && b != null && a != b && !owned.Contains(a!) && !owned.Contains(b!)
                        && !claimed.Contains(a!) && !claimed.Contains(b!), "Grid cell parts are missing or duplicated.");
                    cells.Add((c.Row, c.Col), (a!, b!)); owned.Add(a!); owned.Add(b!);
                }
                var grid = new BlinkyPixelGrid(PixelGrid.BuildFromPartGroups(cells), saved.Owned ? owned : new());
                var restored = BlinkyGridManager.Register(vehicle!, saved.Name, grid);
                claimed.UnionWith(owned);
                LcdGridBuilder.RepairFuelFeeds(vehicle!, grid);
                // Native module save data already carries engine activity/vehicle ignition.
                foreach (var pixel in saved.Active) restored.ActivePixels.Add((pixel.y, pixel.x));
                if (saved.PendingDestroy) ScheduleDestroy(restored);
                else if (saved.Scrolling && saved.ScrollPixels.Length > 0)
                {
                    restored.Scroll.Start(grid.Grid, saved.ScrollPixels, saved.Speed);
                    restored.Scroll.RestoreOffset(saved.Offset);
                }
            }
            catch (Exception ex) { context.Warn($"{saved.VehicleId}/{saved.Name}: {ex.Message}"); }
        }
    }
}

public static partial class BlinkyGridManager
{
    private static readonly HashSet<Part> PixelParts = new();
    internal static bool IsPixelPart(Part part) => part.Id.StartsWith("pixel_", StringComparison.Ordinal) || PixelParts.Contains(part);
    private static void RebuildMembership()
    {
        PixelParts.Clear();
        foreach (var grid in _grids.Values)
            foreach (var cell in grid.BlinkyGrid.Grid.Grid.Values) { PixelParts.Add(cell.a); PixelParts.Add(cell.b); }
    }
}
