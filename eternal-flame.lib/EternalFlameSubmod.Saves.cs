using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.EternalFlameLib;

public sealed partial class EternalFlameSubmod : ISaveParticipantSource
{
    public sealed record SavedMonitor(string VehicleId, bool Fuel, bool Electricity);
    public sealed record SavedFuel(int Interval, SavedMonitor[] Vehicles);
    public IEnumerable<ISaveParticipant> SaveParticipants => new[]
    {
        new SaveParticipant<SavedFuel>("eternal-flame", () => new(_fuelManager.RefillIntervalMs,
            _fuelManager.MonitoredVehicles.Select(v => new SavedMonitor(v.VehicleId, v.RefillFuel, v.RefillElectricity)).ToArray()),
            () => { _fuelManager = new(); _refillIntervalMs = 100; _selectedVehicleIndex = -1; },
            (state, context) =>
            {
                _refillIntervalMs = _fuelManager.RefillIntervalMs = state.Interval;
                foreach (var entry in state.Vehicles)
                {
                    if (VehicleProvider.FindVehicle(entry.VehicleId) == null) { context.Warn($"Missing vehicle {entry.VehicleId}."); continue; }
                    _fuelManager.AddVehicle(entry.VehicleId, entry.VehicleId);
                    var added = _fuelManager.MonitoredVehicles.Last();
                    added.RefillFuel = entry.Fuel;
                    added.RefillElectricity = entry.Electricity;
                }
            }, validate: state =>
            {
                if (state.Interval < 0 || state.Interval > 5000 || state.Vehicles == null || state.Vehicles.Length > 10000
                    || state.Vehicles.Any(v => v == null || string.IsNullOrWhiteSpace(v.VehicleId))
                    || state.Vehicles.Select(v => v.VehicleId).Distinct().Count() != state.Vehicles.Length)
                    throw new InvalidOperationException("Invalid fuel monitor settings.");
            })
    };
}
