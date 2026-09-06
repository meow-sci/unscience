using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using KSA;
using MeowSci.GarrysTorchLib;

internal static class Checks
{
    private static void Main()
    {
        CheckRejectedLayouts();
        var game = new KSA.Program();

        // Reproduce the bug: removing the source after workers stage results prevents commit.
        Universe.Reset();
        for (int i = 0; i < 4; i++)
        {
            game.RunFrame(0.25);
            Universe.InBubble = false;
        }
        Require(Universe.TimeCurrent == 0.25, "old UI teleport must reproduce stalled actuation");

        var harmony = new Harmony("garrys-torch.tests");
        GarrysTorchPatches.Apply(harmony);
        try
        {
            Universe.Reset();
            var submod = new GarrysTorchSubmod();
            GarrysTorchSubmod.Instance = submod;
            for (int i = 0; i < 4; i++)
            {
                Universe.Events.Clear();
                SimStep step = game.RunFrame(0.25);
                Require(Universe.TimeCurrent == (i + 1) * 0.25, "actuator must accumulate committed progress");
                Require(submod.Updates == i + 1, "exactly one weld update per frame");
                Require(submod.StateTime == step.PreviousTime, "teleport must use committed state time");
                Require(step.NextTime.Seconds == step.PreviousTime.Seconds + 0.25, "preserve game step");
                Require(string.Join(",", Universe.Events) ==
                    "apply orbit,apply vehicle,apply cloth,get step,weld,queue cloth,queue vehicle,queue orbit",
                    "weld after all results, before any physics snapshot");
            }

            // Weld interpolation retains player-time pacing during pause and warp.
            foreach (double speed in new[] { 0.0, 10.0 })
            {
                Universe.Speed = speed;
                SimStep step = game.RunFrame(0.25);
                Require(submod.PlayerDelta == 0.25, "weld animation uses player delta");
                Require(submod.StateTime == step.PreviousTime, "timestamp stays at step start during pause/warp");
                Require(step.DeltaTime == 0.25 * speed, "preserve simulation delta");
            }

            int updates = submod.Updates;
            Universe.CurrentSystem = null;
            game.RunFrame(0.25);
            Require(submod.Updates == updates, "skip welding without a loaded system");
            Universe.CurrentSystem = new();
            submod.ThrowOnUpdate = true;
            Universe.Events.Clear();
            game.RunFrame(0.25);
            Require(Universe.Events.Last() == "queue orbit", "weld error must not interrupt game scheduling");

            GarrysTorchSubmod.Instance = null;
            game.RunFrame(0.25);
            Require(submod.Updates == updates + 1, "skip absent/disposed submod");
        }
        finally
        {
            GarrysTorchPatches.Remove(harmony);
        }
        Universe.Events.Clear();
        GarrysTorchSubmod.Instance = new();
        game.RunFrame(0.25);
        Require(!Universe.Events.Contains("weld"), "unload must restore original caller");
        Console.WriteLine("PASS: weld timing, result retention, timestamps, pause/warp, lifecycle and layout guards");
    }

    private static void CheckRejectedLayouts()
    {
        string[] names =
        {
            nameof(Universe.ApplyOrbitSolvers), nameof(Universe.ApplyVehicleSolvers),
            nameof(Universe.ApplyClothSolvers), nameof(Universe.GetJobSimStep),
            nameof(Universe.ExecuteNextClothSolvers), nameof(Universe.ExecuteNextVehicleSolvers),
            nameof(Universe.ExecuteNextOrbitSolvers)
        };
        List<CodeInstruction> Sequence() => names.Select(name =>
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Universe), name))).ToList();

        // Every missing or duplicate seam should fail closed, including the replacement point.
        for (int i = 0; i < names.Length; i++)
        {
            var missing = Sequence();
            missing.RemoveAt(i);
            ExpectRejected(missing);
            var duplicate = Sequence();
            duplicate.Insert(i, new CodeInstruction(duplicate[i]));
            ExpectRejected(duplicate);
        }
        var earlyWorkers = Sequence();
        (earlyWorkers[2], earlyWorkers[4]) = (earlyWorkers[4], earlyWorkers[2]);
        ExpectRejected(earlyWorkers);

        var valid = Sequence();
        var label = new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
        valid[3].labels.Add(label);
        valid[3].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        var patched = GarrysTorchPatches.Transpile(valid).ToList();
        Require(patched[3].labels.Contains(label) && patched[3].blocks.Count == 1,
            "replacement must preserve branch and exception metadata");
    }

    private static void ExpectRejected(List<CodeInstruction> codes)
    {
        try { _ = GarrysTorchPatches.Transpile(codes).ToList(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("unexpected game-loop layout must reject patch installation");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
