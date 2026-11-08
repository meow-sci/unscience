using System;
using System.Reflection;
using HarmonyLib;
using Brutal.VulkanApi;
using KSA;

namespace MeowSci.Unscience;

/// <summary>Releases mod resources while the game still owns live workers, renderer and Vulkan device.</summary>
internal static class GameShutdownPatches
{
    private static Action? _beforeDispose;
    private static MethodInfo Target => AccessTools.DeclaredMethod(typeof(KSA.Program), nameof(IDisposable.Dispose))
        ?? throw new MissingMethodException("KSA.Program.Dispose()");
    private static MethodInfo PrefixMethod => AccessTools.DeclaredMethod(typeof(GameShutdownPatches), nameof(BeforeDispose));

    internal static void Apply(Harmony harmony, Action beforeDispose)
    {
        ArgumentNullException.ThrowIfNull(beforeDispose);
        Remove(harmony);
        try
        {
            _beforeDispose = beforeDispose;
            harmony.Patch(Target, prefix: new HarmonyMethod(PrefixMethod) { priority = Priority.First });
        }
        catch { Remove(harmony); throw; }
    }

    internal static void Remove(Harmony harmony)
    {
        harmony.Unpatch(Target, PrefixMethod);
        _beforeDispose = null;
    }

    private static void BeforeDispose()
    {
        var cleanup = _beforeDispose;
        _beforeDispose = null; // Callback can remove this patch and can re-enter Unload.
        if (cleanup == null) return;
        try
        {
            JobSystems.WaitAll();
            KSA.Program.GetRenderer()?.Device.WaitIdle();
            Console.WriteLine("unscience: Releasing submods before native renderer disposal.");
            cleanup();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience: Pre-renderer shutdown failed: {ex}");
        }
    }
}
