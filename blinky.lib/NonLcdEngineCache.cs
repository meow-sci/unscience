using System;
using System.Collections.Generic;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.BlinkyLib;

/// <summary>
/// Lazily scans and caches all EngineControllers on a vehicle that are NOT part of any LCD pixel grid.
/// Use <see cref="GetOrScan"/> for lazy initialization, and <see cref="Invalidate"/> when grids change.
/// </summary>
public static class NonLcdEngineCache
{
    private static readonly Dictionary<string, EngineController[]> _cache = new();

    /// <summary>
    /// Returns the cached non-LCD engine controllers for the vehicle, scanning lazily on first access.
    /// </summary>
    public static EngineController[] GetOrScan(Vehicle vehicle)
    {
        if (_cache.TryGetValue(vehicle.Id, out var cached))
            return cached;

        var result = ScanNonLcdEngines(vehicle);
        _cache[vehicle.Id] = result;
        return result;
    }

    /// <summary>Returns true if any cached non-LCD engine on this vehicle is currently active.</summary>
    public static bool AnyActive(Vehicle vehicle)
    {
        var engines = GetOrScan(vehicle);
        for (int i = 0; i < engines.Length; i++)
        {
            if (engines[i].IsActive)
                return true;
        }
        return false;
    }

    /// <summary>Deactivates all cached non-LCD engines on the vehicle.</summary>
    public static void DeactivateAll(Vehicle vehicle)
    {
        var engines = GetOrScan(vehicle);
        for (int i = 0; i < engines.Length; i++)
            engines[i].SetIsActive(null, false);
    }

    /// <summary>Removes the cached entry for a vehicle so it will be re-scanned on next access.</summary>
    public static void Invalidate(string vehicleId)
    {
        if (_cache.Remove(vehicleId))
            Console.WriteLine($"blinky: invalidated non-LCD engine cache for '{vehicleId}'");
    }

    /// <summary>Clears the entire cache.</summary>
    public static void Clear()
    {
        _cache.Clear();
    }

    private static EngineController[] ScanNonLcdEngines(Vehicle vehicle)
    {
        var nonLcd = new List<EngineController>();

        foreach (var part in PartHelpers.GetAllParts(vehicle))
        {
            // Skip parts that belong to any LCD pixel grid
            if (BlinkyGridManager.IsPixelPart(part.FullPart))
                continue;

            var controllers = part.SubtreeModules.Get<EngineController>();
            for (int i = 0; i < controllers.Length; i++)
                nonLcd.Add(controllers[i]);
        }

        Console.WriteLine($"blinky: scanned vehicle '{vehicle.Id}' — found {nonLcd.Count} non-LCD engine controller(s)");
        return nonLcd.ToArray();
    }
}
