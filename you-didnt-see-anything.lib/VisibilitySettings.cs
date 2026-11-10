using System;
using System.Text.Json.Serialization;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.YouDidntSeeAnythingLib;

/// <summary>Scene-owned options; no game or ImGui pointers are persisted.</summary>
public static class VisibilitySettings
{
    public static bool Enabled;
    public static bool DetachedOnly;
    public static bool HideGauges;

    public sealed record SavedSettings(
        [property: JsonRequired] bool Enabled,
        [property: JsonRequired] bool DetachedOnly,
        [property: JsonRequired] bool HideGauges);
    public static ISaveParticipant CreateParticipant() => new SaveParticipant<SavedSettings>(
        "you-didnt-see-anything", Capture, Reset, (state, _) => Restore(state), validate: Validate);
    public static SavedSettings Capture() => new(Enabled, DetachedOnly, HideGauges);
    public static void Reset() => (Enabled, DetachedOnly, HideGauges) = (false, false, false);
    public static void Validate(SavedSettings state) => ArgumentNullException.ThrowIfNull(state);
    public static void Restore(SavedSettings state) => (Enabled, DetachedOnly, HideGauges) =
        (state.Enabled, state.DetachedOnly, state.HideGauges);
}
