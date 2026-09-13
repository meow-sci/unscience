using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.IronManLib;

public sealed partial class IronManSubmod : ISaveParticipantSource
{
    internal sealed class SavedKitten
    {
        public string VehicleId { get; set; } = "";
        public IronManEvaSettings.Saved? Original { get; set; }
        public IronManFlightSettings.Saved? Current { get; set; }
    }

    public IEnumerable<ISaveParticipant> SaveParticipants => new[]
    {
        new SaveParticipant<SavedKitten[]>("iron-man", () => _configured.Where(k => !k.IsDisposed).Select(k => new SavedKitten
        {
            VehicleId = k.Id,
            Original = _enabled.TryGetValue(k, out var original) ? original.Capture(k) : null,
            Current = IsEnabled(k) ? new IronManFlightSettings(k.FlightComputer).Capture() : null
        }).ToArray(), ResetSavedMode, (state, context) =>
        {
            foreach (var data in state)
            {
                if (VehicleProvider.FindVehicle(data.VehicleId) is not KittenEva kitten)
                { context.Warn($"Missing kitten {data.VehicleId}."); continue; }
                try
                {
                    Configure(kitten);
                    if (data.Original == null) continue;
                    var original = new IronManEvaSettings(data.Original, kitten);
                    Enable(kitten); // Disarmed by design; reconstruct native EVA restore preferences below.
                    _enabled[kitten] = original;
                    if (data.Current != null) new IronManFlightSettings(data.Current).Restore(kitten.FlightComputer);
                }
                catch (Exception ex) { context.Warn($"{data.VehicleId}: {ex.Message}"); }
            }
            PublishEnabled();
            _status = "Saved mode restored. Engines remain disarmed after load.";
        }, order: 90, validate: state =>
        {
            if (state.Length > 10000 || state.Any(s => s == null || string.IsNullOrWhiteSpace(s.VehicleId)))
                throw new InvalidOperationException("Invalid saved Iron Man targets.");
            if (state.Select(s => s.VehicleId).Distinct().Count() != state.Length)
                throw new InvalidOperationException("Duplicate Iron Man targets.");
            foreach (var item in state)
            {
                if (item.Current != null) IronManFlightSettings.Validate(item.Current);
                if (item.Original != null)
                {
                    IronManFlightSettings.Validate(item.Original.Flight);
                    if (!Enum.IsDefined(item.Original.ControlMode)) throw new InvalidOperationException("Invalid EVA control mode.");
                }
            }
        })
    };

    private void ResetSavedMode()
    {
        foreach (var pair in _enabled)
            if (!pair.Key.IsDisposed) pair.Value.Restore(pair.Key);
        _enabled.Clear();
        _configured.Clear();
        PublishEnabled();
        _pending = false;
    }
}
