using System;
using System.Collections.Generic;
using System.Linq;

namespace MeowSci.PebblesLib;

/// <summary>One cube cell's native exclusion words: 256 candidate bits, a cleared bit is a removed instance.</summary>
public sealed class ClutterCellMask
{
    public const int WordCount = 8;
    public int X { get; set; }
    public int Y { get; set; }
    public int Face { get; set; }
    public uint[] Words { get; set; } = AllIncluded();
    public static uint[] AllIncluded() => Enumerable.Repeat(uint.MaxValue, WordCount).ToArray();
}

/// <summary>A native displaced-object record (KSA 5447+), addressed by the cell/subcell slot it left.</summary>
public sealed class ClutterDisplacedState
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Face { get; set; }
    public uint SubCell { get; set; }
    public uint ObjectId { get; set; }
    public uint ScaleId { get; set; }
    public uint PackedColor { get; set; }
    public bool Settled { get; set; }
    public double[] Position { get; set; } = new double[3];
    public double[] Rotation { get; set; } = [0, 0, 0, 1];
    public double[] RestPosition { get; set; } = new double[3];
    public double[] RestRotation { get; set; } = [0, 0, 0, 1];
    public double[] Velocity { get; set; } = new double[3];
    public double[] AngularVelocity { get; set; } = new double[3];
}

/// <summary>Removed and displaced clutter of one grid: an ecotype at one exact object separation.</summary>
public sealed class ClutterGridState
{
    public string Ecotype { get; set; } = "";
    public double Separation { get; set; }
    public List<ClutterCellMask> Cells { get; set; } = new();
    public List<ClutterDisplacedState> Displaced { get; set; } = new();
    public string GridKey() => ClutterGridMemory.Key(Ecotype, Separation);
    public bool HasContent() => Cells.Count != 0 || Displaced.Count != 0;
}

/// <summary>`pebbles.clutter-state` sidecar entry: override-grid clutter state that native saves cannot carry.</summary>
public sealed class ClutterBodyState
{
    public string BodyId { get; set; } = "";
    public List<ClutterGridState> Grids { get; set; } = new();
}

public static class ClutterGridValidation
{
    public const int MaxBodies = 256, MaxGridsPerBody = 64, MaxCellsPerGrid = 262_144, MaxDisplacedPerGrid = 65_536;
    public const int Faces = 6, SubCells = 256, Scales = 16;

    public static void Validate(ClutterBodyState[] bodies)
    {
        Require(bodies != null && bodies.Length <= MaxBodies, "Too many saved clutter-state bodies.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var body in bodies!)
        {
            Require(body != null && !string.IsNullOrWhiteSpace(body.BodyId) && ids.Add(body.BodyId), "Invalid or duplicate clutter-state body.");
            Require(body!.Grids != null && body.Grids.Count <= MaxGridsPerBody, $"Too many clutter grids on {body.BodyId}.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var grid in body.Grids!)
            {
                Grid(grid);
                Require(keys.Add(grid.GridKey()), $"Duplicate clutter grid {grid.Ecotype} on {body.BodyId}.");
            }
        }
    }

    public static void Grid(ClutterGridState grid)
    {
        Require(grid != null && !string.IsNullOrWhiteSpace(grid.Ecotype) && double.IsFinite(grid.Separation) && grid.Separation > 0, "Invalid clutter grid identity.");
        Require(grid!.Cells != null && grid.Cells.Count <= MaxCellsPerGrid && grid.Displaced != null && grid.Displaced.Count <= MaxDisplacedPerGrid, $"Clutter grid {grid.Ecotype} exceeds its state limits.");
        var cells = new HashSet<(int, int, int)>();
        foreach (var cell in grid.Cells!)
            Require(cell != null && Face(cell.Face) && cell.Words?.Length == ClutterCellMask.WordCount && cells.Add((cell.X, cell.Y, cell.Face)), $"Invalid or duplicate excluded cell in {grid.Ecotype}.");
        var slots = new HashSet<(int, int, int, uint)>();
        foreach (var d in grid.Displaced!)
            Require(d != null && Face(d.Face) && d.SubCell < SubCells && d.ScaleId < Scales && slots.Add((d.X, d.Y, d.Face, d.SubCell))
                && Finite(d.Position, 3) && Finite(d.Rotation, 4) && Finite(d.RestPosition, 3) && Finite(d.RestRotation, 4)
                && Finite(d.Velocity, 3) && Finite(d.AngularVelocity, 3), $"Invalid or duplicate displaced object in {grid.Ecotype}.");
    }

    private static bool Face(int face) => face is >= 0 and < Faces;
    private static bool Finite(double[]? values, int length) => values?.Length == length && values.All(double.IsFinite);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
