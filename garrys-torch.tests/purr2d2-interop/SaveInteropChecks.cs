using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;
using Purr2D2.Runtime;
using static FrameInteropChecks;

internal static class SaveInteropChecks
{
    internal static void Run(bool originalAudit)
    {
        foreach (bool purrFirst in new[] { true, false })
        {
            if (originalAudit) ReproduceEarlyReset(purrFirst);
            else CheckDirectLoads(purrFirst);
        }
        if (!originalAudit) CheckPurrJoinFailure();
    }

    private static void ReproduceEarlyReset(bool purrFirst)
    {
        var purr = new Harmony("purr2d2.interop.reset-original." + purrFirst);
        var ours = new Harmony("MeowSci.Unscience");
        var runtime = new SaberRuntime();
        GameHooks.Runtime = runtime;
        try
        {
            Install(purrFirst, purr, ours);
            Universe.Reset();
            Universe.DeserializeSave(new(900));
            Require(runtime.Resets == 1 && runtime.UnsafeResets == 1
                && !Universe.Events.Contains("native load") && !Universe.Events.Contains("join all"),
                "actual original void Purr prefix resets while production Unscience defers the direct call");
            Console.WriteLine("REPRODUCED: direct DeserializeSave deferred by Unscience still resets Purr before joins (purrFirst=" + purrFirst + ")");
        }
        finally { Cleanup(purr, ours); }
    }

    private static void CheckDirectLoads(bool purrFirst)
    {
        var purr = new Harmony("purr2d2.interop.reset-fixed." + purrFirst);
        var ours = new Harmony("MeowSci.Unscience");
        var runtime = new SaberRuntime();
        GameHooks.Runtime = runtime;
        int callbacks = 0;
        UniverseTime observedTime = default;
        Action<double, UniverseTime> listener = (_, time) => { callbacks++; observedTime = time; Universe.Events.Add("unscience callback"); };
        PhysicsFrameHook.BeforePhysics += listener;
        NativeSaveHooks.Resetting = () => Universe.Events.Add("unscience reset");
        NativeSaveHooks.Restoring = () => Universe.Events.Add("unscience restore");
        try
        {
            Install(purrFirst, purr, ours);
            Universe.Reset();
            bool staleEdit = false;
            PhysicsFrameHook.Enqueue(() => staleEdit = true);
            Universe.DeserializeSave(new(900));
            Require(runtime.Resets == 0 && Universe.Events.Count == 0 && Universe.Time == 100,
                "direct load request has no Purr cleanup/join/native effects while Unscience defers it");
            SimStep step = new KSA.Program().RunFrame(0.25);
            Require(runtime.Resets == 1 && runtime.UnsafeResets == 0 && !staleEdit,
                "accepted replay resets Purr once after workers join and discards old-world mutations");
            Require(callbacks == 1 && step.PreviousTime.Seconds == 900 && observedTime == step.PreviousTime
                && Universe.Steps == 1 && Universe.VehicleDispatches == 1,
                "world replacement precedes the single step/callback/snapshot computed from loaded time");
            RequireOrder("input", "purr settled", "preflight", "join all", "unscience reset", "purr reset",
                "native load", "unscience restore", "get step", "unscience callback", "queue cloth", "queue vehicle", "queue orbit");

            Universe.Reset();
            callbacks = 0;
            int priorResets = runtime.Resets;
            Universe.DeserializeSave(new(901, Reject: true));
            Require(runtime.Resets == priorResets, "rejected data is also deferred without premature Purr cleanup");
            new KSA.Program().RunFrame(0.25);
            Require(runtime.Resets == priorResets && Universe.Time == 100
                && !Universe.Events.Contains("unscience reset") && !Universe.Events.Contains("native load")
                && NativeSaveHooks.LastLoadError is InvalidDataException,
                "production preflight rejection leaves Purr and both native/mod worlds intact");
            Require(callbacks == 1 && observedTime.Seconds == 100, "failed deferred load still schedules the unchanged world");

            Universe.Reset();
            priorResets = runtime.Resets;
            var file = new UncompressedSave();
            file.Load();
            Require(runtime.Resets == priorResets && Universe.Events.Count == 0, "file load defers before reaching any Deserialize prefix");
            new KSA.Program().RunFrame(0.25);
            Require(runtime.Resets == priorResets + 1 && runtime.UnsafeResets == 0 && Universe.Time == 900,
                "production file-load replay reaches Purr cleanup at the joined boundary");
        }
        finally
        {
            PhysicsFrameHook.BeforePhysics -= listener;
            Cleanup(purr, ours);
        }
    }

    private static void CheckPurrJoinFailure()
    {
        var purr = new Harmony("purr2d2.interop.join-failure");
        var runtime = new SaberRuntime();
        GameHooks.Runtime = runtime;
        GameHooks.Install(purr);
        try
        {
            Universe.Reset();
            JobSystems.ThrowOnWaitAll = true;
            bool rejected = false;
            try { Universe.DeserializeSave(new(900)); }
            catch (InvalidOperationException error) when (error.Message == "fixture worker join failure") { rejected = true; }
            Require(rejected && runtime.Resets == 0 && !Universe.Events.Contains("native load")
                && Universe.Events.Any(e => e.StartsWith("purr report ", StringComparison.Ordinal)),
                "standalone actual Purr reset prefix reports join failure and aborts before cleanup/native replacement");
        }
        finally
        {
            JobSystems.ThrowOnWaitAll = false;
            purr.UnpatchAll(purr.Id);
            GameHooks.Runtime = null;
        }
    }

    private static void Install(bool purrFirst, Harmony purr, Harmony ours)
    {
        if (purrFirst) GameHooks.Install(purr);
        NativeSaveHooks.Apply(ours);
        if (!purrFirst) GameHooks.Install(purr);
    }

    private static void Cleanup(Harmony purr, Harmony ours)
    {
        NativeSaveHooks.Remove(ours);
        PhysicsFrameHook.Remove(ours);
        purr.UnpatchAll(purr.Id);
        ours.UnpatchAll(ours.Id);
        GameHooks.Runtime = null;
        NativeSaveHooks.Resetting = NativeSaveHooks.Restoring = null;
    }

    private static void RequireOrder(params string[] names)
    {
        int previous = -1;
        foreach (string name in names)
        {
            int index = Universe.Events.FindIndex(previous + 1, e => e == name);
            Require(index > previous, "cross-mod lifecycle ordering at " + name + ": " + string.Join(",", Universe.Events));
            previous = index;
        }
    }
}
