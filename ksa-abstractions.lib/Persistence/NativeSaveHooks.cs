using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using KSA;

namespace MeowSci.KsaAbstractions.Persistence;

/// <summary>Bridges native save transactions to detached Unscience persistence callbacks.</summary>
public static class NativeSaveHooks
{
    private static readonly List<(MethodInfo Target, MethodInfo Patch)> Installed = new();
    private static int _fileLoadDepth;
    private static bool _nativeLoadReached;
    public static bool IsApplied => Installed.Count != 0 && PhysicsFrameHook.IsApplied;

    public static Action<GameSave>? Capturing { get; set; }
    public static Action<UncompressedSave>? Written { get; set; }
    public static Action<UncompressedSave>? WriteFailed { get; set; }
    public static Action<UncompressedSave>? Loading { get; set; }
    public static Action? Resetting { get; set; }
    public static Action? Restoring { get; set; }
    public static Action? LoadFinished { get; set; }
    public static Action<Exception>? LoadFailed { get; set; }
    public static Exception? LastLoadError { get; private set; }

    public static void Apply(Harmony harmony)
    {
        if (Installed.Count != 0) return;
        try
        {
            // A load can originate late in UI rendering. Without the early-frame seam,
            // installing synchronous reset hooks would free resources still referenced by UI.
            PhysicsFrameHook.Apply(harmony);
            Patch(harmony, typeof(GameSave), nameof(GameSave.Populate), Type.EmptyTypes,
                postfix: nameof(Populated));
            Patch(harmony, typeof(UncompressedSave), nameof(UncompressedSave.Write), Type.EmptyTypes,
                postfix: nameof(Saved));
            Patch(harmony, typeof(UncompressedSave), nameof(UncompressedSave.Load), Type.EmptyTypes,
                prefix: nameof(LoadingSave), finalizer: nameof(FinishedLoading));
            Patch(harmony, typeof(Universe), nameof(Universe.DeserializeSave), new[] { typeof(UniverseData) },
                prefix: nameof(ResetBeforeLoad), postfix: nameof(RestoreAfterLoad), finalizer: nameof(FinishedDirectLoad));
            Patch(harmony, typeof(Universe), nameof(Universe.LoadSystem), new[] { typeof(string) },
                prefix: nameof(ResetBeforeSystem), finalizer: nameof(FinishedDirectLoad));
        }
        catch
        {
            Remove(harmony);
            throw;
        }
    }

    public static void Remove(Harmony harmony)
    {
        for (int i = Installed.Count - 1; i >= 0; i--)
            harmony.Unpatch(Installed[i].Target, Installed[i].Patch);
        Installed.Clear();
        PhysicsFrameHook.ClearPendingWorldChange();
    }

    private static void Patch(Harmony harmony, Type type, string name, Type[] arguments,
        string? prefix = null, string? postfix = null, string? finalizer = null)
    {
        MethodInfo target = AccessTools.Method(type, name, arguments)
            ?? throw new MissingMethodException(type.FullName, name);
        HarmonyMethod? Resolve(string? methodName)
        {
            if (methodName == null) return null;
            MethodInfo method = AccessTools.Method(typeof(NativeSaveHooks), methodName)
                ?? throw new MissingMethodException(typeof(NativeSaveHooks).FullName, methodName);
            Installed.Add((target, method));
            return new HarmonyMethod(method);
        }
        harmony.Patch(target, prefix: Resolve(prefix), postfix: Resolve(postfix), finalizer: Resolve(finalizer));
    }

    private static void Populated(GameSave __instance, bool __runOriginal)
    {
        // Capture only detached data. Native Populate already read the published state;
        // joining workers here would not change the native snapshot's capture time.
        if (__runOriginal) Invoke(Capturing, __instance, "capture");
    }

    private static void Saved(UncompressedSave __instance, bool __result, bool __runOriginal)
    {
        if (!__runOriginal) return;
        // Since KSA 5482 a failed save returns false instead of throwing, and the previous save
        // folder can survive intact. Writing the sidecar there would pair this session's feature
        // state with the old universe, so only a successful native write publishes it.
        if (__result) Invoke(Written, __instance, "write");
        else Invoke(WriteFailed, __instance, "write failure");
    }

    private static bool LoadingSave(UncompressedSave __instance, out bool __state)
    {
        __state = false;
        if (KSA.Program.IsEditorOpen || !FrameHookAvailable()) return true;
        if (!PhysicsFrameHook.IsReplayingWorldChange)
        {
            PhysicsFrameHook.EnqueueWorldChange(__instance.Load);
            return false;
        }
        __state = true;
        _fileLoadDepth++;
        _nativeLoadReached = false;
        LastLoadError = null;
        Invoke(Loading, __instance, "preflight");
        return true;
    }

    private static Exception? FinishedLoading(UncompressedSave __instance, Exception? __exception, bool __state)
    {
        if (__state)
        {
            _fileLoadDepth--;
            // Since KSA 5482 an unreadable save is logged and Load returns before replacing the world.
            Exception? failure = __exception ?? (_nativeLoadReached ? null
                : new InvalidDataException($"KSA could not read save '{__instance.Id}'; the current scene was left unchanged."));
            LastLoadError = failure;
            Invoke(LoadFinished, "finish load");
            if (failure != null) Invoke(LoadFailed, failure, "load failure");
        }
        return __exception;
    }

    private static Exception? FinishedDirectLoad(Exception? __exception)
    {
        if (__exception != null && _fileLoadDepth == 0)
        {
            LastLoadError = __exception;
            Invoke(LoadFailed, __exception, "direct load failure");
        }
        return __exception;
    }

    private static bool ResetBeforeLoad(UniverseData universeData, out bool __state)
    {
        __state = false;
        _nativeLoadReached = true;
        // Match the original's no-world early failure without touching the current session.
        if (Universe.CurrentSystem == null || universeData == null || !FrameHookAvailable()) return true;
        if (!PhysicsFrameHook.IsReplayingWorldChange)
        {
            PhysicsFrameHook.EnqueueWorldChange(() => Universe.DeserializeSave(universeData));
            return false;
        }
        if (_fileLoadDepth == 0) LastLoadError = null;
        NativeSavePreflight.Validate(universeData);
        __state = ResetAtJoinedBoundary();
        return true;
    }

    private static void RestoreAfterLoad(bool __state, bool __runOriginal)
    {
        if (__state && __runOriginal) Invoke(Restoring, "restore");
    }

    private static bool ResetBeforeSystem(string id)
    {
        // LoadSystem validates this before replacing the world. An invalid name must not
        // clear the current setup merely because the method was called.
        if (Universe.CurrentSystem == null || SystemLibrary.Find(id) == null || !FrameHookAvailable()) return true;
        if (!PhysicsFrameHook.IsReplayingWorldChange)
        {
            PhysicsFrameHook.EnqueueWorldChange(() => Universe.LoadSystem(id));
            return false;
        }
        if (_fileLoadDepth == 0) LastLoadError = null;
        ResetAtJoinedBoundary();
        return true;
    }

    private static bool FrameHookAvailable()
    {
        if (PhysicsFrameHook.IsApplied) return true;
        Console.WriteLine("unscience saves: frame hook unavailable; native load continues without scene restore.");
        return false;
    }

    private static bool ResetAtJoinedBoundary()
    {
        try
        {
            JobSystems.OrbitSolvers.Wait();
            JobSystems.VehicleSolver.Wait();
            JobSystems.ClothSolvers.Wait();
            // PrepareFrame queues nearest-orbit work before this handoff; it traverses
            // the current universe and must finish before vehicle destruction as well.
            JobSystems.NearestOrbitAndPerformanceWorker.Wait();
            PhysicsFrameHook.ClearPending();
            Invoke(Resetting, "reset");
            // Cleanup callbacks can themselves enqueue old-world edits. They cannot survive
            // the replacement, even when a participant throws halfway through cleanup.
            PhysicsFrameHook.ClearPending();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience saves: cannot join reset boundary: {ex}");
            throw; // Never destroy a world while a worker could still own it.
        }
    }

    private static void Invoke(Action? callbacks, string operation)
    {
        if (callbacks == null) return;
        foreach (Action callback in callbacks.GetInvocationList())
        {
            try { callback(); }
            catch (Exception ex) { Console.WriteLine($"unscience saves: {operation} callback failed: {ex}"); }
        }
    }

    private static void Invoke<T>(Action<T>? callbacks, T value, string operation)
    {
        if (callbacks == null) return;
        foreach (Action<T> callback in callbacks.GetInvocationList())
        {
            try { callback(value); }
            catch (Exception ex) { Console.WriteLine($"unscience saves: {operation} callback failed: {ex}"); }
        }
    }
}
