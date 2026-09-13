using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.BlinkyLib;

/// <summary>
/// Per-vehicle grid state managed by <see cref="BlinkyGridManager"/>.
/// </summary>
public class GridState
{
    public string VehicleId { get; }
    public string GridName { get; }
    public Vehicle Vehicle { get; }
    public BlinkyPixelGrid BlinkyGrid { get; }
    public ScrollAnimation Scroll { get; } = new();

    /// <summary>
    /// Tracks which pixels are currently "on" for intelligent static display diffing.
    /// </summary>
    internal HashSet<(int row, int col)> ActivePixels { get; } = new();

    public GridState(string vehicleId, string gridName, Vehicle vehicle, BlinkyPixelGrid grid)
    {
        VehicleId = vehicleId;
        GridName = gridName;
        Vehicle = vehicle;
        BlinkyGrid = grid;
    }
}

/// <summary>
/// Static singleton that manages per-vehicle named LCD grids and exposes scroll, static display, and off operations.
/// Shared control surface for the blinky mod UI and reusable callers.
/// </summary>
public static partial class BlinkyGridManager
{
    private static readonly Dictionary<(string vehicleId, string gridName), GridState> _grids = new();

    /// <summary>All currently registered grid states, keyed by (vehicleId, gridName).</summary>
    public static IReadOnlyDictionary<(string vehicleId, string gridName), GridState> Grids => _grids;

    // ── Registration ─────────────────────────────────────────────────────────

    /// <summary>Registers a named grid for the given vehicle. Replaces any existing grid with the same name.</summary>
    public static GridState Register(Vehicle vehicle, string gridName, BlinkyPixelGrid grid)
    {
        var id = vehicle.Id;
        var key = (id, gridName);
        if (_grids.ContainsKey(key))
            Console.WriteLine($"blinky: replacing existing grid '{gridName}' for vehicle '{id}'");

        var state = new GridState(id, gridName, vehicle, grid);
        _grids[key] = state;
        RebuildMembership();
        NonLcdEngineCache.Invalidate(id);
        Console.WriteLine($"blinky: registered grid '{gridName}' for vehicle '{id}' ({grid.Grid.Cols}x{grid.Grid.Rows})");
        return state;
    }

    /// <summary>Unregisters the named grid for the given vehicle ID. Stops any running scroll.</summary>
    public static void Unregister(string vehicleId, string gridName)
    {
        var key = (vehicleId, gridName);
        if (_grids.TryGetValue(key, out var state))
        {
            state.Scroll.Stop();
            _grids.Remove(key);
            RebuildMembership();
            NonLcdEngineCache.Invalidate(vehicleId);
            Console.WriteLine($"blinky: unregistered grid '{gridName}' for vehicle '{vehicleId}'");
        }
    }

    /// <summary>Gets the grid state for a specific named grid, or null if not registered.</summary>
    public static GridState? Get(string vehicleId, string gridName)
    {
        _grids.TryGetValue((vehicleId, gridName), out var state);
        return state;
    }

    /// <summary>Returns all grids registered for the given vehicle ID.</summary>
    public static IEnumerable<GridState> GetAllForVehicle(string vehicleId)
    {
        foreach (var state in _grids.Values)
            if (state.VehicleId == vehicleId)
                yield return state;
    }

    /// <summary>Clears all registered grids.</summary>
    public static void Clear()
    {
        foreach (var state in _grids.Values)
            state.Scroll.Stop();
        _grids.Clear();
        RebuildMembership();
        NonLcdEngineCache.Clear();
    }

    // ── Scroll ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a scrolling animation on the named grid with the supplied pixel data.
    /// Stops any existing scroll first and turns off all pixels before starting.
    /// </summary>
    public static bool StartScroll(string vehicleId, string gridName, (int x, int y)[] pixels, float speed)
    {
        var state = Get(vehicleId, gridName);
        if (state == null) return false;

        state.Scroll.Stop();
        TurnOffAllPixels(state);
        EnsureVehicleIgnited(state.Vehicle);

        state.Scroll.Start(state.BlinkyGrid.Grid, pixels, speed);
        return true;
    }

    /// <summary>
    /// Starts a scrolling animation using the built-in default pixel data.
    /// </summary>
    public static bool StartBuiltInScroll(string vehicleId, string gridName, float speed)
    {
        var state = Get(vehicleId, gridName);
        if (state == null) return false;

        state.Scroll.Stop();
        TurnOffAllPixels(state);
        EnsureVehicleIgnited(state.Vehicle);

        state.Scroll.StartBuiltIn(state.BlinkyGrid.Grid, speed);
        return true;
    }

    /// <summary>Stops any running scroll on the named grid.</summary>
    public static bool StopScroll(string vehicleId, string gridName)
    {
        var state = Get(vehicleId, gridName);
        if (state == null) return false;

        state.Scroll.Stop();
        return true;
    }

    // ── Static Display ───────────────────────────────────────────────────────

    /// <summary>
    /// Displays a static set of pixels on the named grid.
    /// </summary>
    public static bool DisplayStatic(string vehicleId, string gridName, (int x, int y)[] pixels, bool reset)
    {
        var state = Get(vehicleId, gridName);
        if (state == null) return false;

        state.Scroll.Stop();
        EnsureVehicleIgnited(state.Vehicle);

        var grid = state.BlinkyGrid.Grid;
        var newPixels = new HashSet<(int row, int col)>();
        foreach (var (x, y) in pixels)
            newPixels.Add((y, x)); // (x=col, y=row) → (row, col) for grid lookup

        if (reset)
        {
            foreach (var pos in state.ActivePixels)
            {
                if (!newPixels.Contains(pos))
                    SetPixel(grid, pos.row, pos.col, false);
            }

            foreach (var pos in newPixels)
            {
                if (!state.ActivePixels.Contains(pos))
                    SetPixel(grid, pos.row, pos.col, true);
            }

            state.ActivePixels.Clear();
            foreach (var pos in newPixels)
                state.ActivePixels.Add(pos);
        }
        else
        {
            foreach (var pos in newPixels)
            {
                SetPixel(grid, pos.row, pos.col, true);
                state.ActivePixels.Add(pos);
            }
        }

        return true;
    }

    // ── Off ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Turns off all pixels on the named grid and stops any running scroll.
    /// </summary>
    public static bool TurnOff(string vehicleId, string gridName)
    {
        var state = Get(vehicleId, gridName);
        if (state == null) return false;

        state.Scroll.Stop();
        TurnOffAllPixels(state);
        return true;
    }

    // ── Pattern Application ──────────────────────────────────────────────────

    /// <summary>Applies a pattern function to all pixels on the named grid.</summary>
    public static bool ApplyPattern(string vehicleId, string gridName, Func<(int row, int col), bool> selector)
    {
        var state = Get(vehicleId, gridName);
        if (state == null) return false;

        state.Scroll.Stop();
        state.ActivePixels.Clear();
        EnsureVehicleIgnited(state.Vehicle);

        foreach (var (key, controllers) in state.BlinkyGrid.Grid.Engines)
        {
            bool on = selector(key);
            for (int i = 0; i < controllers.Length; i++)
                controllers[i].SetIsActive(null, on);
            if (on)
                state.ActivePixels.Add(key);
        }

        return true;
    }

    // ── Tick ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls Update on all active scroll animations. Call this every frame from the mod.
    /// </summary>
    public static void TickAll(double dt)
    {
        foreach (var state in _grids.Values)
        {
            if (state.Scroll.IsActive && state.BlinkyGrid.Grid.Cols > 0)
                state.Scroll.Update(dt);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void SetPixel(PixelGrid grid, int row, int col, bool on)
    {
        if (grid.Engines.TryGetValue((row, col), out var controllers))
        {
            for (int i = 0; i < controllers.Length; i++)
                controllers[i].SetIsActive(null, on);
        }
    }

    private static void EnsureVehicleIgnited(Vehicle vehicle)
    {
        vehicle.SetEnum(VehicleEngine.MainIgnite);
    }

    private static void TurnOffAllPixels(GridState state)
    {
        foreach (var (_, controllers) in state.BlinkyGrid.Grid.Engines)
        {
            for (int i = 0; i < controllers.Length; i++)
                controllers[i].SetIsActive(null, false);
        }
        state.ActivePixels.Clear();
    }

    // ── Global Scan ─────────────────────────────────────────────────────────

    /// <summary>
    /// Scans ALL vehicles in the current system for blinky grids.
    /// Discovers grids by parsing pixel_* part IDs on every vehicle.
    /// Returns the total number of newly discovered grids and their names.
    /// </summary>
    public static (int discovered, List<string> names) ScanAllVehicles()
    {
        var vehicles = VehicleProvider.GetAllVehicles();
        var allNames = new List<string>();
        int total = 0;

        foreach (var vehicle in vehicles)
        {
            try
            {
                var discovered = PixelGrid.ScanAllFromVehicle(vehicle);
                foreach (var (gridName, pixelGrid) in discovered)
                {
                    pixelGrid.RefreshEngineControllers();
                    var blinkyGrid = new BlinkyPixelGrid(pixelGrid, new List<Part>());
                    Register(vehicle, gridName, blinkyGrid);
                    allNames.Add($"{gridName} on {vehicle.Id}");
                    total++;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"blinky: ScanAllVehicles error on vehicle '{vehicle.Id}': {ex.Message}");
            }
        }

        Console.WriteLine($"blinky: ScanAllVehicles complete — {total} grid(s) across {vehicles.Count} vehicle(s)");
        return (total, allNames);
    }
}
