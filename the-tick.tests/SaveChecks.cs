using System;
using System.Linq;
using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.TheTickLib;

internal static class SaveChecks
{
    private static int _checks;

    public static void Run(Harmony harmony)
    {
        var submod = new TheTickSubmod();
        var coordinator = new SceneSaveCoordinator(submod.SaveParticipants);
        var hull = new Vehicle("Hull");
        var rover = new Vehicle("Rover");
        SetWorld(hull, rover);
        TickProtection.Add(hull);
        TickProtection.Add(rover);
        var savedA = SaveJson.FromElement<SaveDocument>(SaveJson.ToElement(coordinator.Capture()));
        Require(savedA.Features[TheTickSubmod.SaveId].Version == 1, "record is version 1");
        Require(SavedIds(savedA).SequenceEqual(new[] { "Hull", "Rover" }), "JSON round-trip captures all targets in stable order");

        coordinator.PrepareLoad(savedA);
        Require(TickProtection.Contains(hull), "prepare does not mutate the world");
        coordinator.ResetWorld();
        Require(TickProtection.Snapshot().Length == 0 && submod.PickerResets == 1,
            "reset releases registrations and picker before reconstruction");
        var newHull = new Vehicle("Hull");
        var newRover = new Vehicle("Rover");
        SetWorld(newHull, newRover);
        coordinator.RestoreWorld();
        coordinator.FinishLoad();
        Require(TickProtection.Contains(newHull) && TickProtection.Contains(newRover), "replay binds reconstructed vehicles");
        Require(!TickProtection.Contains(hull) && !TickProtection.Contains(rover), "old objects remain unprotected");
        var restored = new VehicleUpdateState(newHull);
        PhysicsBubble.Detect(restored);
        Require(restored.DestructionEvent == null && newHull.Parts.Parts[0].CrashTolerancePascals == TickProtection.UnbreakableTolerancePascals,
            "restored registration gates both production patches");

        TickProtection.Remove(newHull);
        var savedB = coordinator.Capture();
        Require(SavedIds(savedB).SequenceEqual(new[] { "Rover" }), "deletions are reflected in subsequent saves");
        Load(coordinator, savedA, new Vehicle("Hull"), new Vehicle("Rover"));
        Require(TickProtection.Snapshot().Length == 2, "A restores both targets after edits");
        Load(coordinator, savedB, new Vehicle("Hull"), new Vehicle("Rover"));
        Require(TickProtection.Snapshot().Single().Id == "Rover", "B restores its distinct target list");
        Load(coordinator, savedA, new Vehicle("Hull"), new Vehicle("Rover"));
        Load(coordinator, savedA, new Vehicle("Hull"), new Vehicle("Rover"));
        Require(TickProtection.Snapshot().Length == 2, "A-B-A and repeated loads never duplicate targets");

        Load(coordinator, new SaveDocument(), new Vehicle("Hull"));
        Require(TickProtection.Snapshot().Length == 0 && coordinator.RetainedCount == 0, "saves without a the-tick record restore nothing and retain nothing");
        TickProtection.Add(VehicleProvider.FindVehicle("Hull")!);
        Load(coordinator, null, new Vehicle("Hull"));
        Require(TickProtection.Snapshot().Length == 0, "vanilla loads clear old setup");

        KSA.Program.ControlledVehicle = new Vehicle("Fallback");
        Load(coordinator, savedA);
        Require(TickProtection.Snapshot().Length == 0 && coordinator.RetainedCount == 1,
            "missing targets warn and retain the record without a controlled-vehicle fallback");
        Require(SavedIds(coordinator.Capture()).Length == 2, "partial restore cannot silently erase saved targets");
        Load(coordinator, savedA, new Vehicle("Hull"), new Vehicle("Hull"), new Vehicle("Rover") { IsDisposed = true });
        Require(TickProtection.Snapshot().Length == 0 && coordinator.Messages.Any(message => message.Contains("ambiguous")),
            "ambiguous and disposed targets are skipped with diagnostics");

        foreach (var invalid in new[] { new[] { "" }, new[] { "Hull", "Hull" }, new[] { (string)null! }, new string[10001] })
        {
            var bad = new SaveDocument();
            bad.Features[TheTickSubmod.SaveId] = new SaveFeature { State = SaveJson.ToElement(invalid) };
            Load(coordinator, bad, new Vehicle("Hull"));
            Require(coordinator.RetainedCount == 1 && TickProtection.Snapshot().Length == 0,
                "invalid saved arrays are rejected before replay and retained");
        }

        TheTickPatches.Remove(harmony);
        Load(coordinator, savedA, new Vehicle("Hull"), new Vehicle("Rover"));
        Require(coordinator.RetainedCount == 1 && coordinator.Messages.Any(message => message.Contains("unavailable")),
            "unavailable patches report failure instead of falsely restoring protection");
        TheTickPatches.Apply(harmony);
        Load(coordinator, savedA, new Vehicle("Hull"), new Vehicle("Rover"));
        Require(coordinator.RetainedCount == 0 && TickProtection.Snapshot().Length == 2,
            "saved protection is recoverable after patch availability returns");

        var duplicate = new Vehicle("Duplicate");
        TickProtection.Clear();
        TickProtection.Add(duplicate);
        SetWorld(duplicate, new Vehicle("Duplicate"));
        var freshCoordinator = new SceneSaveCoordinator(submod.SaveParticipants);
        var failedCapture = freshCoordinator.Capture();
        Require(!failedCapture.Features.ContainsKey(TheTickSubmod.SaveId) && failedCapture.Warnings.Any(message => message.Contains("ambiguous")),
            "capture does not write an unresolvable ambiguous target as a successful record");
        duplicate.IsDisposed = true;
        Require(SavedIds(freshCoordinator.Capture()).Length == 0, "disposed targets are excluded from capture");
        coordinator.ResetWorld();
        KSA.Program.ControlledVehicle = null;
        Console.WriteLine($"PASS: {_checks} The Tick save/restore checks.");
    }

    private static string[] SavedIds(SaveDocument document) => SaveJson.FromElement<string[]>(document.Features[TheTickSubmod.SaveId].State);

    private static void Load(SceneSaveCoordinator coordinator, SaveDocument? document, params Vehicle[] vehicles)
    {
        coordinator.PrepareLoad(document);
        coordinator.ResetWorld();
        SetWorld(vehicles);
        coordinator.RestoreWorld();
        coordinator.FinishLoad();
    }

    private static void SetWorld(params Vehicle[] vehicles)
    {
        Universe.CurrentSystem = new CelestialSystem();
        Universe.CurrentSystem.All.UnsafeAsList().AddRange(vehicles);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
}
