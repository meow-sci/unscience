using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

internal static partial class Program
{
    private static int _passed;

    private static void Main()
    {
        var harmony = new Harmony("MeowSci.Saves.Tests");
        NativeSaveHooks.Apply(harmony);
        try
        {
            Run("capture and write after native success", CaptureAndWrite);
            Run("failed native capture and write skip sidecar callbacks", FailedSave);
            Run("native write returning false reports failure without sidecar", WriteReturnsFalse);
            Run("load joins before reset and restores before menus", NormalLoad);
            Run("editor refusal preserves scene", EditorRefusal);
            Run("unreadable save reports failure and only clears load context", ReadFailure);
            Run("reconstruction failure never restores and clears context", ReconstructionFailure);
            Run("direct load resets baseline and restores", DirectLoad);
            Run("no system preserves scene", NoSystem);
            Run("new system validates before cleanup", NewSystem);
            Run("callback failures are isolated and native exceptions survive", CallbackFailures);
            Run("repeated loads have independent transactions", RepeatedLoads);
            Run("world replacement waits for frame boundary and latest request wins", DeferredLoads);
            Run("structural preflight refusal preserves old scene and reports once", NativePreflight);
            Run("vehicles KSA skips load with warnings instead of refusal", SkippedVehiclePreflight);
            Run("skipped vehicle stays unresolved through the real coordinator", SkippedVehicleRoundTrip);
            Run("worker join failure never reaches native destruction", JoinFailure);
        }
        finally { NativeSaveHooks.Remove(harmony); }
        Run("removal restores native methods", Removed);
        Console.WriteLine($"Native save lifecycle: {_passed} checks passed.");
        StorageTests.Run();
        RetiredFeatureTests.Run();
        PartReferenceTests.Run();
        KittenPhaseTests.Run();
        MaterialOwnershipTests.Run();
    }

    private static void Run(string name, Action test)
    {
        Trace.Events.Clear();
        PhysicsFrameHook.Pending.Clear();
        PhysicsFrameHook.ClearPendingWorldChange();
        KSA.Program.IsEditorOpen = false;
        Universe.CurrentSystem = new CelestialSystem();
        NativeSaveHooks.LoadFailed = null;
        NativeSaveHooks.WriteFailed = _ => Trace.Events.Add("write failed");
        JobSystems.NearestOrbitAndPerformanceWorker.Fail = false;
        NativeSaveHooks.Capturing = _ => Trace.Events.Add("capture");
        NativeSaveHooks.Written = _ => Trace.Events.Add("write");
        NativeSaveHooks.Loading = _ => Trace.Events.Add("preflight");
        NativeSaveHooks.Resetting = () => Trace.Events.Add("reset");
        NativeSaveHooks.Restoring = () => Trace.Events.Add("restore");
        NativeSaveHooks.LoadFinished = () => Trace.Events.Add("finished");
        NativeSaveHooks.LoadWarning = _ => Trace.Events.Add("load warning");
        test();
        _passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void CaptureAndWrite()
    {
        var save = new UncompressedSave();
        GameSave? captured = null;
        NativeSaveHooks.Capturing += item => captured = item;
        save.Populate();
        save.Write();
        Equal("native capture", "capture", "native write", "write");
        Check(ReferenceEquals(save, captured), "capture uses exact save object");
    }

    private static void FailedSave()
    {
        var save = new UncompressedSave { FailCapture = true, ThrowWrite = true };
        Throws(save.Populate, "native capture failed");
        Throws(() => save.Write(), "native write failed");
        Equal("native capture", "native write");
    }

    private static void WriteReturnsFalse()
    {
        var save = new UncompressedSave { FailWrite = true };
        save.Populate();
        Check(!save.Write(), "native failure result survives the postfix");
        Equal("native capture", "capture", "native write", "write failed");
    }

    private static void NormalLoad()
    {
        PhysicsFrameHook.Pending.Enqueue(() => throw new Exception("old action"));
        NativeSaveHooks.Resetting += () => PhysicsFrameHook.Pending.Enqueue(() => { });
        LoadNow(new UncompressedSave());
        Equal("preflight", "native read", "join nearest-orbit-and-performance", "join orbit", "join vehicle", "join cloth", "clear pending",
            "reset", "clear pending", "join nearest-orbit-and-performance", "join orbit", "join vehicle", "join cloth", "native destroy",
            "native reconstructed", "restore", "native menus closed", "finished");
        Check(PhysicsFrameHook.Pending.Count == 0, "old-world and reset-queued edits cleared");
    }

    private static void EditorRefusal()
    {
        KSA.Program.IsEditorOpen = true;
        new UncompressedSave().Load();
        Equal("refused");
    }

    private static void ReadFailure()
    {
        int failures = 0;
        NativeSaveHooks.LoadFailed = ex => { Check(ex is System.IO.InvalidDataException, "unread save reported"); failures++; };
        LoadNow(new UncompressedSave { FailRead = true });
        Equal("preflight", "native read", "finished");
        Check(failures == 1 && NativeSaveHooks.LastLoadError is System.IO.InvalidDataException, "unread save visible once");
        LoadNow(new UncompressedSave());
        Check(failures == 1 && NativeSaveHooks.LastLoadError == null, "next successful load clears the failure");
    }

    private static void ReconstructionFailure()
    {
        var save = new UncompressedSave { UniverseData = new UniverseData { FailReconstruction = true } };
        Throws(() => LoadNow(save), "native reconstruction failed");
        Check(Trace.Events.Contains("reset"), "old scene cleared at native destruction");
        Check(!Trace.Events.Contains("restore"), "failed reconstruction not replayed");
        Check(Trace.Events.Last() == "finished", "context finalized");
    }

    private static void DirectLoad()
    {
        Universe.DeserializeSave(new UniverseData());
        Equal();
        PhysicsFrameHook.ReplayPending();
        Check(Trace.Events.Contains("reset") && Trace.Events.Contains("restore"), "direct load handled");
        Check(!Trace.Events.Contains("preflight") && !Trace.Events.Contains("finished"), "no fake file context");
    }

    private static void NoSystem()
    {
        Universe.CurrentSystem = null;
        Throws(() => Universe.DeserializeSave(new UniverseData()), "no system");
        Equal();
    }

    private static void NewSystem()
    {
        Throws(() => Universe.LoadSystem("invalid"), "unknown system");
        Equal();
        Universe.LoadSystem("valid");
        Equal();
        PhysicsFrameHook.ReplayPending();
        Equal("join nearest-orbit-and-performance", "join orbit", "join vehicle", "join cloth", "clear pending", "reset", "clear pending", "native new system");
    }

    private static void CallbackFailures()
    {
        NativeSaveHooks.Loading = _ => throw new Exception("preflight callback");
        NativeSaveHooks.Loading += _ => Trace.Events.Add("preflight survived");
        NativeSaveHooks.Resetting = () => throw new Exception("reset callback");
        NativeSaveHooks.Resetting += () => Trace.Events.Add("reset survived");
        NativeSaveHooks.Restoring = () => throw new Exception("restore callback");
        NativeSaveHooks.Restoring += () => Trace.Events.Add("restore survived");
        NativeSaveHooks.LoadFinished = () => throw new Exception("finalizer callback");
        NativeSaveHooks.LoadFinished += () => Trace.Events.Add("finished survived");
        LoadNow(new UncompressedSave());
        foreach (string name in new[] { "preflight", "reset", "restore", "finished" })
            Check(Trace.Events.Contains(name + " survived"), name + " callbacks isolated");
        LoadNow(new UncompressedSave { FailRead = true });
        Check(NativeSaveHooks.LastLoadError is System.IO.InvalidDataException, "unreadable save still reported");
    }

    private static void RepeatedLoads()
    {
        var save = new UncompressedSave();
        LoadNow(save);
        LoadNow(save);
        foreach (string name in new[] { "preflight", "reset", "restore", "finished" })
            Check(Trace.Events.Count(item => item == name) == 2, name + " once per load");
    }

    private static void Removed()
    {
        var save = new UncompressedSave();
        save.Populate();
        save.Write();
        Equal("native capture", "native write");
    }

    private static void DeferredLoads()
    {
        var oldRequest = new UncompressedSave { FailRead = true };
        var latest = new UncompressedSave();
        UncompressedSave? loaded = null;
        NativeSaveHooks.Loading += save => loaded = save;
        oldRequest.Load();
        latest.Load();
        Equal();
        Check(loaded == null, "UI dispatch cannot preflight or clear resources");
        PhysicsFrameHook.ReplayPending();
        Check(ReferenceEquals(loaded, latest), "only latest request replayed");
        Check(!PhysicsFrameHook.IsReplayingWorldChange, "replay guard released");
        Universe.CurrentSystem = null;
        Trace.Events.Clear();
        Universe.LoadSystem("valid");
        Equal("native new system");
    }

    private static void LoadNow(UncompressedSave save)
    {
        save.Load();
        PhysicsFrameHook.ReplayPending();
    }

    private static void JoinFailure()
    {
        JobSystems.NearestOrbitAndPerformanceWorker.Fail = true;
        int failures = 0;
        NativeSaveHooks.LoadFailed = _ => failures++;
        Throws(() => LoadNow(new UncompressedSave()), "join failed");
        Check(failures == 1 && !Trace.Events.Contains("native destroy") && !Trace.Events.Contains("reset"), "old scene preserved before unsafe reset");
        Check(Trace.Events.Last() == "finished", "file transaction unwinds on join failure");
    }

    private static void Throws(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException ex) when (ex.Message == message) { return; }
        throw new Exception("Expected native exception: " + message);
    }

    private static void Equal(params string[] expected) =>
        Check(Trace.Events.SequenceEqual(expected), "event sequence: " + string.Join(", ", Trace.Events));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
