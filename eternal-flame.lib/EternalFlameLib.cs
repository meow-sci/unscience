using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.EternalFlameLib;

public sealed class MonitoredVehicle
{
    public string VehicleId { get; }
    public string DisplayName { get; }
    public bool RefillFuel { get; set; }
    public bool RefillElectricity { get; set; }

    public MonitoredVehicle(string vehicleId, string displayName)
    {
        VehicleId = vehicleId;
        DisplayName = displayName;
        RefillFuel = true;
        RefillElectricity = true;
    }
}

public sealed class FuelManager
{
    private readonly List<MonitoredVehicle> _monitored = new();
    private long _lastRefillTickMs;
    public int RefillIntervalMs { get; set; } = 100;

    public IReadOnlyList<MonitoredVehicle> MonitoredVehicles => _monitored;

    public void AddVehicle(string vehicleId, string displayName)
    {
        if (_monitored.Any(m => m.VehicleId == vehicleId))
            return;

        _monitored.Add(new MonitoredVehicle(vehicleId, displayName));
        Console.WriteLine($"eternal-flame: AddVehicle - vehicleId={vehicleId}, monitored={_monitored.Count}");
    }

    public void RemoveVehicle(string vehicleId)
    {
        int removed = _monitored.RemoveAll(m => m.VehicleId == vehicleId);
        Console.WriteLine($"eternal-flame: RemoveVehicle - vehicleId={vehicleId}, removed={removed}, monitored={_monitored.Count}");
    }

    /// <summary>
    /// Refills monitored vehicles from the <c>Universe.ExecuteNextVehicleSolvers</c> prefix.
    /// Fuel and battery charge are module state that the vehicle worker snapshots when it starts
    /// and commits back after its step. A refill made later in the frame (for example from the UI
    /// update) is overwritten by that commit while engines burn, so both refills run here: after
    /// the previous results are applied and before the next snapshot, where KSA's own refill
    /// command runs. KSA 5482 flags refilled tanks so dry engines re-read their propellant.
    /// </summary>
    public void RefillBeforeVehicleSolvers()
    {
        if (_monitored.Count == 0)
            return;

        long now = Environment.TickCount64;
        int interval = Math.Max(RefillIntervalMs, 1);
        long elapsedMs = _lastRefillTickMs == 0 ? interval : now - _lastRefillTickMs;
        if (elapsedMs < interval)
            return;

        _lastRefillTickMs = now;

        var vehicles = VehicleProvider.GetAllVehicles();
        if (vehicles.Count == 0)
            return;

        foreach (var entry in _monitored)
        {
            if (!entry.RefillFuel && !entry.RefillElectricity)
                continue;

            var vehicle = vehicles.FirstOrDefault(v => v.Id == entry.VehicleId);
            if (vehicle == null)
                continue;

            if (entry.RefillFuel)
            {
                try
                {
                    vehicle.RefillConsumables();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"eternal-flame: Error refilling fuel {entry.DisplayName}: {ex.Message}");
                }
            }

            if (entry.RefillElectricity)
            {
                try
                {
                    RefillBatteries(vehicle);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"eternal-flame: Error refilling electricity {entry.DisplayName}: {ex.Message}\n{ex}");
                }
            }
        }
    }

    private static void RefillBatteries(Vehicle vehicle)
    {
        var batteryStates = vehicle.Parts.Batteries;
        if (batteryStates.NumModules == 0)
            return;

        var modules = batteryStates.Modules;
        for (int i = 0; i < modules.Length; i++)
        {
            var battery = modules[i];
            var mutableRef = batteryStates.GetModuleAndAllMutableStatesForInitialization(battery);
            mutableRef.Module.Refill(ref mutableRef.State);
        }
    }
}
