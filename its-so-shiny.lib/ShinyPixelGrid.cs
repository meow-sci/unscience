using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.ZippoLib;

namespace MeowSci.ItsSoShinyLib;

public sealed class ShinyPixelGrid
{
    private readonly Dictionary<(int row, int col), ShinyPixelCell> _cells = new();

    private ShinyPixelGrid() { }

    public int Count => _cells.Count;
    public int Rows { get; private set; }
    public int Cols { get; private set; }
    public IReadOnlyDictionary<(int row, int col), ShinyPixelCell> Cells => _cells;

    /// <summary>Rebind native-loaded cells without depending on ordinary part ids, which KSA omits.</summary>
    internal static ShinyPixelGrid FromSavedCells(IEnumerable<ShinyPixelCell> cells)
    {
        var grid = new ShinyPixelGrid();
        foreach (var cell in cells) grid._cells.Add((cell.Row, cell.Col), cell);
        grid.RecomputeSize();
        return grid;
    }

    public static ShinyPixelGrid CreateFromParts(IEnumerable<Part> parts, string gridName)
    {
        var grid = new ShinyPixelGrid();

        foreach (var part in parts)
        {
            if (!TryParseCellId(part.Id, gridName, out int row, out int col))
                continue;

            var lightPart = FindLightPart(part);
            if (lightPart == null)
                continue;

            grid._cells[(row, col)] = new ShinyPixelCell(row, col, part, lightPart);
        }

        grid.RecomputeSize();
        Console.WriteLine($"its-so-shiny: created {grid._cells.Count} light pixels for grid '{gridName}' from new parts");
        return grid;
    }

    public static ShinyPixelGrid ScanFromVehicle(Vehicle vehicle, string gridName)
    {
        var grid = new ShinyPixelGrid();

        foreach (var part in PartHelpers.GetAllParts(vehicle))
        {
            if (!TryParseCellId(part.Id, gridName, out int row, out int col))
                continue;

            var lightPart = FindLightPart(part);
            if (lightPart == null)
                continue;

            grid._cells[(row, col)] = new ShinyPixelCell(row, col, part, lightPart);
        }

        grid.RecomputeSize();
        Console.WriteLine($"its-so-shiny: found {grid._cells.Count} light pixels for grid '{gridName}'");
        return grid;
    }

    public static Dictionary<string, ShinyPixelGrid> ScanAllFromVehicle(Vehicle vehicle)
    {
        var byName = new Dictionary<string, List<(int row, int col, Part host, Part light)>>();

        foreach (var part in PartHelpers.GetAllParts(vehicle))
        {
            if (!TryParseAnyCellId(part.Id, out string gridName, out int row, out int col))
                continue;

            var lightPart = FindLightPart(part);
            if (lightPart == null)
                continue;

            if (!byName.TryGetValue(gridName, out var cells))
            {
                cells = new List<(int row, int col, Part host, Part light)>();
                byName[gridName] = cells;
            }
            cells.Add((row, col, part, lightPart));
        }

        var result = new Dictionary<string, ShinyPixelGrid>();
        foreach (var (name, cells) in byName)
        {
            var grid = new ShinyPixelGrid();
            foreach (var cell in cells)
                grid._cells[(cell.row, cell.col)] = new ShinyPixelCell(cell.row, cell.col, cell.host, cell.light);
            grid.RecomputeSize();
            result[name] = grid;
            Console.WriteLine($"its-so-shiny: ScanAll found grid '{name}': {grid.Cols}x{grid.Rows} ({grid.Count} pixels)");
        }

        return result;
    }

    public static bool IsValidGridName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (char c in name)
            if (!char.IsLetterOrDigit(c) && c != '-') return false;
        return true;
    }

    public static string MakeCellId(string gridName, int row, int col) => $"shiny_{gridName}_{row}_{col}";

    private void RecomputeSize()
    {
        if (_cells.Count == 0)
        {
            Rows = 0;
            Cols = 0;
            return;
        }

        Rows = _cells.Keys.Max(k => k.row) + 1;
        Cols = _cells.Keys.Max(k => k.col) + 1;
    }

    private static bool TryParseCellId(string partId, string expectedGridName, out int row, out int col)
    {
        row = 0;
        col = 0;
        return TryParseAnyCellId(partId, out string gridName, out row, out col) && gridName == expectedGridName;
    }

    private static bool TryParseAnyCellId(string partId, out string gridName, out int row, out int col)
    {
        gridName = "";
        row = 0;
        col = 0;

        if (!partId.StartsWith("shiny_", StringComparison.Ordinal))
            return false;

        var segments = partId.Split('_');
        if (segments.Length != 4)
            return false;

        gridName = segments[1];
        return int.TryParse(segments[2], out row) && int.TryParse(segments[3], out col);
    }

    private static Part? FindLightPart(Part hostPart)
    {
        if (hostPart.Template != null && LightController.HasLights(hostPart.Template))
            return hostPart;

        foreach (var subPart in hostPart.SubParts)
        {
            var found = FindLightPart(subPart);
            if (found != null)
                return found;
        }

        return null;
    }
}
