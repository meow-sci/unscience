using System;
using System.Linq;
using KSA;
using MeowSci.DohLib;
using MeowSci.DohLib.Spawning;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.WarPaintLib;

internal static partial class Checks
{
    private static void NativeKillChecks(DohSubmod submod)
    {
        var kittens = Kittens("smite_1", "smite_2");
        SetWorld(kittens);
        var firstSet = submod.Paint(kittens[0], Red);
        WarPaintService.Shared.TryGetPaint(kittens[0], out var firstPaint);
        string preset = firstPaint.PresetName;
        var secondSet = WarPaintService.Shared.Apply(kittens[1], new PaintSettings { Tint = Red }, preset)!.MaterialSet;
        submod.Track(kittens[0], "Calico", firstSet);
        submod.Track(kittens[1], "Calico", secondSet);
        Program.ControlledVehicle = kittens[0];
        Trace.Events.Clear();
        NativeKittenPaintBackend.FailRestore = true;
        Require(submod.Spawner.RequestSmite(kittens[0].Id) && InputEvents.VehicleDestroyBuffer.Count == 1,
            "Smite submits KSA's native destroy input without a mod physics hook");
        Require(submod.Spawner.RequestSmite(kittens[0].Id) && InputEvents.VehicleDestroyBuffer.Count == 1,
            "repeated requests keep one native kill event per object");
        Require(!kittens[0].IsDisposed && !firstSet.IsReleased && Trace.Events.Count == 0,
            "request leaves the current frame's avatar and paint alive without restoring bindings");
        var coordinator = new SceneSaveCoordinator(submod.SaveParticipants.Append(WarPaintService.Shared.SaveParticipant));
        Require(SavedKittens(coordinator.Capture()).Count == 2, "save before native dispatch still includes both kittens");
        InputEvents.ApplyInputEvents();
        Require(kittens[0].IsDisposed && !kittens[1].IsDisposed && Program.ControlledVehicle == null
            && Trace.Events.Contains("destroy smite_1 Kill Abandoned"),
            "native input selects Kill and the built-in cause, with native control handoff");
        Require(!firstSet.IsReleased && !secondSet.IsReleased,
            "paint slots remain owned until post-destruction cleanup");
        Require(SavedKittens(coordinator.Capture()).Count == 1, "dead kittens are omitted even before registry pruning");
        Require(submod.Spawner.PruneDisposed() == 1 && firstSet.IsReleased && !secondSet.IsReleased
            && submod.Registry.Get(kittens[0].Id) == null,
            "pruning releases only the dead kitten's private set, even when both used the same preset");
        Require(!Trace.Events.Any(e => e.StartsWith("restore paint")), "native-dead avatars are never rebound");
        Require(WarPaintService.Shared.Presets.TryGet(preset, out _), "a surviving kitten keeps its reusable preset");
        Require(submod.Spawner.RequestSmiteAll() == 1, "Smite All targets only remaining live tracked kittens");
        InputEvents.ApplyInputEvents();
        submod.Spawner.PruneDisposed();
        WarPaintService.Shared.Update();
        Require(secondSet.IsReleased && submod.Registry.Count == 0 && WarPaintService.Shared.Presets.TryGet(preset, out _),
            "last user releases all private GPU slots while the global preset remains reusable");
        Require(submod.Spawner.PruneDisposed() == 0 && InputEvents.VehicleDestroyBuffer.Count == 0,
            "repeated cleanup and native dispatch leave no pending ownership");
        NativeKittenPaintBackend.FailRestore = false;

        var removed = new KittenEva("same-id"); SetWorld(removed);
        submod.Track(removed, "Calico", null);
        Require(submod.Spawner.RequestSmite(removed.Id), "old-world kill keeps the exact native object");
        removed.IsDisposed = true; // Native scene replacement or an impact wins before input dispatch.
        submod.Registry.Clear();
        var restored = new KittenEva(removed.Id); SetWorld(restored);
        submod.Track(restored, "Calico", null);
        InputEvents.ApplyInputEvents();
        Require(!restored.IsDisposed && submod.Registry.Get(restored.Id) != null,
            "queued input cannot resolve to a newly restored same-name kitten");
        var rocket = new Vehicle("rocket"); SetWorld(rocket);
        submod.Registry.Register(rocket.Id, "Calico", null);
        Require(!submod.Spawner.RequestSmite(rocket.Id) && !submod.Spawner.RequestSmite("missing")
            && !submod.Spawner.RequestSmite(restored.Id) && InputEvents.VehicleDestroyBuffer.Count == 0,
            "non-kittens, missing entries and objects no longer in the native world cannot be killed");
    }

    private static void ConstructionRollbackChecks()
    {
        var orphan = new KittenEva("orphan");
        var stubborn = new KittenEva("stubborn");
        SetWorld(orphan, stubborn);
        KittenSpawner.RemoveOrphanedKitten(Universe.CurrentSystem!, orphan.Id);
        Require(orphan.IsDisposed && !Universe.CurrentSystem!.All.TryGet(orphan.Id, out _),
            "constructor failure still rolls back the partially registered vehicle");
        Universe.FailDestroy = true;
        KittenSpawner.RemoveOrphanedKitten(Universe.CurrentSystem!, stubborn.Id);
        Universe.FailDestroy = false;
        Require(!Universe.CurrentSystem!.All.TryGet(stubborn.Id, out _),
            "failed native constructor rollback cannot leave an incomplete avatar in the world");
    }
}
