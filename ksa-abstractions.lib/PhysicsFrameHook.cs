using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSA;

namespace MeowSci.KsaAbstractions;

/// <summary>Runs queued vessel edits and registered callbacks after result application and before any next-step physics snapshots.</summary>
public static class PhysicsFrameHook
{
    private static readonly Queue<Action> Pending = new();
    public static event Action<double, UniverseTime>? BeforePhysics;

    /// <summary>Queue a main-thread mutation after worker results, before welds and new snapshots.</summary>
    public static void Enqueue(Action action) => Pending.Enqueue(action);

    public static void Apply(Harmony harmony)
    {
        harmony.Patch(PrepareFrameMethod(), transpiler: new HarmonyMethod(TranspilerMethod()));
        Console.WriteLine("unscience physics: PrepareFrame handoff hook applied");
    }

    public static void Remove(Harmony harmony)
    {
        harmony.Unpatch(PrepareFrameMethod(), TranspilerMethod());
        Pending.Clear();
    }

    private static MethodInfo PrepareFrameMethod() =>
        AccessTools.Method(typeof(Program), "PrepareFrame", new[] { typeof(double), typeof(double) })
        ?? throw new MissingMethodException(typeof(Program).FullName, "PrepareFrame(double, double)");

    private static MethodInfo TranspilerMethod() =>
        AccessTools.Method(typeof(PhysicsFrameHook), nameof(Transpile));

    internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.ToList();
        // Patch the caller: a prefix on a small solver method can miss a call already inlined
        // into PrepareFrame. Replacing this one call preserves its stack, labels and blocks.
        // Reject missing, duplicate or reordered seams instead of falling back to UI teleports.
        string[] seams =
        {
            nameof(Universe.ApplyOrbitSolvers),
            nameof(Universe.ApplyVehicleSolvers),
            nameof(Universe.ApplyClothSolvers),
            nameof(Universe.GetJobSimStep),
            nameof(Universe.ExecuteNextClothSolvers),
            nameof(Universe.ExecuteNextVehicleSolvers),
            nameof(Universe.ExecuteNextOrbitSolvers)
        };
        int previousIndex = -1;
        int stepIndex = -1;
        foreach (string seam in seams)
        {
            var method = AccessTools.Method(typeof(Universe), seam)
                ?? throw new MissingMethodException(typeof(Universe).FullName, seam);
            int index = codes.FindIndex(code => code.Calls(method));
            if (index <= previousIndex || codes.Count(code => code.Calls(method)) != 1)
                throw new InvalidOperationException($"unscience physics: unexpected PrepareFrame solver sequence at {seam}");
            previousIndex = index;
            if (seam == nameof(Universe.GetJobSimStep)) stepIndex = index;
        }

        codes[stepIndex].operand = AccessTools.Method(typeof(PhysicsFrameHook), nameof(GetStepAndDispatch));
        return codes;
    }

    private static SimStep GetStepAndDispatch(double dtPlayer)
    {
        SimStep step = Universe.GetJobSimStep(dtPlayer);
        if (Universe.CurrentSystem == null) { Pending.Clear(); return step; }

        // Drain only the actions present at the handoff; actions queued by callbacks wait a frame.
        int count = Pending.Count;
        for (int i = 0; i < count; i++)
        {
            try { Pending.Dequeue()(); }
            catch (Exception ex) { Console.WriteLine($"unscience physics: queued mutation failed: {ex}"); }
        }
        if (BeforePhysics != null)
            foreach (Action<double, UniverseTime> callback in BeforePhysics.GetInvocationList())
            {
                try { callback(dtPlayer, step.PreviousTime); }
                catch (Exception ex) { Console.WriteLine($"unscience physics: callback failed: {ex}"); }
            }
        return step;
    }
}
