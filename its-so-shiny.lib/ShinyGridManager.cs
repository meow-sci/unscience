using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.ItsSoShinyLib;

public sealed class ShinyGridState
{
    public string VehicleId { get; }
    public string GridName { get; }
    public Vehicle Vehicle { get; }
    public ShinyBuiltGrid ShinyGrid { get; }
    public ShinyScrollAnimation Scroll { get; } = new();
    public HashSet<(int row, int col)> ActivePixels { get; } = new();
    public float3 Color { get; set; }
    public float Intensity { get; set; }

    public ShinyGridState(string vehicleId, string gridName, Vehicle vehicle, ShinyBuiltGrid grid, float3 color, float intensity)
    {
        VehicleId = vehicleId;
        GridName = gridName;
        Vehicle = vehicle;
        ShinyGrid = grid;
        Color = color;
        Intensity = intensity;
    }
}

public static partial class ShinyGridManager
{
    private static readonly Dictionary<(string vehicleId, string gridName), ShinyGridState> _grids = new();

    public static IReadOnlyDictionary<(string vehicleId, string gridName), ShinyGridState> Grids => _grids;

    public static ShinyGridState Register(Vehicle vehicle, string gridName, ShinyBuiltGrid grid, float3 color, float intensity)
    {
        var key = (vehicle.Id, gridName);
        var state = new ShinyGridState(vehicle.Id, gridName, vehicle, grid, color, intensity);
        _grids[key] = state;
        RebuildMembership();
        ApplyAppearance(state, color, intensity);
        Console.WriteLine($"its-so-shiny: registered grid '{gridName}' for vehicle '{vehicle.Id}' ({grid.Grid.Cols}x{grid.Grid.Rows})");
        return state;
    }

    public static void Unregister(string vehicleId, string gridName)
    {
        if (_grids.TryGetValue((vehicleId, gridName), out var state))
            state.Scroll.Stop();
        _grids.Remove((vehicleId, gridName));
        RebuildMembership();
    }

    public static ShinyGridState? Get(string vehicleId, string gridName)
    {
        _grids.TryGetValue((vehicleId, gridName), out var state);
        return state;
    }

    public static void Clear()
    {
        foreach (var state in _grids.Values)
            state.Scroll.Stop();
        _grids.Clear();
        RebuildMembership();
    }

    public static bool SetAppearance(string vehicleId, string gridName, float3 color, float intensity)
    {
        var state = Get(vehicleId, gridName);
        if (state == null) return false;
        ApplyAppearance(state, color, intensity);
        return true;
    }

    public static bool ApplyPattern(string vehicleId, string gridName, Func<(int row, int col), bool> selector)
    {
        var state = Get(vehicleId, gridName);
        if (state == null) return false;

        state.Scroll.Stop();
        state.ActivePixels.Clear();
        foreach (var (key, cell) in state.ShinyGrid.Grid.Cells)
        {
            bool on = selector(key);
            cell.SetEnabled(on, state.Intensity);
            if (on) state.ActivePixels.Add(key);
        }
        return true;
    }

    public static bool DisplayStatic(string vehicleId, string gridName, (int x, int y)[] pixels, bool reset)
    {
        var state = Get(vehicleId, gridName);
        if (state == null) return false;

        state.Scroll.Stop();
        var newPixels = new HashSet<(int row, int col)>();
        foreach (var (x, y) in pixels)
            newPixels.Add((y, x));

        if (reset)
        {
            foreach (var pos in state.ActivePixels)
                if (!newPixels.Contains(pos)) SetPixel(state, pos.row, pos.col, false);
            foreach (var pos in newPixels)
                if (!state.ActivePixels.Contains(pos)) SetPixel(state, pos.row, pos.col, true);
            state.ActivePixels.Clear();
        }
        else
        {
            foreach (var pos in newPixels)
                SetPixel(state, pos.row, pos.col, true);
        }

        foreach (var pos in newPixels)
            state.ActivePixels.Add(pos);
        return true;
    }

    public static bool TurnOff(string vehicleId, string gridName)
    {
        var state = Get(vehicleId, gridName);
        if (state == null) return false;
        state.Scroll.Stop();
        TurnOffAllPixels(state);
        return true;
    }

    public static bool StartScroll(string vehicleId, string gridName, (int x, int y)[] pixels, float speed)
    {
        var state = Get(vehicleId, gridName);
        if (state == null) return false;
        TurnOffAllPixels(state);
        state.Scroll.Start(state, pixels, speed);
        return true;
    }

    public static bool StopScroll(string vehicleId, string gridName)
    {
        var state = Get(vehicleId, gridName);
        if (state == null) return false;
        state.Scroll.Stop();
        return true;
    }

    public static void TickAll(double dt)
    {
        foreach (var state in _grids.Values)
            if (state.Scroll.IsActive)
                state.Scroll.Update(dt);
    }

    public static (int discovered, List<string> names) ScanAllVehicles(float3 defaultColor, float defaultIntensity)
    {
        var vehicles = VehicleProvider.GetAllVehicles();
        var names = new List<string>();
        int total = 0;

        foreach (var vehicle in vehicles)
        {
            try
            {
                var discovered = ShinyPixelGrid.ScanAllFromVehicle(vehicle);
                foreach (var (gridName, pixelGrid) in discovered)
                {
                    var builtGrid = new ShinyBuiltGrid(pixelGrid, new List<Part>());
                    Register(vehicle, gridName, builtGrid, defaultColor, defaultIntensity);
                    names.Add($"{gridName} on {vehicle.Id}");
                    total++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"its-so-shiny: scan error on vehicle '{vehicle.Id}': {ex.Message}");
            }
        }

        return (total, names);
    }

    private static void ApplyAppearance(ShinyGridState state, float3 color, float intensity)
    {
        state.Color = color;
        state.Intensity = Math.Clamp(intensity, 0f, 25f);

        foreach (var template in GetDistinctLightTemplates(state))
            ApplyAppearance(template, state.Color, state.Intensity);
    }

    private static IEnumerable<PartTemplate> GetDistinctLightTemplates(ShinyGridState state)
    {
        var templates = new List<PartTemplate>();
        foreach (var cell in state.ShinyGrid.Grid.Cells.Values)
        {
            var template = cell.LightPart.Template;
            if (template == null || ContainsTemplate(templates, template))
                continue;

            templates.Add(template);
        }

        return templates;
    }

    private static bool ContainsTemplate(List<PartTemplate> templates, PartTemplate candidate)
    {
        foreach (var template in templates)
        {
            if (ReferenceEquals(template, candidate))
                return true;
        }

        return false;
    }

    private static void ApplyAppearance(PartTemplate template, float3 color, float intensity)
    {
        var lights = MeowSci.ZippoLib.LightController.GetLightComponents(template);
        if (lights.Count == 0)
            return;

        MeowSci.ZippoLib.LightController.WriteColor(lights, color);
        MeowSci.ZippoLib.LightController.WriteIntensity(lights, intensity);
    }

    private static void SetPixel(ShinyGridState state, int row, int col, bool on)
    {
        if (state.ShinyGrid.Grid.Cells.TryGetValue((row, col), out var cell))
            cell.SetEnabled(on, state.Intensity);
    }

    private static void TurnOffAllPixels(ShinyGridState state)
    {
        foreach (var cell in state.ShinyGrid.Grid.Cells.Values)
            cell.SetEnabled(false, state.Intensity);
        state.ActivePixels.Clear();
    }
}