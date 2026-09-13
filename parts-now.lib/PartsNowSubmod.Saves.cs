using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.PartsNowLib;

public sealed partial class PartsNowSubmod : ISaveParticipantSource
{
    public sealed record SavedDependency(string ModId, string[] PartIds);
    public IEnumerable<ISaveParticipant> SaveParticipants => new[]
    {
        new SaveParticipant<SavedDependency[]>("parts-now", () => RuntimeModRegistry.All()
            .Select(m => new SavedDependency(m.ModId, m.PartIds.ToArray())).ToArray(),
            () => { }, // Installed template registries are process-wide dependencies, never save-owned objects.
            (state, context) =>
            {
                foreach (var dependency in state)
                    foreach (string id in dependency.PartIds)
                    {
                        try { _ = ModLibrary.Get<PartTemplate>(id); }
                        catch { context.Warn($"Install/enable part mod '{dependency.ModId}' (missing template '{id}') and reload the save."); }
                    }
            }, order: 0, validate: state =>
            {
                if (state.Length > 1000 || state.Any(m => m == null || string.IsNullOrWhiteSpace(m.ModId) || m.PartIds == null || m.PartIds.Length > 10000))
                    throw new InvalidOperationException("Invalid saved part-mod dependencies.");
            })
    };
}
