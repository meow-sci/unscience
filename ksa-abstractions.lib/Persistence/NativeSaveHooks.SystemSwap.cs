using System;
using System.IO;
using HarmonyLib;
using KSA;

namespace MeowSci.KsaAbstractions.Persistence;

/// <summary>
/// KSA 5916 star-system switching. <c>UncompressedSave.Load</c> hands the loaded save to
/// <c>SaveSystemSwap.RequestLoad(id, system, ApplyLoaded)</c>. A save from another installed system only
/// queues the switch; <c>SaveSystemSwap.OnFrame</c> (after <c>ImGui.Render</c>) later runs
/// <c>Program.SwapSystem</c>, which destroys the old world, and then <c>ApplyLoaded</c>
/// (<c>Universe.DeserializeSave</c> + <c>Program.OnGameLoaded</c>). Multiplayer joins reach the same
/// swap through <c>SaveSystemSwap.RequestJoin</c>.
/// </summary>
/// <remarks>
/// The file transaction stays open across the queued switch. Every participant resets in the
/// <c>Program.SwapSystem</c> prefix, before KSA's teardown and at a joined worker boundary. The
/// following <c>DeserializeSave</c> then runs immediately (native order, before <c>OnGameLoaded</c>)
/// without a second reset, and the sidecar restores exactly once after native reconstruction.
/// Native timing is kept: <c>SwapSystem</c> rebuilds its renderers behind <c>Loading</c> frames, which
/// replace the ImGui draw data built earlier in that frame before anything is submitted.
/// </remarks>
public static partial class NativeSaveHooks
{
    private static UncompressedSave? _fileLoad;          // save whose Load is running inside the replay
    private static SystemSwapRequest? _fileLoadRequest;  // KSA's decision for that save
    private static string _fileLoadSystem = "";
    private static UncompressedSave? _queuedSwapLoad;    // file transaction waiting for KSA's queued switch
    private static UncompressedSave? _applyingLoad;      // save whose ApplyLoaded continuation is running
    private static UncompressedSave? _resumedSwapLoad;   // queued transaction resumed by the current DeserializeSave
    private static bool _swapReset;                      // SwapSystem already reset; its continuation must not reset again

    private static void ApplySystemSwap(Harmony harmony)
    {
        Patch(harmony, typeof(SaveSystemSwap), nameof(SaveSystemSwap.RequestLoad),
            new[] { typeof(string), typeof(string), typeof(Action) },
            prefix: nameof(WrapApply), postfix: nameof(RecordRequest));
        Patch(harmony, typeof(KSA.Program), nameof(KSA.Program.SwapSystem), new[] { typeof(SystemTemplate) },
            prefix: nameof(ResetBeforeSwap), postfix: nameof(AfterSwap), finalizer: nameof(FinishedSwap));
        PhysicsFrameHook.BeforePhysics -= ForgetStaleSwapReset;
        PhysicsFrameHook.BeforePhysics += ForgetStaleSwapReset;
    }

    private static void RemoveSystemSwap()
    {
        PhysicsFrameHook.BeforePhysics -= ForgetStaleSwapReset;
        _fileLoad = _queuedSwapLoad = _applyingLoad = _resumedSwapLoad = null;
        _fileLoadRequest = null;
        _fileLoadSystem = "";
        _swapReset = false;
    }

    private static void BeginFileLoad(UncompressedSave save)
    {
        _fileLoad = save;
        _fileLoadRequest = null;
        _fileLoadSystem = "";
        _swapReset = false;
    }

    private static void EndFileLoad()
    {
        _fileLoad = null;
        _fileLoadRequest = null;
    }

    /// <summary>RequestLoad prefix: tags ApplyLoaded so its later DeserializeSave is matched to this file load.</summary>
    private static void WrapApply(ref Action apply)
    {
        UncompressedSave? save = _fileLoad;
        if (save == null || apply == null) return;
        Action original = apply;
        apply = () => RunApply(save, original);
    }

    /// <summary>
    /// Runs KSA's ApplyLoaded (DeserializeSave + OnGameLoaded). A resumed switch transaction ends when it
    /// returns, matching where the Load finalizer ends an ordinary load.
    /// </summary>
    private static void RunApply(UncompressedSave save, Action original)
    {
        UncompressedSave? previous = _applyingLoad;
        _applyingLoad = save;
        Exception? failure = null;
        try { original(); }
        catch (Exception ex) { failure = ex; throw; }
        finally
        {
            _applyingLoad = previous;
            if (ReferenceEquals(_resumedSwapLoad, save)) FinishResumedSwapLoad(failure);
        }
    }

    private static void RecordRequest(string savedSystemId, SystemSwapRequest __result)
    {
        if (_fileLoad == null) return;
        _fileLoadRequest = __result;
        _fileLoadSystem = savedSystemId ?? "";
    }

    private static bool KeepOpenForQueuedSwap(UncompressedSave save)
    {
        if (_fileLoadRequest != SystemSwapRequest.Queued || !ReferenceEquals(_fileLoad, save)) return false;
        Console.WriteLine($"unscience saves: '{save.Id}' switches to star system '{_fileLoadSystem}'; "
            + "scene state is restored after KSA applies the switch.");
        _queuedSwapLoad = save;
        EndFileLoad();
        return true;
    }

    private static Exception NotLoadedFailure(UncompressedSave save) => _fileLoadRequest == SystemSwapRequest.NotInstalled
        ? new InvalidDataException($"Save '{save.Id}' was made in star system '{_fileLoadSystem}', which is not installed; "
            + "the current scene was left unchanged.")
        : new InvalidDataException($"KSA could not read save '{save.Id}'; the current scene was left unchanged.");

    /// <summary>Program.SwapSystem prefix: release old-world ownership before KSA destroys that world.</summary>
    private static void ResetBeforeSwap()
    {
        _swapReset = false;
        if (Universe.CurrentSystem == null || !FrameHookAvailable()) return;
        // SwapSystem's own first step is the same JobSystems.WaitAll; ours must come before the callbacks.
        _swapReset = ResetAtJoinedBoundary();
    }

    /// <summary>WontFit restarts the game into the save's system (process exit); nothing continues here.</summary>
    private static void AfterSwap(SystemSwapResult __result)
    {
        if (__result != SystemSwapResult.Swapped) _swapReset = false;
    }

    private static Exception? FinishedSwap(Exception? __exception)
    {
        if (__exception == null) return null;
        _swapReset = false;
        if (_queuedSwapLoad != null) EndQueuedSwap(__exception);
        return __exception;
    }

    private static bool TakeSwapReset()
    {
        bool reset = _swapReset;
        _swapReset = false;
        return reset;
    }

    /// <summary>Only a continuation of the swap that just ran may skip the reset; a missed one ends at the next handoff.</summary>
    private static void ForgetStaleSwapReset(double dtPlayer, UniverseTime previousTime) => _swapReset = false;

    private static void ResumeQueuedSwapLoad()
    {
        if (_queuedSwapLoad != null && ReferenceEquals(_applyingLoad, _queuedSwapLoad))
        {
            _resumedSwapLoad = _queuedSwapLoad;
            _queuedSwapLoad = null;
            _fileLoadDepth++;
            return;
        }
        // A join (or another request) replaced the queued switch before KSA ran it.
        if (_fileLoadDepth == 0) LastLoadError = null;
        SupersedeQueuedSwap("KSA switched systems for another request first");
    }

    private static bool JoinWithoutReset()
    {
        try
        {
            // The swap prefix already reset every participant. Only KSA's freshly created system,
            // which native DeserializeSave replaces next, exists since then.
            JobSystems.WaitAll();
            PhysicsFrameHook.ClearPending();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience saves: cannot join swapped-system load boundary: {ex}");
            throw;
        }
    }

    private static void FinishResumedSwapLoad(Exception? exception)
    {
        _resumedSwapLoad = null;
        _fileLoadDepth--;
        LastLoadError = exception;
        Invoke(LoadFinished, "finish load");
        if (exception != null) Invoke(LoadFailed, exception, "load failure");
    }

    private static void SupersedeQueuedSwap(string reason)
    {
        if (_queuedSwapLoad == null) return;
        EndQueuedSwap(new InvalidOperationException($"KSA did not apply the star-system switch for save "
            + $"'{_queuedSwapLoad.Id}' ({reason}); its Unscience state was kept, not restored."));
    }

    private static void EndQueuedSwap(Exception failure)
    {
        _queuedSwapLoad = null;
        Console.WriteLine($"unscience saves: {failure.Message}");
        LastLoadError = failure;
        Invoke(LoadFinished, "finish load");
        Invoke(LoadFailed, failure, "load failure");
    }
}
