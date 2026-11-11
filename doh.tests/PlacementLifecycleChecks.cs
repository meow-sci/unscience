using System;
using MeowSci.DohLib;
using MeowSci.DohLib.Placement;
using MeowSci.KsaAbstractions.Persistence;

internal static partial class Checks
{
    private static void PlacementLifecycleChecks()
    {
        var session = new PlacementSession();
        object world = new();
        session.Arm(world);
        long generation = session.Generation;
        Require(session.BeginCommit(generation, world), "armed placement accepts one pending click");
        Require(!session.BeginCommit(generation, world), "double click cannot queue a second kitten");
        session.Finish(false);
        Require(session.Armed && !session.Pending, "a miss or failed spawn keeps placement armed");
        Require(session.BeginCommit(generation, world), "placement can retry after a miss");
        session.Finish(true);
        Require(!session.IsCurrent(generation, world), "successful placement disarms the one-shot gesture");
        session.Arm(world);
        generation = session.Generation;
        Require(!session.IsCurrent(generation, new object()), "a replacement scene rejects stale requests");
        session.Cancel(); session.Arm(world);
        Require(!session.IsCurrent(generation, world), "a rearmed gesture rejects earlier previews/clicks in the same world");

        var mouse = new PlacementMouseCapture();
        Require(!mouse.Consume(0, true, false), "UI or existing native drags retain their clicks");
        Require(!mouse.Consume(0, false, false), "unowned release passes through");
        Require(mouse.Consume(0, true, true), "an eligible world click is consumed");
        Require(mouse.Consume(0, false, false), "owned release is consumed after success/cancellation");
        Require(!mouse.Consume(0, false, false), "native input is restored after the owned button pair");
        Require(mouse.Consume(1, true, true) && mouse.Consume(1, false, false), "right-click cancellation owns both events");

        var submod = new DohSubmod();
        var coordinator = new SceneSaveCoordinator(submod.SaveParticipants);
        SetWorld();
        submod.Placement.Arm(KSA.Universe.CurrentSystem!);
        var oldWorld = KSA.Universe.CurrentSystem;
        long oldGeneration = submod.Placement.Generation;
        submod.Placement.BeginCommit(oldGeneration, oldWorld);
        int cancellations = 0;
        submod.PlacementCancelled = () => cancellations++;
        var saved = RoundTrip(coordinator.Capture());
        Require(!saved.Features["doh"].State.GetRawText().Contains("Placement", StringComparison.OrdinalIgnoreCase),
            "the real doh v1 adapter never serializes an armed gesture, preview or unexecuted click");
        Load(coordinator, saved);
        Require(!submod.Placement.Armed && !submod.Placement.Pending && cancellations == 1,
            "real adapter reset cancels pending placement and releases its owner before reconstruction");
        Require(!submod.Placement.IsCurrent(oldGeneration, oldWorld), "an old-world queued click cannot execute after replay");
        Require(KSA.Universe.CurrentSystem!.All.UnsafeAsList().Count == 0,
            "replaying an armed save never creates a kitten");
        foreach (SaveDocument? document in new SaveDocument?[] { saved, saved, new(), null })
        {
            submod.Placement.Arm(KSA.Universe.CurrentSystem!);
            Load(coordinator, document);
            Require(!submod.Placement.Armed && !submod.Placement.Pending,
                "repeated, cross-scene, vanilla and new-scene loads all cancel placement");
        }
    }
}
