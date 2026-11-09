using System;
using System.Linq;
using KSA;
using MeowSci.DohLib;
using MeowSci.DohLib.Spawning;
using MeowSci.KsaAbstractions.Persistence;

internal static partial class Checks
{
    private static bool RosterRefused(KittenEva kitten, bool requireNew = false)
    {
        try { KittenSpawner.EnsureRoster(kitten, requireNew); return false; }
        catch (InvalidOperationException) { return true; }
    }

    private static void RosterIntegrationChecks()
    {
        Universe.KittenRoster = new();
        Universe.Now = 100;
        var kitten = new KittenEva("Roster cat");
        SetWorld(kitten);
        var entry = KittenSpawner.EnsureRoster(kitten, requireNew: true);
        Require(Universe.KittenRoster.Kittens.Count == 1 && ReferenceEquals(Universe.KittenRoster.Find("ROSTER CAT"), entry)
            && entry.Character == kitten.Character.Id, "spawn integrates an exact, case-insensitive native identity and actual character");
        Require(kitten.HasLaunched && entry.AssignedVehicleId == KittenRosterEntryData.EvaAssignmentId
            && entry.MissionCount == 1 && entry.CurrentMissionStartTime == 100,
            "spawn mirrors native starting EVA crew: launched vehicle and one active EVA mission");
        entry.LifetimeDose = .3; entry.TravelledMeters = 123; entry.TotalMissionTime = 50;
        Universe.Now = 120;
        Require(ReferenceEquals(KittenSpawner.EnsureRoster(kitten), entry) && entry.MissionCount == 1
            && entry.CurrentMissionStartTime == 100 && entry.LifetimeDose == .3 && entry.TotalMissionTime == 50,
            "maintenance preserves the native record, mission start and accumulated history");
        Require(RosterRefused(kitten, requireNew: true) && Universe.KittenRoster.Kittens.Count == 1,
            "a new spawn cannot adopt an already occupied roster identity");

        var submod = new DohSubmod();
        submod.Track(kitten, "Calico", null);
        Require(submod.Spawner.RequestSmite(kitten.Id), "integrated kitten uses the existing native kill input");
        InputEvents.ApplyInputEvents();
        submod.Spawner.PruneDisposed();
        Require(entry.Kia && entry.CauseOfDeath == KittenDeathCause.Abandoned && !entry.AssignedToVehicle
            && entry.TotalMissionTime == 70 && Universe.KittenRoster.Kittens.Contains(entry),
            "native death updates cause/mission history and retains the KIA roster entry after DOH pruning");
        Require(submod.Spawner.GenerateKittenName(Universe.CurrentSystem!, new SpawnRequest { VehicleName = "roster CAT" }) == null,
            "a dead roster identity remains reserved after its EVA vehicle is gone");

        var boarded = new KittenEva("Boarder"); SetWorld(boarded);
        var boardEntry = KittenSpawner.EnsureRoster(boarded, requireNew: true);
        submod.Track(boarded, "Calico", null);
        boardEntry.TransferToVehicle("Rocket");
        Universe.DestroyVehicle(boarded, CrewDisposition.Boarded);
        submod.Spawner.PruneDisposed();
        Require(!boardEntry.Kia && boardEntry.AssignedVehicleId == "Rocket" && boardEntry.MissionCount == 1
            && submod.Registry.Count == 0, "boarding/pruning preserve the native seated assignment and mission");

        var old = new KittenEva("Legacy live"); SetWorld(old);
        submod.Track(old, "Calico", null);
        submod.Spawner.MaintainRoster(); submod.Spawner.MaintainRoster();
        Require(Universe.KittenRoster.Find(old.Id) is { MissionCount: 1, AssignedVehicleId: "EVA" } && old.HasLaunched,
            "existing tracked unrostered kittens are backfilled idempotently");
        var unrelated = new KittenEva("Untracked"); Universe.CurrentSystem!.All.UnsafeAsList().Add(unrelated);
        submod.Spawner.MaintainRoster();
        Require(Universe.KittenRoster.Find(unrelated.Id) == null, "maintenance never guesses ownership of untracked native kittens");

        var bad = new KittenEva("Unavailable"); SetWorld(bad);
        var occupied = new KittenRosterEntryData(bad.Id, "Tabby"); Universe.KittenRoster.Kittens.Add(occupied);
        Require(RosterRefused(bad) && occupied.Character == "Tabby", "conflicting characters never overwrite an existing crew member");
        occupied.Character = "Calico"; occupied.Kia = true;
        Require(RosterRefused(bad) && occupied.Kia, "repair never resurrects native KIA crew");
        occupied.Kia = false; occupied.Retired = true;
        Require(RosterRefused(bad) && occupied.Retired, "repair never reactivates retired crew");
        occupied.Retired = false; occupied.StartMission("Other craft");
        Require(RosterRefused(bad) && occupied.AssignedVehicleId == "Other craft", "repair never steals an assignment from another vehicle");
        KittenSpawner.RemoveOrphanedKitten(Universe.CurrentSystem!, bad.Id);
        Require(occupied.AssignedVehicleId == "Other craft" && !occupied.Kia,
            "failed-spawn cleanup cannot end an unrelated crew member's mission");

        var failed = new KittenEva("Failed spawn"); SetWorld(failed);
        var provisional = KittenSpawner.EnsureRoster(failed, requireNew: true);
        KittenSpawner.RemoveOrphanedKitten(Universe.CurrentSystem!, failed.Id, provisional);
        Require(failed.IsDisposed && Universe.KittenRoster.Find(failed.Id) == null
            && Universe.KittenRoster.Kittens.Contains(entry) && Universe.KittenRoster.Kittens.Contains(occupied),
            "failed initialization removes only its own provisional row and preserves earlier crew history");

        var foreign = new Vehicle("Not a kitten"); SetWorld(foreign);
        KittenSpawner.RemoveOrphanedKitten(Universe.CurrentSystem!, foreign.Id);
        Require(!foreign.IsDisposed && Universe.CurrentSystem!.All.TryGet(foreign.Id, out _),
            "constructor rollback never deregisters a non-kitten with the requested name");

        var stale = new KittenEva("Same"); SetWorld(new KittenEva("Same"));
        Require(RosterRefused(stale) && Universe.KittenRoster.Find("Same") == null,
            "stale same-name native objects cannot register crew");
        var duplicate = new KittenEva("Duplicate"); SetWorld(duplicate);
        Universe.KittenRoster.Kittens.Add(new("Duplicate", "Calico"));
        Universe.KittenRoster.Kittens.Add(new("DUPLICATE", "Calico"));
        Require(RosterRefused(duplicate), "ambiguous native roster identities are rejected");
    }

    private static void RosterSaveChecks()
    {
        Universe.KittenRoster = new();
        Universe.KittenRoster.Kittens.Add(new("Home crew", "Tabby") { LifetimeDose = .2 });
        var submod = new DohSubmod();
        var coordinator = new SceneSaveCoordinator(submod.SaveParticipants);
        var legacy = new SaveDocument();
        legacy.Features["doh"] = new() { State = SaveJson.ToElement(new DohSubmod.SavedDoh
        {
            Kittens = new() { new() { VehicleId = "Restored crew", CharacterId = "Calico" } }
        }) };
        Load(coordinator, legacy, new KittenEva("Restored crew"));
        var entry = Universe.KittenRoster.Find("Restored crew")!;
        Require(coordinator.RetainedCount == 0 && entry != null && entry.MissionCount == 1
            && entry.AssignedVehicleId == "EVA", "the real DOH v1 adapter backfills older unrostered save targets");
        entry!.LifetimeDose = .8; entry.TravelledMeters = 987; entry.TotalMissionTime = 42;
        var sidecar = RoundTrip(coordinator.Capture());
        var nativeRoster = SaveJson.ToElement(Universe.KittenRoster);
        Require(!sidecar.Features["doh"].State.GetRawText().Contains("MissionCount")
            && sidecar.Features["doh"].Version == 1, "roster history remains native-owned without duplicating it or changing the sidecar version");
        for (int pass = 0; pass < 2; pass++)
        {
            // Stand in for KSA's universe.xml roster reconstruction before the real sidecar adapter replays.
            Universe.KittenRoster = SaveJson.FromElement<KittenRosterData>(nativeRoster);
            Load(coordinator, sidecar, new KittenEva("Restored crew"));
            entry = Universe.KittenRoster.Find("Restored crew")!;
            Require(coordinator.RetainedCount == 0 && Universe.KittenRoster.Kittens.Count == 2
                && entry.MissionCount == 1 && entry.LifetimeDose == .8 && entry.TravelledMeters == 987
                && entry.TotalMissionTime == 42 && Universe.KittenRoster.Find("Home crew")!.LifetimeDose == .2,
                "repeated native-roster/real-adapter replay neither duplicates crew nor resets any history");
        }
        entry.Kia = true;
        Load(coordinator, sidecar, new KittenEva("Restored crew"));
        Require(coordinator.RetainedCount == 1 && submod.Registry.Count == 0 && entry.Kia
            && coordinator.Messages.Any(m => m.Contains("roster unavailable")),
            "an incompatible native crew record is diagnosed and the original sidecar retained without resurrection");
        Require(coordinator.Capture().Features["doh"].State.GetRawText() == sidecar.Features["doh"].State.GetRawText(),
            "failed roster replay preserves the original ownership record for a later compatible load");
        Universe.KittenRoster = new(); // Native new-scene replacement owns this reset.
        Load(coordinator, null);
        Require(submod.Registry.Count == 0 && Universe.KittenRoster.Kittens.Count == 0,
            "native/new-scene reset clears old references without recreating earlier crew");
    }
}
