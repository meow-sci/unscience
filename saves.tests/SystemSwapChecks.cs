using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

/// <summary>KSA 5916 star-system switching: LoadIntoRunning, queued swaps, joins and uninstalled systems.</summary>
internal static partial class Program
{
    private static readonly string[] Joins =
        { "join nearest-orbit-and-performance", "join orbit", "join vehicle", "join cloth" };

    /// <summary>Our Program.SwapSystem prefix resets at a joined boundary, then KSA joins and tears down.</summary>
    private static string[] SwapReset => Seq(Joins, "clear pending", "reset", "clear pending", Joins, "native teardown");

    /// <summary>The swapped-in DeserializeSave joins without a second reset, then KSA reconstructs.</summary>
    private static string[] SwappedLoad => Seq(Joins, "clear pending", Joins, "native destroy", "native reconstructed", "restore");

    private static void ContainedSystemLoad()
    {
        int failures = 0;
        NativeSaveHooks.LoadFailed = _ => failures++;
        LoadNow(SaveIn("contained"));
        Equal(Seq("preflight", "native read", Joins, "clear pending", "reset", "clear pending", Joins,
            "native destroy", "native reconstructed", "restore", "native menus closed", "finished"));
        Check(failures == 0 && NativeSaveHooks.LastLoadError == null, "a save whose bodies are all running is not refused");
        Check(Universe.CurrentSystem!.Id == "system", "loaded into the running system without a switch");

        // Systems KSA would switch to first are still refused before reset when deserialized in place.
        var other = new UniverseData();
        other.CelestialSystems[0].Id.Id = "other";
        Trace.Events.Clear();
        bool refused = false;
        try { Universe.DeserializeSave(other); PhysicsFrameHook.ReplayPending(); }
        catch (InvalidDataException ex) { refused = ex.Message.Contains("'other'"); }
        Check(refused && failures == 1 && !Trace.Events.Contains("reset"), "a non-contained direct load is refused before reset");
    }

    private static void QueuedSwapLoad()
    {
        int failures = 0;
        NativeSaveHooks.LoadFailed = _ => failures++;
        LoadNow(SaveIn("other"));
        Equal("preflight", "native read", "native swap queued");
        Check(failures == 0 && NativeSaveHooks.LastLoadError == null, "a queued switch is not a load failure");
        Check(Universe.CurrentSystem!.Id == "system", "the old world stays until KSA applies the switch");

        Trace.Events.Clear();
        SaveSystemSwap.OnFrame(); // native: after ImGui.Render
        Equal(Seq(SwapReset, "native new system other", SwappedLoad, "native menus closed", "finished"));
        Check(failures == 0 && NativeSaveHooks.LastLoadError == null, "the switched load completes without failure");
        Check(!PhysicsFrameHook.HasPendingWorldChange, "the swapped-in save is applied before OnGameLoaded, not deferred");
    }

    private static void SwapSidecarRoundTrip()
    {
        string scene = "saved scene";
        int resets = 0;
        bool resetSawOldWorld = false;
        var restored = new List<string>();
        var participant = new SaveParticipant<string>("fixture-scene", () => scene, () =>
        {
            resets++;
            resetSawOldWorld = Universe.CurrentSystem!.Id == "system" && !Trace.Events.Contains("native teardown");
            scene = "";
        }, (value, _) => { restored.Add(value); scene = value; });
        var coordinator = new SceneSaveCoordinator(new[] { participant });
        SaveDocument swapDocument = Copy(coordinator.Capture());
        SaveDocument legacy = Copy(coordinator.Capture());
        legacy.Features["future-feature"] = new SaveFeature { Version = 9, State = SaveJson.ToElement(1) };
        SaveDocument next = legacy;
        NativeSaveHooks.Loading = _ => coordinator.PrepareLoad(next);
        NativeSaveHooks.Resetting = coordinator.ResetWorld;
        NativeSaveHooks.Restoring = coordinator.RestoreWorld;
        NativeSaveHooks.LoadFinished = coordinator.FinishLoad;
        NativeSaveHooks.LoadWarning = coordinator.Report;
        NativeSaveHooks.LoadFailed = error => coordinator.MarkFailure(error.Message);
        LoadNow(new UncompressedSave());
        Check(coordinator.RetainedCount == 1, "a same-system load retained a record this build cannot restore");

        scene = "live scene";
        next = swapDocument;
        resets = 0;
        restored.Clear();
        LoadNow(SaveIn("other"));
        // An auto save in the frames before KSA applies the switch still describes the live scene.
        SaveDocument gap = coordinator.Capture();
        Check(gap.Features.ContainsKey("future-feature") && gap.Features["fixture-scene"].State.GetString() == "live scene",
            "capture before the switch keeps the live scene and its retained records");
        Check(resets == 0 && restored.Count == 0, "nothing is reset or restored before KSA switches");

        Trace.Events.Clear();
        SaveSystemSwap.OnFrame();
        Check(resets == 1 && resetSawOldWorld, "every participant resets once, before KSA's teardown");
        Check(restored.SequenceEqual(new[] { "saved scene" }), "the switched save's sidecar restores exactly once");
        Check(coordinator.Status.StartsWith("Restored 1") && coordinator.Messages.Count == 0, "no false failure: " + coordinator.Status);
        Check(Copy(coordinator.Capture()).Features["fixture-scene"].State.GetString() == "saved scene", "the restored state saves again");
    }

    private static void NotInstalledLoad()
    {
        Exception? reported = null;
        NativeSaveHooks.LoadFailed = error => reported = error;
        LoadNow(SaveIn("missing"));
        Equal("preflight", "native read", "native not installed alert", "finished");
        Check(reported is InvalidDataException && reported.Message.Contains("'missing'") && reported.Message.Contains("not installed")
            && !reported.Message.Contains("could not read"), "accurate failure: " + reported?.Message);
        Check(ReferenceEquals(NativeSaveHooks.LastLoadError, reported), "failure visible once");
    }

    private static void JoinSwap()
    {
        // Joining a host in the running system keeps the existing deferral to the frame boundary.
        var local = new UniverseData();
        SaveSystemSwap.RequestJoin("system", () => Universe.DeserializeSave(local));
        Equal();
        PhysicsFrameHook.ReplayPending();
        Check(Trace.Events.Contains("reset") && Trace.Events.Contains("restore"), "same-system join replays at the boundary");

        Trace.Events.Clear();
        var host = SystemData("other");
        SaveSystemSwap.RequestJoin("other", () => { Universe.DeserializeSave(host); Trace.Events.Add("native joined"); });
        Equal("native swap queued");
        Trace.Events.Clear();
        SaveSystemSwap.OnFrame();
        Equal(Seq(SwapReset, "native new system other", SwappedLoad, "native joined"));
        Check(!PhysicsFrameHook.HasPendingWorldChange, "the host world is applied before the join completes");

        Trace.Events.Clear();
        Universe.DeserializeSave(host);
        Equal();
        PhysicsFrameHook.ReplayPending();
        Check(Trace.Events.Contains("reset"), "no stale switch state skips a later reset");
    }

    private static void JoinReplacesQueuedSwap()
    {
        var failures = new List<Exception>();
        NativeSaveHooks.LoadFailed = failures.Add;
        LoadNow(SaveIn("other"));
        var host = SystemData("other");
        SaveSystemSwap.RequestJoin("other", () => Universe.DeserializeSave(host)); // KSA keeps the latest request
        Trace.Events.Clear();
        SaveSystemSwap.OnFrame();
        Equal(Seq(SwapReset, "native new system other", "finished", SwappedLoad));
        Check(failures.Count == 1 && failures[0].Message.Contains("did not apply"), "the replaced save load is reported once");
    }

    private static void WontFitSwap()
    {
        KSA.Program.Instance.SwapWontFit = true;
        LoadNow(SaveIn("other"));
        Trace.Events.Clear();
        SaveSystemSwap.OnFrame();
        Equal(Seq(SwapReset, "native restart into other"));
        // KSA exits here. Were it to continue, a later load must not inherit the swap's reset.
        Trace.Events.Clear();
        Universe.DeserializeSave(new UniverseData());
        Equal();
        PhysicsFrameHook.ReplayPending();
        Check(Trace.Events.Contains("reset"), "a later load resets normally");
    }

    private static void StaleSwapReset()
    {
        KSA.Program.Instance.SwapSystem(new SystemTemplate("other")); // a caller with no continuation
        Check(Trace.Events.IndexOf("reset") < Trace.Events.IndexOf("native teardown"), "even a direct swap resets first");
        PhysicsFrameHook.RunBeforePhysics(); // the next frame boundary
        Trace.Events.Clear();
        var data = SystemData("other");
        SaveSystemSwap.RequestJoin("other", () => Universe.DeserializeSave(data));
        Equal();
        PhysicsFrameHook.ReplayPending();
        Check(Trace.Events.Contains("reset"), "a later same-system load is deferred and resets");
    }

    private static void SwapWithoutSeams()
    {
        // The 5901 hook set (no RequestLoad / SwapSystem seams) must fail these same checks.
        _harmony.Unpatch(AccessTools.Method(typeof(KSA.Program), nameof(KSA.Program.SwapSystem)), HarmonyPatchType.All, _harmony.Id);
        _harmony.Unpatch(AccessTools.Method(typeof(SaveSystemSwap), nameof(SaveSystemSwap.RequestLoad)), HarmonyPatchType.All, _harmony.Id);
        try
        {
            Exception? reported = null;
            NativeSaveHooks.LoadFailed = error => reported = error;
            LoadNow(SaveIn("other"));
            Check(reported?.Message.Contains("could not read") == true, "old hooks report a false read failure");
            Trace.Events.Clear();
            SaveSystemSwap.OnFrame();
            Check(Trace.Events.Contains("native teardown") && !Trace.Events.Contains("reset"), "old hooks let KSA tear down before any reset");
            Check(PhysicsFrameHook.HasPendingWorldChange && Trace.Events.Contains("native menus closed")
                && !Trace.Events.Contains("native reconstructed"), "old hooks defer the load past OnGameLoaded");
        }
        finally
        {
            NativeSaveHooks.Remove(_harmony);
            NativeSaveHooks.Apply(_harmony);
        }
    }

    private static UncompressedSave SaveIn(string system) => new() { UniverseData = SystemData(system) };

    private static UniverseData SystemData(string system)
    {
        var data = new UniverseData();
        data.CelestialSystems[0].Id.Id = system;
        return data;
    }

    private static SaveDocument Copy(SaveDocument document) => SaveJson.FromElement<SaveDocument>(SaveJson.ToElement(document));

    private static string[] Seq(params object[] parts) =>
        parts.SelectMany(part => part as string[] ?? new[] { (string)part }).ToArray();
}
