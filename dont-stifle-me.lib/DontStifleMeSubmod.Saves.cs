using System.Collections.Generic;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.DontStifleMeLib;

public sealed partial class DontStifleMeSubmod : ISaveParticipantSource
{
    public sealed record SavedLimits(bool Enabled, bool Snap, bool ExtendedValues);
    public IEnumerable<ISaveParticipant> SaveParticipants => new[]
    {
        new SaveParticipant<SavedLimits>("dont-stifle-me",
            () => new(EditorScaleSettings.Enabled, EditorScaleSettings.Snap, EditorLimitSettings.JplSaidNoClamps),
            () => { EditorScaleSettings.Enabled = true; EditorScaleSettings.Snap = true; EditorLimitSettings.JplSaidNoClamps = false; },
            (state, _) => { EditorScaleSettings.Enabled = state.Enabled; EditorScaleSettings.Snap = state.Snap; EditorLimitSettings.JplSaidNoClamps = state.ExtendedValues; }, order: 10)
    };
}
