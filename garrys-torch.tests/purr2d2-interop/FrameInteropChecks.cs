using System.Reflection;
using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;
using Purr2D2.Runtime;

internal static class FrameInteropChecks
{
    internal static readonly MethodInfo Frame = AccessTools.Method(typeof(KSA.Program), "PrepareFrame");
    internal static readonly MethodInfo Hook = AccessTools.Method(typeof(PhysicsFrameHook), "Transpile");
    private static readonly MethodInfo Dispatch = AccessTools.Method(typeof(Universe), nameof(Universe.ExecuteNextVehicleSolvers));
    private static int _callbacks;
    private static int _prefixes;
    private static double _delta;
    private static UniverseTime _stateTime;

    private static void Main(string[] args)
    {
        bool originalAudit = args.Contains("--audit-original", StringComparer.Ordinal);
        Console.WriteLine("Compiled Purr source: " + typeof(FrameInteropChecks).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "Purr2D2SourceDir").Value);
        new KSA.Program().RunFrame(0.25);
        CheckExactSourceTransformation();
        CheckLegacyInstaller(originalAudit);
        foreach (bool purrFirst in new[] { true, false }) CheckCurrentComposition(purrFirst);
        SaveInteropChecks.Run(originalAudit);
        Console.WriteLine(originalAudit
            ? "PASS: original Purr source reproduces vehicle-anchor conflict and premature direct-load reset"
            : "PASS: actual Purr GameHooks + Unscience frame/save hooks compose in both installation orders");
    }

    private static void CheckExactSourceTransformation()
    {
        var original = PatchProcessor.GetOriginalInstructions(Frame);
        var transformed = GameHooks.InsertSettledUpdate(original).ToList();
        Require(transformed.Count(c => c.Calls(Dispatch)) == 0
            && transformed.Count(c => c.Calls(AccessTools.Method(typeof(GameHooks), "PrepareAndDispatchVehicles"))) == 1,
            "actual Purr transformation replaces the Unscience vehicle-dispatch anchor");
        foreach (string name in new[] { "ApplyOrbitSolvers", "ApplyVehicleSolvers", "ApplyClothSolvers", "GetJobSimStep" })
            Require(transformed.Count(c => c.Calls(AccessTools.Method(typeof(Universe), name))) == 1,
                "actual Purr leaves " + name + " untouched");
        Console.WriteLine("PASS: exact Purr source replaces ExecuteNextVehicleSolvers; Apply* and GetJobSimStep remain direct");
    }

    private static void CheckLegacyInstaller(bool originalAudit)
    {
        foreach (bool purrFirst in new[] { true, false })
        {
            var purr = new Harmony("purr2d2.interop.legacy." + purrFirst);
            var ours = new Harmony("MeowSci.Unscience");
            bool failed = false;
            try
            {
                // Historical Unscience used a plain HarmonyMethod without priority/before.
                if (purrFirst) GameHooks.Install(purr);
                ours.Patch(Frame, transpiler: new HarmonyMethod(Hook));
                if (!purrFirst) GameHooks.Install(purr);
                Universe.Reset();
                new KSA.Program().RunFrame(0.25);
                Require(Universe.VehicleDispatches == 1, "legacy composition retains one vehicle dispatch");
            }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException inner
                && inner.Message.Contains("unexpected PrepareFrame solver sequence at ExecuteNextVehicleSolvers", StringComparison.Ordinal))
            {
                failed = true;
                Require(originalAudit && purrFirst, "updated Purr must support historical Unscience in both orders");
                Console.WriteLine("REPRODUCED: actual Purr installed first makes historical Unscience reject ExecuteNextVehicleSolvers");
            }
            finally
            {
                ours.Unpatch(Frame, Hook);
                purr.UnpatchAll(purr.Id);
            }
            Require(failed == (originalAudit && purrFirst), "source-backed legacy install-order expectation");
        }
    }

    private static void CheckCurrentComposition(bool purrFirst)
    {
        var purr = new Harmony("purr2d2.interop.current." + purrFirst);
        var ours = new Harmony("MeowSci.Unscience");
        var runtime = new SaberRuntime();
        GameHooks.Runtime = runtime;
        // Purr's actual background warmup roots this wrapper. Warm the real compiled wrapper
        // before adding a separate solver prefix, then ensure its ordinary call still sees it.
        var wrappedDispatch = AccessTools.Method(typeof(GameHooks), "PrepareAndDispatchVehicles")
            .CreateDelegate<Action<double, SimStep>>();
        wrappedDispatch(0.25, new(new(100), new(100.25), 0.25));
        MethodInfo prefix = AccessTools.Method(typeof(FrameInteropChecks), nameof(BeforeVehicleDispatch));
        ours.Patch(Dispatch, prefix: new HarmonyMethod(prefix));
        PhysicsFrameHook.BeforePhysics += BeforePhysics;
        try
        {
            if (purrFirst) GameHooks.Install(purr);
            NativeSaveHooks.Apply(ours);
            if (!purrFirst) GameHooks.Install(purr);
            NativeSaveHooks.Apply(ours);
            CheckFrame(runtime, purr: true, hook: true);
            CheckFlushException();

            purr.UnpatchAll(purr.Id);
            CheckFrame(runtime, purr: false, hook: true);
            GameHooks.Install(purr);
            CheckFrame(runtime, purr: true, hook: true);

            NativeSaveHooks.Remove(ours);
            PhysicsFrameHook.Remove(ours);
            CheckFrame(runtime, purr: true, hook: false);
            NativeSaveHooks.Apply(ours);
            CheckFrame(runtime, purr: true, hook: true);
        }
        finally
        {
            PhysicsFrameHook.BeforePhysics -= BeforePhysics;
            NativeSaveHooks.Remove(ours);
            PhysicsFrameHook.Remove(ours);
            purr.UnpatchAll(purr.Id);
            ours.UnpatchAll(ours.Id);
            GameHooks.Runtime = null;
        }
    }

    private static void CheckFrame(SaberRuntime runtime, bool purr, bool hook)
    {
        Universe.Reset();
        _callbacks = _prefixes = 0;
        int updates = runtime.Updates;
        if (hook) PhysicsFrameHook.Enqueue(() => Universe.Events.Add("queued edit"));
        SimStep step = new KSA.Program().RunFrame(0.25);
        string[] actual = Universe.Events.Where(e => !e.StartsWith("purr ", StringComparison.Ordinal)).ToArray();
        string expected = "wait orbit reader,wait orbit,wait vehicle,wait cloth,apply orbit,apply vehicle,apply cloth,input,"
            + (hook ? "get step,wait orbit reader,queued edit,unscience callback," : "get step,")
            + "queue cloth,flush derived,flush resources,solver prefix,queue vehicle,queue orbit";
        Require(string.Join(",", actual) == expected, "exact result/input/step/edit/callback/flush/snapshot ordering: " + string.Join(",", actual));
        Require(_callbacks == (hook ? 1 : 0) && runtime.Updates - updates == (purr ? 1 : 0)
            && _prefixes == 1 && Universe.VehicleDispatches == 1 && Universe.Steps == 1,
            "each production hook, warmed-wrapper solver prefix and native dispatch runs exactly once");
        if (hook) Require(_delta == 0.25 && _stateTime == step.PreviousTime, "callback keeps player delta and PreviousTime");
        if (purr)
        {
            Require(runtime.Delta == 0.25 && Universe.Events.IndexOf("purr settled") > Universe.Events.IndexOf("input")
                && Universe.Events.IndexOf("purr returned") < Universe.Events.IndexOf("get step"),
                "actual Purr settled callback stays after input and before step computation");
            foreach (string segment in new[] { "VehicleWait", "DirtyDerived", "ResourceFlush", "VehiclePreparationAndDispatch" })
                Require(Universe.Events.Count(e => e == "purr end " + segment) == 1, "real diagnostic wrapper completes once: " + segment);
            Require(Universe.Events.Count(e => e == "purr enter") == 1
                && Universe.Events.Count(e => e == "purr worker joined") == 1, "frame entry and joined-worker diagnostics survive composition");
        }
    }

    private static void CheckFlushException()
    {
        Universe.Reset();
        PartTree.ThrowOnDerived = true;
        bool failed = false;
        try { new KSA.Program().RunFrame(0.25); }
        catch (InvalidOperationException error) when (error.Message == "fixture derived failure") { failed = true; }
        Require(failed && Universe.Events.Contains("purr end DirtyDerived") && !Universe.Events.Contains("queue vehicle"),
            "actual Purr wrapper preserves native exceptions and its diagnostics finally block");
        PartTree.ThrowOnDerived = false;
    }

    private static void BeforePhysics(double dt, UniverseTime stateTime)
    {
        _callbacks++; _delta = dt; _stateTime = stateTime;
        PhysicsFrameHook.JoinOrbitReaders();
        Universe.Events.Add("unscience callback");
    }

    private static void BeforeVehicleDispatch() { _prefixes++; Universe.Events.Add("solver prefix"); }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
