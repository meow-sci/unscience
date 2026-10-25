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
    private static Action? _pendingWorldChange;
    private static bool _orbitReadersJoined;
    public static bool IsApplied { get; private set; }
    public static bool IsReplayingWorldChange { get; private set; }
    public static event Action<double, UniverseTime>? BeforePhysics;

    /// <summary>Queue a main-thread mutation after worker results, before welds and new snapshots.</summary>
    public static void Enqueue(Action action) => Pending.Enqueue(action);

    /// <summary>
    /// Waits for the nearest-orbit/performance job that PrepareFrame queues just before this
    /// handoff. It reads flight plans and cached orbit points concurrently, and Teleport or other
    /// flight-plan edits dispose those points. Joins at most once per frame; callbacks that move
    /// vessels call it before mutating, and queued work and world changes join automatically.
    /// Intended for the handoff only: the flag resets there, and later in the frame the same worker
    /// may run unrelated editor performance jobs, which this does not wait for.
    /// </summary>
    public static void JoinOrbitReaders()
    {
        if (_orbitReadersJoined) return;
        JobSystems.NearestOrbitAndPerformanceWorker.Wait();
        _orbitReadersJoined = true;
    }

    /// <summary>Discard mutations queued against a world that is being replaced.</summary>
    public static void ClearPending() => Pending.Clear();

    /// <summary>Replace the pending world request; replay before the next simulation step is computed.</summary>
    public static void EnqueueWorldChange(Action action)
    {
        if (!IsApplied) throw new InvalidOperationException("World-change frame hook is unavailable.");
        _pendingWorldChange = action;
    }

    public static void ClearPendingWorldChange() => _pendingWorldChange = null;

    public static void Apply(Harmony harmony)
    {
        if (IsApplied) return;
        MethodInfo original = PrepareFrameMethod();
        MethodInfo transpiler = TranspilerMethod();
        string[] owners = Array.Empty<string>();
        try
        {
            // Harmony reruns the complete transpiler chain on each patch/unpatch. Validate the
            // original solver calls before call-site profilers can replace their operands. Priority
            // covers later normal-priority patches; before covers earlier owners even at First.
            // Never interpret an arbitrary foreign wrapper as evidence of a safe solver boundary.
            var existing = Harmony.GetPatchInfo(original)?.Transpilers;
            // A previous failed wrapper generation must not accumulate registrations on retry.
            if (existing?.Any(patch => patch.PatchMethod == transpiler) == true)
                harmony.Unpatch(original, transpiler);
            owners = Harmony.GetPatchInfo(original)?.Transpilers
                .Select(patch => patch.owner).Where(owner => owner != harmony.Id).Distinct().ToArray()
                ?? Array.Empty<string>();
            harmony.Patch(original, transpiler: new HarmonyMethod(transpiler)
            {
                priority = Priority.First,
                before = owners
            });
            IsApplied = true;
            Console.WriteLine("unscience physics: PrepareFrame handoff hook applied");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience physics: PrepareFrame handoff installation failed: {ex}");
            Console.WriteLine($"unscience physics: requested priority={Priority.First} before=[{string.Join(",", owners)}]");
            try { LogTranspilers(original); }
            catch (Exception diagnostic)
            {
                Console.WriteLine($"unscience physics: PrepareFrame patch inspection also failed: {diagnostic}");
            }
            try { harmony.Unpatch(original, transpiler); }
            catch (Exception rollback)
            {
                Console.WriteLine($"unscience physics: PrepareFrame targeted rollback also failed: {rollback}");
            }
            throw;
        }
    }

    public static void Remove(Harmony harmony)
    {
        try
        {
            harmony.Unpatch(PrepareFrameMethod(), TranspilerMethod());
            // On generation failure Harmony retains the previous executable chain. Keep the
            // installation flag until removal succeeds, but never keep queued old-world work.
            IsApplied = false;
        }
        finally
        {
            Pending.Clear();
            ClearPendingWorldChange();
            _orbitReadersJoined = false;
        }
    }

    private static MethodInfo PrepareFrameMethod() =>
        AccessTools.Method(typeof(Program), "PrepareFrame", new[] { typeof(double), typeof(double) })
        ?? throw new MissingMethodException(typeof(Program).FullName, "PrepareFrame(double, double)");

    private static MethodInfo TranspilerMethod() =>
        AccessTools.Method(typeof(PhysicsFrameHook), nameof(Transpile));

    private static void LogTranspilers(MethodInfo original)
    {
        var patches = Harmony.GetPatchInfo(original)?.Transpilers;
        Console.WriteLine($"unscience physics: {original.DeclaringType?.FullName}.{original.Name}(double,double); "
            + $"Harmony {typeof(Harmony).Assembly.GetName().Version}; {patches?.Count ?? 0} registered transpiler(s)");
        if (patches == null) return;
        foreach (var patch in patches)
            Console.WriteLine($"unscience physics: transpiler owner='{patch.owner}' "
                + $"method='{patch.PatchMethod.DeclaringType?.FullName}.{patch.PatchMethod.Name}' "
                + $"priority={patch.priority} index={patch.index} "
                + $"before=[{string.Join(",", patch.before)}] after=[{string.Join(",", patch.after)}]");
    }

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
            int count = codes.Count(code => code.Calls(method));
            if (index <= previousIndex || count != 1)
                throw new InvalidOperationException($"unscience physics: unexpected PrepareFrame solver sequence at {seam} "
                    + $"(calls={count}, index={index}, previous={previousIndex})");
            previousIndex = index;
            if (seam == nameof(Universe.GetJobSimStep)) stepIndex = index;
        }

        codes[stepIndex].operand = AccessTools.Method(typeof(PhysicsFrameHook), nameof(GetStepAndDispatch));
        return codes;
    }

    private static SimStep GetStepAndDispatch(double dtPlayer)
    {
        // The previous ImGui frame has finished and worker results have committed. A
        // console load requested after ImGui.Image must never dispose its textures inline.
        // Replay the whole transaction here, before computing any time from the old world.
        _orbitReadersJoined = false;
        Action? worldChange = _pendingWorldChange;
        _pendingWorldChange = null;
        if (worldChange != null)
        {
            JoinOrbitReaders();
            IsReplayingWorldChange = true;
            try { worldChange(); }
            catch (Exception ex) { Console.WriteLine($"unscience saves: deferred world change failed: {ex}"); }
            finally { IsReplayingWorldChange = false; }
        }
        SimStep step = Universe.GetJobSimStep(dtPlayer);
        if (Universe.CurrentSystem == null) { Pending.Clear(); return step; }

        // Drain only the actions present at the handoff; actions queued by callbacks wait a frame.
        int count = Pending.Count;
        if (count > 0) JoinOrbitReaders();
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
