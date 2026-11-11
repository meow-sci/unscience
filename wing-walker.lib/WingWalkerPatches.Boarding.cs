using System;
using KSA;

namespace MeowSci.WingWalkerLib;

public static partial class WingWalkerPatches
{
    private sealed record BoardingTarget(KittenEva Kitten, Vehicle Host, EVADoor Door, IVASeat Seat);
    [ThreadStatic] private static BoardingTarget? _boardingTarget;

    private static void BeginBoarding(KittenEva __instance, Vehicle __0, EVADoor __1,
        out BoardingTarget? __state)
    {
        __state = _boardingTarget;
        IVASeat? seat = __1.AlignedSeat;
        _boardingTarget = seat != null && ExternalCrewParts.IsExternal(seat)
            ? new BoardingTarget(__instance, __0, __1, seat) : null;
    }

    private static void FinishBoarding(BoardingTarget? __state) => _boardingTarget = __state;

    // Native BoardVehicle resolves an aligned seat through AddCrew(part.Id, seat.TemplateId).
    // Repeated instances of one part have identical runtime Ids in 5801. Keep the exact door's
    // seat here while leaving native suit return, EVA teardown and camera/control handoff intact.
    private static bool AddCrewToBoardingSeat(Vehicle __instance, KeyHash __0, string __1, string __2,
        ref CrewSeatResult __result)
    {
        BoardingTarget? target = _boardingTarget;
        if (target == null || !ReferenceEquals(target.Host, __instance)
            || __0 != KeyHash.Make(target.Kitten.Id)
            || !string.Equals(__1, target.Seat.Parent.FullPart.Id, StringComparison.Ordinal)
            || !string.Equals(__2, target.Seat.TemplateId, StringComparison.Ordinal)) return true;

        bool found = false;
        if (!__instance.IsDisposed && !target.Kitten.IsDisposed && !__instance.Parts.IsDisposed
            && ReferenceEquals(target.Door.AlignedSeat, target.Seat))
        {
            ReadOnlySpan<IVASeat> seats = __instance.Crew;
            for (int i = 0; i < seats.Length; i++)
            {
                if (seats[i].AssignedKittenHash == __0)
                {
                    __result = CrewSeatResult.AlreadyAboard;
                    return false;
                }
                found |= ReferenceEquals(seats[i], target.Seat);
            }
        }
        if (!found) __result = CrewSeatResult.NoSuchSeat;
        else if (target.Seat.AssignedKittenHash != KeyHash.Zero) __result = CrewSeatResult.SeatTaken;
        else __result = Vehicle.SetSeatCrew(target.Seat, __0, __instance.Id, __instance.HasLaunched);
        return false;
    }
}
