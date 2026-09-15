using System;
using System.Linq;
using HarmonyLib;
using KSA;
using MeowSci.KitchenSinkLib;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

internal static class SaveChecks
{
    private const string GLoadId = "kitchen-sink-g-load";
    private static int _checks;

    public static void Run(Harmony harmony)
    {
        var submod = new KitchenSinkSubmod();
        var coordinator = new SceneSaveCoordinator(submod.SaveParticipants);
        var axle = new Vehicle("Axle");
        var cradle = new Vehicle("Cradle");
        SetWorld(axle, cradle);
        GLoadProtection.Add(axle);
        GLoadProtection.Add(cradle);
        IvaForceRender.Enabled = true;
        var savedA = SaveJson.FromElement<SaveDocument>(SaveJson.ToElement(coordinator.Capture()));
        Require(savedA.Features["kitchen-sink"].State.GetBoolean(), "legacy IVA payload stays boolean");
        Require(savedA.Features[GLoadId].Version == 1, "new G-load record has its own version");
        Require(SavedIds(savedA).SequenceEqual(new[] { "Axle", "Cradle" }), "JSON round-trip captures all targets");

        coordinator.PrepareLoad(savedA);
        Require(GLoadProtection.Contains(axle), "prepare does not mutate the world");
        coordinator.ResetWorld();
        Require(GLoadProtection.Snapshot().Length == 0 && !IvaForceRender.Enabled && submod.PickerResets == 1,
            "production reset releases registrations and picker before reconstruction");
        var newAxle = new Vehicle("Axle");
        var newCradle = new Vehicle("Cradle");
        SetWorld(newAxle, newCradle);
        coordinator.RestoreWorld();
        coordinator.FinishLoad();
        Require(GLoadProtection.Contains(newAxle) && GLoadProtection.Contains(newCradle), "replay binds reconstructed vehicles");
        Require(!GLoadProtection.Contains(axle) && !GLoadProtection.Contains(cradle), "old objects remain unprotected");
        var protectedState = new VehicleUpdateState(newAxle);
        PhysicsBubble.Detect(protectedState);
        Require(protectedState.DestructionEvent == null, "restored registration actually gates the production patch");
        Require(IvaForceRender.Enabled, "IVA state also restores");

        GLoadProtection.Remove(newAxle);
        var savedB = coordinator.Capture();
        Require(SavedIds(savedB).SequenceEqual(new[] { "Cradle" }), "deletions are reflected in subsequent saves");
        Load(coordinator, savedA, new Vehicle("Axle"), new Vehicle("Cradle"));
        Require(GLoadProtection.Snapshot().Length == 2, "A restores both targets after edits");
        Load(coordinator, savedB, new Vehicle("Axle"), new Vehicle("Cradle"));
        Require(GLoadProtection.Snapshot().Single().Id == "Cradle", "B restores its distinct target list");
        Load(coordinator, savedA, new Vehicle("Axle"), new Vehicle("Cradle"));
        Load(coordinator, savedA, new Vehicle("Axle"), new Vehicle("Cradle"));
        Require(GLoadProtection.Snapshot().Length == 2, "A-B-A and repeated loads never duplicate targets");

        var legacy = new SaveDocument();
        legacy.Features["kitchen-sink"] = new SaveFeature { State = SaveJson.ToElement(true) };
        Load(coordinator, legacy, new Vehicle("Axle"));
        Require(IvaForceRender.Enabled && GLoadProtection.Snapshot().Length == 0, "legacy boolean-only saves restore without inventing protection");
        GLoadProtection.Add(VehicleProvider.FindVehicle("Axle")!);
        Load(coordinator, null, new Vehicle("Axle"));
        Require(!IvaForceRender.Enabled && GLoadProtection.Snapshot().Length == 0, "vanilla loads clear old setup");

        KSA.Program.ControlledVehicle = new Vehicle("Fallback");
        Load(coordinator, savedA);
        Require(GLoadProtection.Snapshot().Length == 0 && coordinator.RetainedCount == 1,
            "missing targets warn and retain the record without a controlled-vehicle fallback");
        Require(SavedIds(coordinator.Capture()).Length == 2, "partial restore cannot silently erase saved targets");
        Load(coordinator, savedA, new Vehicle("Axle"), new Vehicle("Axle"), new Vehicle("Cradle") { IsDisposed = true });
        Require(GLoadProtection.Snapshot().Length == 0 && coordinator.Messages.Any(message => message.Contains("ambiguous")),
            "ambiguous and disposed targets are skipped with diagnostics");

        foreach (var invalid in new[] { new[] { "" }, new[] { "Axle", "Axle" }, new[] { (string)null! }, new string[10001] })
        {
            var bad = new SaveDocument();
            bad.Features[GLoadId] = new SaveFeature { State = SaveJson.ToElement(invalid) };
            Load(coordinator, bad, new Vehicle("Axle"));
            Require(coordinator.RetainedCount == 1 && GLoadProtection.Snapshot().Length == 0,
                "invalid saved arrays are rejected before replay and retained");
        }

        GLoadProtectionPatches.Remove(harmony);
        Load(coordinator, savedA, new Vehicle("Axle"), new Vehicle("Cradle"));
        Require(coordinator.RetainedCount == 1 && coordinator.Messages.Any(message => message.Contains("patch is unavailable")),
            "unavailable patch reports failure instead of falsely restoring protection");
        GLoadProtectionPatches.Apply(harmony);
        Load(coordinator, savedA, new Vehicle("Axle"), new Vehicle("Cradle"));
        Require(coordinator.RetainedCount == 0 && GLoadProtection.Snapshot().Length == 2,
            "saved protection is recoverable after patch availability returns");

        var duplicate = new Vehicle("Duplicate");
        GLoadProtection.Clear();
        GLoadProtection.Add(duplicate);
        SetWorld(duplicate, new Vehicle("Duplicate"));
        var freshCoordinator = new SceneSaveCoordinator(submod.SaveParticipants);
        var failedCapture = freshCoordinator.Capture();
        Require(!failedCapture.Features.ContainsKey(GLoadId) && failedCapture.Warnings.Any(message => message.Contains("ambiguous")),
            "capture does not write an unresolvable ambiguous target as a successful record");
        duplicate.IsDisposed = true;
        Require(SavedIds(freshCoordinator.Capture()).Length == 0, "disposed targets are excluded from capture");
        coordinator.ResetWorld();
        KSA.Program.ControlledVehicle = null;
        Console.WriteLine($"PASS: {_checks} Kitchen Sink save/restore checks.");
    }

    private static string[] SavedIds(SaveDocument document) => SaveJson.FromElement<string[]>(document.Features[GLoadId].State);

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
