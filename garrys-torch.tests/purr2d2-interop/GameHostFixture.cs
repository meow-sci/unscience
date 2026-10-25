using System.Runtime.CompilerServices;
using Brutal.Concurrency.Jobs;
using Brutal.GlfwApi;
using Brutal.Numerics;

namespace KSA
{
    public readonly record struct UniverseTime(double Seconds);
    public readonly record struct SimStep(UniverseTime PreviousTime, UniverseTime NextTime, double DeltaTime);
    public sealed record UniverseData(double Time, bool Reject = false);

    internal sealed class Program
    {
        private readonly int[] _items = { 1, 2 };
        private SimStep _lastStep;
        internal static bool IsEditorOpen = false;
        internal static bool DrawUI = true;
        private static readonly GlfwWindow Window = new();
        internal static GlfwWindow GetWindow() => Window;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal SimStep RunFrame(double dt) { PrepareFrame(0, dt); return _lastStep; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void PrepareFrame(double currentPlayerTime, double dtPlayer)
        {
            JobSystems.NearestOrbitAndPerformanceWorker.Wait();
            JobSystems.OrbitSolvers.Wait();
            JobSystems.VehicleSolver.Wait();
            JobSystems.ClothSolvers.Wait();
            Universe.ApplyOrbitSolvers();
            Universe.ApplyVehicleSolvers();
            Universe.ApplyClothSolvers();
            InputEvents.ApplyInputEvents();
            ReadOnlySpan<int> items = _items;
            for (int i = 0; i < items.Length; i++)
                if (items[i] < 0) return;
            // Native PrepareFrame also requeues its nearest-orbit reader before GetJobSimStep.
            JobSystems.WorkersBusy = true;
            SimStep step = Universe.GetJobSimStep(dtPlayer);
            Universe.ExecuteNextClothSolvers(dtPlayer, step);
            PartTree.FlushDirtyDerived();
            PartTree.FlushDirtyResourceManagers();
            Universe.ExecuteNextVehicleSolvers(dtPlayer, step);
            Universe.ExecuteNextOrbitSolvers(dtPlayer, step);
            _lastStep = step;
        }
        [MethodImpl(MethodImplOptions.NoInlining)] internal void DrawProgramMenusHook() { }
        [MethodImpl(MethodImplOptions.NoInlining)] internal void OnFrameEditor(double dtPlayer) { }
        [MethodImpl(MethodImplOptions.NoInlining)] internal void OnDrawUiThreadSafe(double dtPlayer) { }
        [MethodImpl(MethodImplOptions.NoInlining)] internal void OnMouseButton(GlfwWindow window, GlfwMouseButton button, GlfwButtonAction action, GlfwModifier modifiers) { }
        [MethodImpl(MethodImplOptions.NoInlining)] internal void OnCursorPos(GlfwWindow window, double2 position) { }
        [MethodImpl(MethodImplOptions.NoInlining)] internal void OnScroll(GlfwWindow window, double2 offset) { }
        [MethodImpl(MethodImplOptions.NoInlining)] internal void OnFocus(GlfwWindow window, GlfwFocusEvent focusEvent) { }
        [MethodImpl(MethodImplOptions.NoInlining)] internal void OnKey(GlfwWindow window, GlfwKey key, int scanCode, GlfwKeyAction action, GlfwModifier modifiers) { }
        [MethodImpl(MethodImplOptions.NoInlining)] internal void OnGameLoaded() { }
        [MethodImpl(MethodImplOptions.NoInlining)] internal void Dispose() { }
    }

    internal static class Universe
    {
        internal static readonly List<string> Events = new();
        internal static object? CurrentSystem = new();
        internal static double Time = 100;
        internal static int Steps;
        internal static int VehicleDispatches;
        internal static void Reset()
        {
            Events.Clear(); CurrentSystem = new(); Time = 100; Steps = VehicleDispatches = 0;
            JobSystems.WorkersBusy = true; JobSystems.ThrowOnWaitAll = false; PartTree.ThrowOnDerived = false;
        }
        internal static void ApplyOrbitSolvers() => Events.Add("apply orbit");
        internal static void ApplyVehicleSolvers() => Events.Add("apply vehicle");
        internal static void ApplyClothSolvers() => Events.Add("apply cloth");
        internal static SimStep GetJobSimStep(double dt) { Events.Add("get step"); Steps++; return new(new(Time), new(Time + dt), dt); }
        internal static void ExecuteNextClothSolvers(double dt, SimStep step) => Events.Add("queue cloth");
        // This fixture does not model native worker execution. Its dispatch call remains an
        // observable managed seam for checking warmed production wrappers and Harmony prefixes.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ExecuteNextVehicleSolvers(double dt, SimStep step)
        { Events.Add("queue vehicle"); VehicleDispatches++; }
        internal static void ExecuteNextOrbitSolvers(double dt, SimStep step) => Events.Add("queue orbit");
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void DeserializeSave(UniverseData universeData)
        { JobSystems.WaitAll(); Events.Add("native load"); Time = universeData.Time; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void LoadSystem(string id) { Events.Add("native system"); CurrentSystem = new(); }
    }

    internal static class JobSystems
    {
        internal static bool WorkersBusy;
        internal static bool ThrowOnWaitAll;
        internal static readonly JobScheduler NearestOrbitAndPerformanceWorker = new("orbit reader");
        internal static readonly JobScheduler OrbitSolvers = new("orbit");
        internal static readonly JobScheduler VehicleSolver = new("vehicle");
        internal static readonly JobScheduler ClothSolvers = new("cloth");
        internal static void WaitAll()
        {
            Universe.Events.Add("join all");
            if (ThrowOnWaitAll) throw new InvalidOperationException("fixture worker join failure");
            WorkersBusy = false;
        }
    }
    internal static class InputEvents { internal static void ApplyInputEvents() => Universe.Events.Add("input"); }
    internal static class PartTree
    {
        internal static bool ThrowOnDerived;
        internal static void FlushDirtyDerived()
        { Universe.Events.Add("flush derived"); if (ThrowOnDerived) throw new InvalidOperationException("fixture derived failure"); }
        internal static void FlushDirtyResourceManagers() => Universe.Events.Add("flush resources");
    }
    internal sealed class PartTemplate { }
    internal sealed class PartInstance { }
    internal sealed class Part
    { internal Part(string id, PartTemplate template, PartInstance instance, Part parent) { } }
    internal sealed class KittenEva
    { [MethodImpl(MethodImplOptions.NoInlining)] internal bool OnKey(GlfwKeyEvent keyEvent) => false; }
    public class GameSave
    { [MethodImpl(MethodImplOptions.NoInlining)] internal void Populate() { } }
    public sealed class UncompressedSave : GameSave
    {
        internal string Id => "fixture";
        internal UniverseData Data = new(900);
        [MethodImpl(MethodImplOptions.NoInlining)] internal bool Write() => true;
        [MethodImpl(MethodImplOptions.NoInlining)] internal void Load() => Universe.DeserializeSave(Data);
    }
    internal static class SystemLibrary { internal static object? Find(string id) => new(); }
}

namespace Brutal.Concurrency.Jobs
{
    internal sealed class JobScheduler(string name)
    {
        internal void Wait()
        {
            KSA.Universe.Events.Add("wait " + name);
            if (name == "orbit reader") KSA.JobSystems.WorkersBusy = false;
        }
    }
}

namespace MeowSci.KsaAbstractions.Persistence
{
    // Only preflight data policy is substituted. The production save hook owns all deferral,
    // guard, joins, reset/replay and error propagation exercised by this harness.
    internal static class NativeSavePreflight
    {
        internal static IReadOnlyList<string> Validate(KSA.UniverseData data)
        {
            KSA.Universe.Events.Add("preflight");
            if (data.Reject) throw new InvalidDataException("fixture rejected native save");
            return Array.Empty<string>();
        }
    }
}
