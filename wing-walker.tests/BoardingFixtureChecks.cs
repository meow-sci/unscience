using System;
using System.Linq;
using HarmonyLib;
using KSA;
using MeowSci.WingWalkerLib;

internal static class BoardingFixtureChecks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Wing Walker boarding fixture: " + message);
    }

    public static void Warm()
    {
        var boardingSeat = Seat("WarmBoardingSeat");
        new KittenEva("warm-kitten").BoardVehicle(VehicleWith(boardingSeat), new EVADoor(boardingSeat));
        var addCrewSeat = Seat("WarmAddCrewSeat");
        VehicleWith(addCrewSeat).AddCrew(KeyHash.Make("warm-add-crew"), addCrewSeat.Parent.FullPart.Id, addCrewSeat.TemplateId);
    }

    public static void Run()
    {
        var boardMethod = AccessTools.Method(typeof(KittenEva), nameof(KittenEva.BoardVehicle), new[] { typeof(Vehicle), typeof(EVADoor) });
        var boardPatches = Harmony.GetPatchInfo(boardMethod);
        Require(boardPatches?.Prefixes.Any(patch => patch.PatchMethod.Name == "BeginBoarding") == true,
            "Apply installs the boarding scope prefix on KittenEva.BoardVehicle");

        var first = Seat(ExternalCrewParts.StandingPartId);
        var second = Seat(ExternalCrewParts.StandingPartId);
        first.AssignedKittenHash = KeyHash.Make("already-here");
        var host = VehicleWith(first, second);
        host.Id = "boarding-host";
        host.HasLaunched = true;
        var kitten = new KittenEva("new-crew");
        CrewSeatResult boarded = kitten.BoardVehicle(host, new EVADoor(second));
        Require(boarded == CrewSeatResult.Seated && second.AssignedKittenHash == KeyHash.Make(kitten.Id),
            $"aligned door boards the exact second copy when the first same-ID seat is occupied (result={boarded})");
        Require(first.AssignedKittenHash == KeyHash.Make("already-here"), "boarding leaves the first occupant unchanged");
        Require(host.AddCrew(KeyHash.Make("unscoped"), second.Parent.FullPart.Id, second.TemplateId) == CrewSeatResult.SeatTaken,
            "direct unscoped AddCrew retains native first-match behavior");
        Require(new KittenEva(kitten.Id).BoardVehicle(host, new EVADoor(second)) == CrewSeatResult.AlreadyAboard,
            "duplicate crew is rejected across every seat on the host");
        Require(new KittenEva("other-new-crew").BoardVehicle(host, new EVADoor(second)) == CrewSeatResult.SeatTaken
            && second.AssignedKittenHash == KeyHash.Make(kitten.Id),
            "an occupied exact target returns SeatTaken without replacing its occupant");

        var staleAlignedSeat = Seat(ExternalCrewParts.StandingPartId);
        var currentHostSeat = Seat(ExternalCrewParts.StandingPartId);
        var staleHost = VehicleWith(currentHostSeat);
        CrewSeatResult staleResult = new KittenEva("stale-door-crew").BoardVehicle(staleHost, new EVADoor(staleAlignedSeat));
        Require(staleResult == CrewSeatResult.NoSuchSeat && currentHostSeat.AssignedKittenHash == KeyHash.Zero,
            "stale aligned seat reference fails closed instead of falling back by duplicate part ID");

        var nativeSeat = Seat("CoreCommandSeat");
        var nativeHost = VehicleWith(nativeSeat);
        Require(new KittenEva("native-crew").BoardVehicle(nativeHost, new EVADoor(nativeSeat)) == CrewSeatResult.Seated,
            "native aligned seats keep the stock boarding route");

        CheckForeignHostAndNestedScope();
        CheckExceptionRestoresScope();
    }

    private static void CheckForeignHostAndNestedScope()
    {
        var nestedFirst = Seat(ExternalCrewParts.SeatedPartId);
        var nestedSecond = Seat(ExternalCrewParts.SeatedPartId);
        nestedFirst.AssignedKittenHash = KeyHash.Make("nested-blocker");
        var outerHost = VehicleWith(nestedFirst, nestedSecond);
        var outerKitten = new KittenEva("outer-crew");
        var innerSeat = Seat(ExternalCrewParts.StandingPartId);
        var innerHost = VehicleWith(innerSeat);
        var foreignHostSeat = Seat(ExternalCrewParts.SeatedPartId);
        var foreignHost = VehicleWith(foreignHostSeat);
        CrewSeatResult foreignHostResult = CrewSeatResult.NoSuchSeat;
        CrewSeatResult nestedResult = CrewSeatResult.NoSuchSeat;
        CrewSeatResult afterNested = CrewSeatResult.NoSuchSeat;
        outerKitten.AfterAddCrew = () =>
        {
            foreignHostResult = foreignHost.AddCrew(KeyHash.Make("foreign-host-crew"),
                foreignHostSeat.Parent.FullPart.Id, foreignHostSeat.TemplateId);
            nestedResult = new KittenEva("inner-crew").BoardVehicle(innerHost, new EVADoor(innerSeat));
            afterNested = outerHost.AddCrew(KeyHash.Make(outerKitten.Id), nestedSecond.Parent.FullPart.Id, nestedSecond.TemplateId);
        };
        Require(outerKitten.BoardVehicle(outerHost, new EVADoor(nestedSecond)) == CrewSeatResult.Seated
            && foreignHostResult == CrewSeatResult.Seated && foreignHostSeat.AssignedKittenHash == KeyHash.Make("foreign-host-crew")
            && nestedResult == CrewSeatResult.Seated && afterNested == CrewSeatResult.AlreadyAboard,
            "foreign hosts use stock AddCrew and nested boarding restores the outer exact-seat scope");
    }

    private static void CheckExceptionRestoresScope()
    {
        var first = Seat(ExternalCrewParts.StandingPartId);
        var second = Seat(ExternalCrewParts.StandingPartId);
        first.AssignedKittenHash = KeyHash.Make("exception-blocker");
        var host = VehicleWith(first, second);
        var kitten = new KittenEva("exception-crew")
        {
            AfterAddCrew = () => throw new InvalidOperationException("fixture boarding failure")
        };
        bool threw = false;
        try { kitten.BoardVehicle(host, new EVADoor(second)); }
        catch (InvalidOperationException) { threw = true; }
        CrewSeatResult afterException = host.AddCrew(KeyHash.Make(kitten.Id), second.Parent.FullPart.Id, second.TemplateId);
        Require(threw && afterException == CrewSeatResult.SeatTaken,
            "boarding finalizer restores scope after the native body throws");
    }

    private static IVASeat Seat(string partId) => new(partId);
    private static Vehicle VehicleWith(params IVASeat[] seats) => new(new PartTree(seats));
}
