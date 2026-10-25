using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSA;
using MeowSci.GarrysTorchLib;
using MeowSci.KsaAbstractions;

/// <summary>Actual Harmony composition, rather than a hand-built instruction-list test.</summary>
internal static class FrameHookInteropChecks
{
    private static readonly MethodInfo Frame = AccessTools.Method(typeof(KSA.Program), "PrepareFrame");
    private static readonly MethodInfo Hook = AccessTools.Method(typeof(PhysicsFrameHook), "Transpile");
    private static readonly MethodInfo Dispatch = AccessTools.Method(typeof(PhysicsFrameHook), "GetStepAndDispatch");
    private static bool _rejectHook;
    private static bool _rejectRemoval;

    public static void Run()
    {
        // Patch after JIT warmup, just as another mod can prepare methods before Unscience loads.
        new KSA.Program().RunFrame(0.1);
        CheckOriginalOrderingFailure();
        foreach (int priority in new[] { Priority.Normal, Priority.First })
            CheckProfilerInstalledFirst(priority);
        CheckProfilerInstalledLast();
        CheckFailedInstall();
        CheckFailedRemoval();
        Console.WriteLine("PASS: production frame-hook Harmony composition, reapply and rollback");
    }

    private static void CheckOriginalOrderingFailure()
    {
        var profiler = new Harmony("frame-tests.original-profiler");
        var ours = new Harmony("frame-tests.original-hook");
        profiler.Patch(Frame, transpiler: new HarmonyMethod(typeof(FrameHookInteropChecks), nameof(ProfileCalls)));
        try
        {
            bool rejected = false;
            // The pre-fix installer used this default-priority configuration. Reproduce its
            // real Harmony exception so the compatibility test cannot pass with a no-op profiler.
            try { ours.Patch(Frame, transpiler: new HarmonyMethod(Hook)); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException inner
                && inner.Message.Contains("unexpected PrepareFrame solver sequence", StringComparison.Ordinal))
            { rejected = true; }
            Require(rejected, "original default-priority installer reproduces wrapped sequence-guard failure");
        }
        finally
        {
            ours.Unpatch(Frame, Hook);
            profiler.UnpatchAll(profiler.Id);
        }
    }

    private static void CheckProfilerInstalledFirst(int priority)
    {
        var profiler = new Harmony("frame-tests.profiler-first." + priority);
        var ours = new Harmony("frame-tests.hook-first." + priority);
        profiler.Patch(Frame, transpiler: new HarmonyMethod(typeof(FrameHookInteropChecks), nameof(ProfileCalls)) { priority = priority });
        try
        {
            GarrysTorchPatches.Apply(ours);
            GarrysTorchPatches.Apply(ours);
            try
            {
                var hooks = Harmony.GetPatchInfo(Frame).Transpilers.Where(p => p.PatchMethod == Hook).ToArray();
                Require(hooks.Length == 1,
                    "reapply does not duplicate the shared transpiler");
                Require(hooks[0].priority == Priority.First && hooks[0].before.Contains(profiler.Id),
                    "validate before preinstalled profiling owners even at First priority");
                CheckFrame();
                // Harmony regenerates the entire chain when the earlier owner is removed.
                profiler.Unpatch(Frame, AccessTools.Method(typeof(FrameHookInteropChecks), nameof(ProfileCalls)));
                CheckFrame(expectProfiles: false);
            }
            finally { GarrysTorchPatches.Remove(ours); }
            CheckUnpatched();
        }
        finally
        {
            GarrysTorchPatches.Remove(ours);
            profiler.UnpatchAll(profiler.Id);
        }
    }

    private static void CheckProfilerInstalledLast()
    {
        var profiler = new Harmony("frame-tests.profiler-last");
        var ours = new Harmony("frame-tests.hook-last");
        GarrysTorchPatches.Apply(ours);
        try
        {
            profiler.Patch(Frame, transpiler: new HarmonyMethod(typeof(FrameHookInteropChecks), nameof(ProfileCalls)));
            CheckFrame();
            GarrysTorchPatches.Remove(ours);
            Universe.Events.Clear();
            new KSA.Program().RunFrame(0.25);
            Require(!Universe.Events.Contains("weld") && Universe.Events.Contains("profile apply orbit"),
                "removing our hook preserves the foreign profiling transpiler");
            GarrysTorchPatches.Apply(ours);
            CheckFrame();
        }
        finally
        {
            GarrysTorchPatches.Remove(ours);
            profiler.UnpatchAll(profiler.Id);
        }
        CheckUnpatched();
    }

    private static void CheckFailedInstall()
    {
        var foreign = new Harmony("frame-tests.reject-dispatch");
        var ours = new Harmony("frame-tests.failed-hook");
        MethodInfo reject = AccessTools.Method(typeof(FrameHookInteropChecks), nameof(RejectDispatch));
        foreign.Patch(Frame, transpiler: new HarmonyMethod(reject));
        try
        {
            _rejectHook = true;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                bool rejected = false;
                using var output = new StringWriter();
                TextWriter previous = Console.Out;
                Console.SetOut(output);
                try { GarrysTorchPatches.Apply(ours); }
                catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
                { rejected = true; }
                finally { Console.SetOut(previous); }
                Require(rejected && !PhysicsFrameHook.IsApplied, "failed generation preserves the original exception and stays unapplied");
                Require(!Harmony.GetPatchInfo(Frame).Transpilers.Any(p => p.PatchMethod == Hook),
                    "three failed startup retries leave no shared transpiler registration");
                Require(output.ToString().Contains("fixture foreign transpiler rejects dispatch wrapper", StringComparison.Ordinal)
                    && output.ToString().Contains("owner='frame-tests.reject-dispatch'", StringComparison.Ordinal)
                    && output.ToString().Contains("priority=400", StringComparison.Ordinal)
                    && output.ToString().Contains("before=[", StringComparison.Ordinal),
                    "failure reports include the inner cause and foreign owner/order diagnostics");
            }
            Require(Harmony.GetPatchInfo(Frame).Transpilers.Any(p => p.PatchMethod == reject),
                "failed installation retains foreign patches");
            Universe.Events.Clear();
            new KSA.Program().RunFrame(0.25);
            Require(!Universe.Events.Contains("weld"), "failed installation leaves the previous caller executable");
            _rejectHook = false;
            GarrysTorchPatches.Apply(ours);
            CheckFrame(expectProfiles: false);
        }
        finally
        {
            _rejectHook = false;
            GarrysTorchPatches.Remove(ours);
            foreign.UnpatchAll(foreign.Id);
        }
        CheckUnpatched();
    }

    private static void CheckFrame(bool expectProfiles = true)
    {
        Universe.Reset();
        GarrysTorchSubmod.Instance = new();
        PhysicsFrameHook.Enqueue(() => Universe.Events.Add("queued edit"));
        PhysicsFrameHook.EnqueueWorldChange(() =>
        {
            Require(PhysicsFrameHook.IsReplayingWorldChange, "world change replay guard under composition");
            Universe.Time = 700;
            Universe.Events.Add("world load");
        });
        SimStep step = new KSA.Program().RunFrame(0.25);
        string[] events = Universe.Events.Where(e => !e.StartsWith("profile ", StringComparison.Ordinal)).ToArray();
        Require(string.Join(",", events) ==
            "apply orbit,apply vehicle,apply cloth,join orbit readers,world load,get step,queued edit,weld,queue cloth,queue vehicle,queue orbit",
            "foreign wrappers preserve the exact result/world-change/step/edit/weld/snapshot boundary");
        Require(step.PreviousTime.Seconds == 700 && GarrysTorchSubmod.Instance.StateTime == step.PreviousTime,
            "world replacement precedes step computation and welds retain PreviousTime");
        Require(GarrysTorchSubmod.Instance.Updates == 1, "reapply dispatches welding once");
        Require(Universe.Events.Contains("profile apply orbit") == expectProfiles,
            "foreign wrappers run while their owner is installed");
    }

    private static void CheckFailedRemoval()
    {
        var foreign = new Harmony("frame-tests.reject-removal");
        var ours = new Harmony("frame-tests.remove-hook");
        MethodInfo reject = AccessTools.Method(typeof(FrameHookInteropChecks), nameof(RejectDispatch));
        GarrysTorchPatches.Apply(ours);
        foreign.Patch(Frame, transpiler: new HarmonyMethod(reject));
        try
        {
            _rejectRemoval = true;
            bool staleMutation = false;
            bool staleWorld = false;
            PhysicsFrameHook.Enqueue(() => staleMutation = true);
            PhysicsFrameHook.EnqueueWorldChange(() => staleWorld = true);
            bool rejected = false;
            try { PhysicsFrameHook.Remove(ours); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
            { rejected = true; }
            Require(rejected && PhysicsFrameHook.IsApplied
                && Harmony.GetPatchInfo(Frame).Transpilers.Any(p => p.PatchMethod == Hook),
                "failed unpatch retains Harmony registration and its truthful installed state");
            Universe.Reset();
            new KSA.Program().RunFrame(0.25);
            Require(!staleMutation && !staleWorld, "failed removal still discards pending mutation and world replacement");
            _rejectRemoval = false;
            GarrysTorchPatches.Remove(ours);
            Require(!PhysicsFrameHook.IsApplied, "later successful removal releases installed state");
        }
        finally
        {
            _rejectRemoval = false;
            GarrysTorchPatches.Remove(ours);
            foreign.UnpatchAll(foreign.Id);
        }
    }

    private static void CheckUnpatched()
    {
        Universe.Reset();
        new KSA.Program().RunFrame(0.25);
        Require(!PhysicsFrameHook.IsApplied && !Universe.Events.Contains("weld"), "unload restores original scheduling");
        Require(!Harmony.GetPatchInfo(Frame).Transpilers.Any(p => p.PatchMethod == Hook), "unload removes our registration");
    }

    private static IEnumerable<CodeInstruction> ProfileCalls(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var code in instructions)
        {
            if (code.operand is MethodInfo target && target.DeclaringType == typeof(Universe))
            {
                var wrapper = AccessTools.Method(typeof(FrameHookInteropChecks), "Profile" + target.Name);
                if (wrapper != null) code.operand = wrapper;
            }
            yield return code;
        }
    }

    private static IEnumerable<CodeInstruction> RejectDispatch(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.ToList();
        if (_rejectHook && codes.Any(code => code.Calls(Dispatch)))
            throw new InvalidOperationException("fixture foreign transpiler rejects dispatch wrapper");
        if (_rejectRemoval && !codes.Any(code => code.Calls(Dispatch)))
            throw new InvalidOperationException("fixture foreign transpiler rejects hook removal");
        return codes;
    }

    private static void ProfileApplyOrbitSolvers() { Universe.Events.Add("profile apply orbit"); Universe.ApplyOrbitSolvers(); }
    private static void ProfileApplyVehicleSolvers() { Universe.Events.Add("profile apply vehicle"); Universe.ApplyVehicleSolvers(); }
    private static void ProfileApplyClothSolvers() { Universe.Events.Add("profile apply cloth"); Universe.ApplyClothSolvers(); }
    private static SimStep ProfileGetJobSimStep(double dt) { Universe.Events.Add("profile step"); return Universe.GetJobSimStep(dt); }
    private static void ProfileExecuteNextClothSolvers(double dt, SimStep step) { Universe.Events.Add("profile queue cloth"); Universe.ExecuteNextClothSolvers(dt, step); }
    private static void ProfileExecuteNextVehicleSolvers(double dt, SimStep step) { Universe.Events.Add("profile queue vehicle"); Universe.ExecuteNextVehicleSolvers(dt, step); }
    private static void ProfileExecuteNextOrbitSolvers(double dt, SimStep step) { Universe.Events.Add("profile queue orbit"); Universe.ExecuteNextOrbitSolvers(dt, step); }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
