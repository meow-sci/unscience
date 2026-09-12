using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.IFeelSeenLib;

public sealed partial class IFeelSeenSubmod : ISaveParticipantSource
{
    public sealed record SavedVisibility(string VehicleId, bool Visible);
    public IEnumerable<ISaveParticipant> SaveParticipants => new[]
    {
        new SaveParticipant<SavedVisibility[]>("i-feel-seen",
            () => _tracker.Tracked.Where(v => !v.Vehicle.IsDisposed).Select(v => new SavedVisibility(v.Vehicle.Id, v.SeeMe)).ToArray(),
            () => { _tracker.Clear(); _pendingVehicleIndex = 0; }, (state, context) =>
            {
                foreach (var item in state)
                {
                    var vehicle = VehicleProvider.FindVehicle(item.VehicleId);
                    if (vehicle == null) { context.Warn($"Missing vehicle {item.VehicleId}."); continue; }
                    _tracker.AddVehicle(vehicle);
                    _tracker.Tracked.Last().SeeMe = item.Visible;
                }
            }, validate: state =>
            {
                if (state.Length > 10000 || state.Any(s => s == null || string.IsNullOrWhiteSpace(s.VehicleId))
                    || state.Select(s => s.VehicleId).Distinct().Count() != state.Length)
                    throw new InvalidOperationException("Invalid visibility targets.");
            })
    };
}
