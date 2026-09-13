using System;
using System.Collections.Generic;
using System.Linq;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.ByoMusicLib;

public sealed partial class ByoMusicSubmod : ISaveParticipantSource
{
    public sealed record SavedSound(string VehicleId, string File, bool Repeat, float Gap, float Volume, float Range);
    public IEnumerable<ISaveParticipant> SaveParticipants => new[]
    {
        new SaveParticipant<SavedSound[]>("byo-music", () => _sounds.Where(s => !s.Finished && !s.Target.IsDisposed)
            .Select(s => new SavedSound(s.Target.Id, s.FileName, s.Repeat, s.GapSeconds, s.Volume, s.RangeMetres)).ToArray(),
            Dispose, (state, context) =>
            {
                foreach (var item in state)
                {
                    try
                    {
                        var target = VehicleProvider.FindVehicle(item.VehicleId);
                        if (target == null) { context.Warn($"Missing vehicle {item.VehicleId}."); continue; }
                        _sounds.Add(new(target, item.File, item.Repeat, item.Gap, item.Volume, item.Range, paused: true));
                    }
                    catch (Exception ex) { context.Warn($"{item.File}: {ex.Message}"); }
                }
                _status = "Saved sounds restored paused. Resume starts each sound from the beginning.";
            }, order: 220, validate: state =>
            {
                if (state.Length > 1000 || state.Any(s => s == null || string.IsNullOrWhiteSpace(s.VehicleId)
                    || string.IsNullOrWhiteSpace(s.File) || !float.IsFinite(s.Gap) || s.Gap < 0 || s.Gap > 3600
                    || !float.IsFinite(s.Volume) || s.Volume < 0 || s.Volume > 1 || !float.IsFinite(s.Range) || s.Range < 1 || s.Range > 100000))
                    throw new InvalidOperationException("Invalid saved sound settings.");
            })
    };
}
