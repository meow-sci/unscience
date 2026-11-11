using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using KSA;
using MeowSci.DohLib;
using MeowSci.WarPaintLib;
using MeowSci.DohLib.Spawning;
using MeowSci.KsaAbstractions.Persistence;

internal static partial class Checks
{
    private static void NamingChecks()
    {
        Universe.KittenRoster = new();
        var spawner = new DohSubmod().Spawner;
        var system = new CelestialSystem();
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "Mittens" }) == "Mittens",
            "single spawns use the exact entered name");
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "  Space Cat  " }) == "  Space Cat  ",
            "nonblank custom names are preserved verbatim");
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = SpawnRequest.DefaultNamePrefix })
            == SpawnRequest.DefaultNamePrefix, "the prefilled value is also an exact single-spawn name");
        system.All.UnsafeAsList().Add(new Vehicle("MITTENS"));
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "Mittens" }) == "Mittens1",
            "occupied single names gain a suffix using the native case-insensitive lookup");
        system.All.UnsafeAsList().Add(new Vehicle("mittens007"));
        system.All.UnsafeAsList().Add(new Vehicle("Mittens42"));
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "Mittens" }) == "Mittens43",
            "single collisions advance above the highest suffix instead of filling gaps");
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "Mittens42" }) == "Mittens43",
            "occupied numbered single names advance their suffix instead of appending another number");
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "mittens007" }) == "mittens43",
            "leading-zero suffixes compare numerically and preserve the requested prefix casing");
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "Mittens3" }) == "Mittens3",
            "available exact single names remain unchanged even when higher suffixes exist");

        system.All.UnsafeAsList().Add(new Vehicle("Squad_0"));
        system.All.UnsafeAsList().Add(new KittenEva("SQUAD_19"));
        system.All.UnsafeAsList().Add(new Vehicle("Squad_900_tail"));
        system.All.UnsafeAsList().Add(new Vehicle("Squad_-50"));
        system.All.UnsafeAsList().Add(new Vehicle("Squad_+60"));
        var batch = new SpawnRequest { VehicleName = "Squad_", Count = 3 };
        Require(spawner.GenerateKittenName(system, batch) == "Squad_20", "batches advance past sparse case-insensitive suffixes and ignore nonnumeric endings");
        system.All.UnsafeAsList().Add(new KittenEva("Squad_20"));
        Require(spawner.GenerateKittenName(system, batch) == "Squad_21", "each registered batch member advances the next name");
        Require(spawner.GenerateKittenName(system, new SpawnRequest()) == "Kitten_doh_0",
            "unrelated custom prefixes do not advance default-prefix numbering");
        system.All.UnsafeAsList().Add(new KittenEva("Kitten_doh_80"));
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "" }) == "Kitten_doh_81",
            "empty single names use automatic numbering");
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = " \t", Count = 2 }) == "Kitten_doh_81",
            "whitespace batch names use the default prefix");
        system.All.UnsafeAsList().Add(new KittenEva(SpawnRequest.DefaultNamePrefix));
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = SpawnRequest.DefaultNamePrefix }) == "Kitten_doh_81",
            "repeated spawns with the prefilled single name advance above existing numbers");

        var fresh = new DohSubmod().Spawner;
        Require(fresh.GenerateKittenName(system, batch) == "Squad_21",
            "a fresh spawner continues above native-restored names without a persisted counter");
        Require(spawner.GenerateKittenName(new CelestialSystem(), batch) == "Squad_0",
            "changing scenes does not carry a stale numbering counter");
        system.All.UnsafeAsList().Add(new Vehicle("Team72"));
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "Team7", Count = 2 }) == "Team73",
            "batch prefixes ending in digits remain literal prefixes");
        system.All.UnsafeAsList().Add(new Vehicle("999"));
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "999" }) == "1000",
            "a digits-only occupied single name advances as a number");
        system.All.UnsafeAsList().Add(new Vehicle("Huge_18446744073709551615"));
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "Huge_", Count = 2 }) == "Huge_18446744073709551616",
            "large existing suffixes advance without integer overflow or wrapping");

        RosterNamingChecks(system);
    }

    /// <summary>KSA 5669: EVA kittens share roster dose/death state by name, so rostered names are occupied.</summary>
    private static void RosterNamingChecks(CelestialSystem system)
    {
        var spawner = new DohSubmod().Spawner;
        Universe.KittenRoster = new KittenRosterData();
        Universe.KittenRoster.Kittens.Add(new("Valentina", "Calico"));
        Universe.KittenRoster.Kittens.Add(new("Crew_0", "Calico"));
        Universe.KittenRoster.Kittens.Add(new("CREW_50", "Calico") { Kia = true });
        Universe.KittenRoster.Kittens.Add(new("Crew_75", "Calico") { Retired = true });
        system.All.UnsafeAsList().Add(new Vehicle("Crew_90"));
        try
        {
            Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "valentina" }) == "valentina1",
                "a single roster-only name gains a suffix case-insensitively");
            Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "Crew_", Count = 2 }) == "Crew_91",
                "batch numbering takes the maximum across scene and roster names");
            Universe.KittenRoster.Kittens.Add(new("crew_120", "Calico") { AssignedVehicleId = "Other craft" });
            Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "Crew_0" }) == "Crew_121",
                "single collisions account for seated, retired and KIA roster identities");
            Require(Universe.KittenRoster.Kittens.Count == 5 && Universe.KittenRoster.Kittens[2].Kia
                && Universe.KittenRoster.Kittens[3].Retired,
                "name selection never changes existing native crew history");
            Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "Valentine" }) == "Valentine",
                "names that only resemble a rostered kitten remain available");
        }
        finally
        {
            Universe.KittenRoster = new KittenRosterData();
        }
    }

    private static void SpawnNameContinuationSaveChecks()
    {
        Universe.KittenRoster = new();
        var submod = new DohSubmod { SpawnName = "Saved_4" };
        var coordinator = new SceneSaveCoordinator(submod.SaveParticipants);
        var kitten = new KittenEva("Saved_4");
        SetWorld(kitten, new KittenEva("Saved_40"));
        submod.Track(kitten, "Calico", null);
        KittenSpawner.EnsureRoster(kitten);
        Universe.KittenRoster.Kittens.Add(new("Saved_99", "Tabby") { Kia = true });
        var saved = RoundTrip(coordinator.Capture());
        var nativeRoster = SaveJson.ToElement(Universe.KittenRoster);
        for (int pass = 0; pass < 2; pass++)
        {
            Universe.KittenRoster = SaveJson.FromElement<KittenRosterData>(nativeRoster);
            Load(coordinator, saved, new KittenEva("Saved_4"), new KittenEva("Saved_40"));
            string name = submod.Spawner.GenerateKittenName(Universe.CurrentSystem!, new SpawnRequest { VehicleName = submod.SpawnName });
            Require(name == "Saved_100" && submod.SpawnName == "Saved_4" && submod.Registry.Count == 1,
                "real-adapter repeated replay derives the next name from restored vehicles/roster without editing saved input or duplicating kittens");
            var spawned = new KittenEva(name);
            Universe.CurrentSystem!.All.UnsafeAsList().Add(spawned);
            KittenSpawner.EnsureRoster(spawned, requireNew: true);
            Require(submod.Spawner.GenerateKittenName(Universe.CurrentSystem!, new SpawnRequest { VehicleName = submod.SpawnName }) == "Saved_101",
                "the next post-load spawn advances again after normal native roster registration");
        }
        Universe.KittenRoster = new();
        Load(coordinator, null);
        Require(submod.Spawner.GenerateKittenName(Universe.CurrentSystem!, new SpawnRequest()) == "Kitten_doh_0",
            "a vanilla/new scene derives fresh automatic numbering after sidecar reset");
    }

    private static void SpawnNameSaveChecks()
    {
        var submod = new DohSubmod();
        var coordinator = new SceneSaveCoordinator(submod.SaveParticipants.Append(WarPaintService.Shared.SaveParticipant));
        SetWorld(new Vehicle("Rocket"));
        Require(submod.SpawnName == SpawnRequest.DefaultNamePrefix, "the name field starts with the existing prefix");
        submod.SpawnName = "Crew 🐱_";
        var saved = RoundTrip(coordinator.Capture());
        submod.SpawnName = "Changed";
        Require(SaveJson.FromElement<DohSubmod.SavedDoh>(saved.Features["doh"].State).SpawnName == "Crew 🐱_",
            "capture detaches the name from subsequent UI edits");
        Load(coordinator, saved, new Vehicle("Different scene"));
        Require(submod.SpawnName == "Crew 🐱_", "the real adapter restores the Unicode name across scenes");
        submod.SpawnName = "Changed again";
        Load(coordinator, saved, new Vehicle("Another scene"));
        Require(submod.SpawnName == "Crew 🐱_", "repeated loads restore the saved name without compounding it");

        submod.SpawnName = "";
        var blank = RoundTrip(coordinator.Capture());
        Load(coordinator, blank);
        Require(submod.SpawnName == "", "blank automatic-naming input round-trips unchanged");

        var legacyState = JsonNode.Parse(saved.Features["doh"].State.GetRawText())!.AsObject();
        legacyState.Remove("SpawnName");
        var legacy = RoundTrip(saved);
        legacy.Features["doh"] = new SaveFeature { State = JsonSerializer.SerializeToElement(legacyState) };
        Load(coordinator, legacy);
        Require(submod.SpawnName == SpawnRequest.DefaultNamePrefix && coordinator.RetainedCount == 0,
            "legacy v1 records without the name restore the default without losing the record");
        submod.SpawnName = "Old scene";
        Load(coordinator, null);
        Require(submod.SpawnName == SpawnRequest.DefaultNamePrefix, "vanilla saves and new scenes reset the name field");

        foreach (string? invalid in new[] { null, "bad\0name", new string('x', 256), new string('猫', 86) })
        {
            var payload = SaveJson.FromElement<DohSubmod.SavedDoh>(saved.Features["doh"].State);
            payload.SpawnName = invalid!;
            var malformed = RoundTrip(saved);
            malformed.Features["doh"] = new SaveFeature { State = SaveJson.ToElement(payload) };
            Load(coordinator, malformed);
            Require(coordinator.RetainedCount == 1 && submod.SpawnName == SpawnRequest.DefaultNamePrefix
                && coordinator.Messages.Any(message => message.Contains("Invalid DOH spawn name")),
                "invalid names are diagnosed and retained before touching the text buffer");
        }
    }
}
