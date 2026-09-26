using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MeowSci.PebblesLib;

/// <summary>
/// Per-body memory of removed and displaced clutter for every grid (ecotype name + exact separation)
/// seen by a live override. Subcell keys are only meaningful on their own grid, so state is never
/// reinterpreted across spacings.
/// Masks merge with bitwise AND: a removed instance is never resurrected. Displaced records are
/// replaced by an authoritative grid (a placement Pebbles replayed this memory into, whose records
/// have evolved since) and otherwise merged, so a fresh native placement (for example after renderer
/// recreation) cannot erase remembered records whose mask bits stay cleared.
/// Every remembered displaced record keeps its slot bit cleared, so the resting instance is not drawn twice.
/// </summary>
public sealed class ClutterGridMemory
{
    private sealed class Grid(string ecotype, double separation)
    {
        public readonly string Ecotype = ecotype;
        public readonly double Separation = separation;
        public readonly Dictionary<(int X, int Y, int Face), uint[]> Masks = new();
        public readonly Dictionary<(int X, int Y, int Face, uint SubCell), ClutterDisplacedState> Displaced = new();
    }

    private readonly Dictionary<string, Grid> _grids = new(StringComparer.Ordinal);

    public static string Key(string ecotype, double separation) => ecotype + "|" + separation.ToString("R", CultureInfo.InvariantCulture);
    public IReadOnlyCollection<string> Keys => _grids.Keys;
    public bool Contains(string key) => _grids.ContainsKey(key);

    public void Remember(ClutterGridState state, bool authoritative)
    {
        var key = state.GridKey();
        if (!_grids.TryGetValue(key, out var grid)) _grids[key] = grid = new Grid(state.Ecotype, state.Separation);
        foreach (var cell in state.Cells) And(grid, (cell.X, cell.Y, cell.Face), cell.Words);
        if (authoritative) grid.Displaced.Clear();
        foreach (var d in state.Displaced)
        {
            grid.Displaced[(d.X, d.Y, d.Face, d.SubCell)] = Copy(d);
            if (d.SubCell >= ClutterCellMask.WordCount * 32) continue;
            var words = ClutterCellMask.AllIncluded();
            words[d.SubCell / 32] &= ~(1u << (int)(d.SubCell % 32));
            And(grid, (d.X, d.Y, d.Face), words);
        }
    }

    /// <summary>Adds saved grids whose keys are not already known (known keys are newer, live state). Returns the keys added.</summary>
    public List<string> Seed(IEnumerable<ClutterGridState> saved)
    {
        var added = new List<string>();
        foreach (var state in saved)
        {
            var key = state.GridKey();
            if (_grids.ContainsKey(key)) continue;
            Remember(state, authoritative: true);
            added.Add(key);
        }
        return added;
    }

    /// <summary>A detached, deterministically ordered snapshot, or null for a grid never seen.</summary>
    public ClutterGridState? Get(string key)
    {
        if (!_grids.TryGetValue(key, out var grid)) return null;
        return new ClutterGridState
        {
            Ecotype = grid.Ecotype, Separation = grid.Separation,
            Cells = grid.Masks.OrderBy(p => p.Key).Select(p => new ClutterCellMask { X = p.Key.X, Y = p.Key.Y, Face = p.Key.Face, Words = (uint[])p.Value.Clone() }).ToList(),
            Displaced = grid.Displaced.OrderBy(p => p.Key).Select(p => Copy(p.Value)).ToList()
        };
    }

    /// <summary>Grids with content, excluding keys another owner persists (stock grids travel in native saves).</summary>
    public List<ClutterGridState> Export(IReadOnlySet<string> excludedKeys)
        => _grids.Keys.Where(k => !excludedKeys.Contains(k)).Order(StringComparer.Ordinal)
            .Select(k => Get(k)!).Where(g => g.HasContent()).ToList();

    private static void And(Grid grid, (int X, int Y, int Face) cell, uint[] words)
    {
        if (!grid.Masks.TryGetValue(cell, out var mask)) grid.Masks[cell] = mask = ClutterCellMask.AllIncluded();
        for (var i = 0; i < ClutterCellMask.WordCount; i++) mask[i] &= words[i];
    }

    private static ClutterDisplacedState Copy(ClutterDisplacedState d) => new()
    {
        X = d.X, Y = d.Y, Face = d.Face, SubCell = d.SubCell, ObjectId = d.ObjectId, ScaleId = d.ScaleId, PackedColor = d.PackedColor, Settled = d.Settled,
        Position = (double[])d.Position.Clone(), Rotation = (double[])d.Rotation.Clone(), RestPosition = (double[])d.RestPosition.Clone(),
        RestRotation = (double[])d.RestRotation.Clone(), Velocity = (double[])d.Velocity.Clone(), AngularVelocity = (double[])d.AngularVelocity.Clone()
    };
}
