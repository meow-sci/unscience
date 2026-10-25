using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using KSA;
using MeowSci.DohLib;
using MeowSci.DohLib.Spawning;
using MeowSci.KsaAbstractions.Persistence;

internal static partial class Checks
{
    private static void NamingChecks()
    {
        var spawner = new DohSubmod().Spawner;
        var system = new CelestialSystem();
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "Mittens" }) == "Mittens",
            "single spawns use the exact entered name");
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "  Space Cat  " }) == "  Space Cat  ",
            "nonblank custom names are preserved verbatim");
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = SpawnRequest.DefaultNamePrefix })
            == SpawnRequest.DefaultNamePrefix, "the prefilled value is also an exact single-spawn name");
        system.All.UnsafeAsList().Add(new Vehicle("MITTENS"));
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "Mittens" }) == null,
            "occupied single names are rejected using the native case-insensitive lookup");

        system.All.UnsafeAsList().Add(new Vehicle("Squad_0"));
        var batch = new SpawnRequest { VehicleName = "Squad_", Count = 3 };
        Require(spawner.GenerateKittenName(system, batch) == "Squad_1", "batches skip occupied prefixed names");
        Require(spawner.GenerateKittenName(system, batch) == "Squad_2", "batch names advance the numeric suffix");
        Require(spawner.GenerateKittenName(system, new SpawnRequest()) == "Kitten_doh_3",
            "callers omitting a name retain automatic default-prefix numbering");
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = "" }) == "Kitten_doh_4",
            "empty single names use automatic numbering");
        Require(spawner.GenerateKittenName(system, new SpawnRequest { VehicleName = " \t", Count = 2 }) == "Kitten_doh_5",
            "whitespace batch names use the default prefix");

        var fresh = new DohSubmod().Spawner;
        system.All.UnsafeAsList().Add(new KittenEva("Squad_1"));
        Require(fresh.GenerateKittenName(system, batch) == "Squad_2",
            "a fresh spawner skips native-restored names without persisting its counter");
    }

    private static void SpawnNameSaveChecks()
    {
        var submod = new DohSubmod();
        var coordinator = new SceneSaveCoordinator(submod.SaveParticipants);
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
