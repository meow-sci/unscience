using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSA;

namespace MeowSci.GarrysTorchLib;

/// <summary>Runs welds after result application and before any next-step physics snapshots.</summary>
public static class GarrysTorchPatches
{
    public static void Apply(Harmony harmony)
    {
        harmony.Patch(PrepareFrameMethod(), transpiler: new HarmonyMethod(TranspilerMethod()));
        Console.WriteLine("garrys-torch: PrepareFrame weld hook applied");
    }

    public static void Remove(Harmony harmony) =>
        harmony.Unpatch(PrepareFrameMethod(), TranspilerMethod());

    private static MethodInfo PrepareFrameMethod() =>
        AccessTools.Method(typeof(Program), "PrepareFrame", new[] { typeof(double), typeof(double) })
        ?? throw new MissingMethodException(typeof(Program).FullName, "PrepareFrame(double, double)");

    private static MethodInfo TranspilerMethod() =>
        AccessTools.Method(typeof(GarrysTorchPatches), nameof(Transpile));

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
                throw new InvalidOperationException($"garrys-torch: unexpected PrepareFrame solver sequence at {seam}");
            previousIndex = index;
            if (seam == nameof(Universe.GetJobSimStep)) stepIndex = index;
        }

        codes[stepIndex].operand = AccessTools.Method(typeof(GarrysTorchPatches), nameof(GetStepAndUpdateWelds));
        return codes;
    }

    private static SimStep GetStepAndUpdateWelds(double dtPlayer)
    {
        SimStep step = Universe.GetJobSimStep(dtPlayer);
        if (Universe.CurrentSystem == null) return step;

        try
        {
            // PreviousTime is the just-applied state's time. NextTime belongs to the workers
            // that have not started yet; stamping it here would put the body ahead of its origin.
            GarrysTorchSubmod.Instance?.UpdateBeforeVehicleSolvers(dtPlayer, step.PreviousTime);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"garrys-torch: Error updating welds before solvers: {ex}");
        }
        return step;
    }
}
