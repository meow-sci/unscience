using System;
using KSA;

namespace MeowSci.WingWalkerLib;

/// <summary>Stable native asset identities; the saved part template determines its crew posture.</summary>
public static class ExternalCrewParts
{
    public const string SeatedPartId = "Unscience_WingWalker_Seated";
    public const string StandingPartId = "Unscience_WingWalker_Standing";
    public const string SeatKey = "CrewSeat";
    // KSA's native portrait fallback uses this ground-model eye height, in metres.
    public const double StandingEyeHeight = 0.7552;

    public static bool IsExternal(IVASeat seat) => HasPart(seat, SeatedPartId) || IsStanding(seat);
    public static bool IsStanding(IVASeat seat) => HasPart(seat, StandingPartId);

    private static bool HasPart(IVASeat seat, string id) =>
        string.Equals(seat.TemplateId, SeatKey, StringComparison.Ordinal)
        && string.Equals(seat.Parent.FullPart.Template.Id, id, StringComparison.Ordinal);
}
