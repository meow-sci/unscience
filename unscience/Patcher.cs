using System;
using HarmonyLib;
using KSA;
using MeowSci.BlinkyLib;
using MeowSci.CameraControllerOverrideLib;
using MeowSci.CameraControllerOverrideLib.Animation;
using MeowSci.EternalFlameLib;
using MeowSci.GlassLib;
using MeowSci.GarrysTorchLib;
using MeowSci.IFeelSeenLib;
using MeowSci.HumbleArteestLib;
using MeowSci.ItsSoShinyLib;
using MeowSci.KittenAnimationsLib;
using MeowSci.KsaAbstractions;
using MeowSci.SphinxLib;
using MeowSci.KiwisMarblesLib;
using MeowSci.ThugLifeLib;
using MeowSci.DontStifleMeLib;
using MeowSci.GraffitiLib;
using MeowSci.FreeFallinLib;
using MeowSci.HotPursuitLib;
using MeowSci.PyroLib;
using MeowSci.PebblesLib;

namespace MeowSci.Unscience;

internal static class Patcher
{
    private static Harmony? _harmony;

    public static VehicleTracker? IFeelSeenTracker { private get; set; }
    public static KeyframeSequencePlayer? CameraSequencePlayer { private get; set; }
    public static ClutterController? PebblesController { private get; set; }
    public static Action? MenuBarToggle { get; set; }

    public static void Patch()
    {
        try
        {
            _harmony = new Harmony("MeowSci.Unscience");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience: Error creating Harmony instance: {ex.Message}");
            return;
        }

        // Each feature is applied in isolation: a single feature failing to patch (e.g. a
        // stale reflection/field target after a game update) logs and is skipped instead of
        // aborting every feature registered after it in the chain.
        TryApply("hotkey-guard", () => HotkeyGuard.Patch(_harmony!));
        // Replays Mod.UpdateSubmods while the HUD is hidden (F2), since StarMap's
        // BeforeGui/AfterGui targets are skipped by the game in that state. Callbacks are
        // registered in Mod.OnFullyLoaded before Patch() runs.
        TryApply("hidden-ui-frame-hook", () => HiddenUiFrameHook.Patch(_harmony!));
        TryApply("thug-life", () => ThugLifeRenderPatches.Apply(_harmony!));
        TryApply("menu-bar", () =>
        {
            MenuBarPatch.ToggleWindow = MenuBarToggle;
            MenuBarPatch.Apply(_harmony!);
        });
        TryApply("blinky", () => BlinkyPatches.Apply(_harmony!));
        TryApply("its-so-shiny", () => ShinyPatches.Apply(_harmony!));
        TryApply("camera-controller-override", () =>
        {
            CameraControllerOverridePatches.SequencePlayer = CameraSequencePlayer;
            CameraControllerOverridePatches.Apply(_harmony!);
        });
        TryApply("eternal-flame", () => EternalFlamePatches.Apply(_harmony!));
        TryApply("kiwis-marbles", () => KiwisMarblesPatches.Apply(_harmony!));
        TryApply("garrys-torch weld timing", () => GarrysTorchPatches.Apply(_harmony!));
        TryApply("garrys-torch kitten scale", () => KittenScalePatches.Apply(_harmony!));
        TryApply("glass", () => GlassPatches.Apply(_harmony!));
        TryApply("i-feel-seen", () => IFeelSeenPatches.Apply(_harmony!, IFeelSeenTracker!));
        TryApply("vehicle-paint", () => VehiclePaintPatches.Apply(_harmony!));
        TryApply("engine-emissive", () => EngineEmissivePatches.Apply(_harmony!));
        TryApply("iva-force-render", () => IvaForceRender.Patch(_harmony!));
        TryApply("dont-stifle-me", () => EditorScalePatches.Apply(_harmony!));
        TryApply("dont-stifle-me editor limits", () => EditorValueLimitPatches.Apply(_harmony!));
        TryApply("kitten-animations", () => KittenAnimationPatches.Apply(_harmony!));
        TryApply("pyro", () => PyroPatches.Apply(_harmony!));
        TryApply("sphinx", () => SphinxPatches.Apply(_harmony!));
        TryApply("graffiti", () => GraffitiPatches.Apply(_harmony!));
        TryApply("free-fallin", () => FreeFallinPatches.Apply(_harmony!));
        TryApply("pebbles", () => PebblesController?.ApplyPatches(_harmony!));
        TryApply("hot-pursuit", () => HotPursuitPatches.Apply(_harmony!));
        Console.WriteLine("unscience: Harmony patches applied");
    }

    private static void TryApply(string feature, Action apply)
    {
        try
        {
            apply();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience: Failed to apply {feature} patches: {ex.Message}");
        }
    }

    public static void Unload()
    {
        try
        {
            if (_harmony != null)
            {
                TryRemove("hotkey-guard", () => HotkeyGuard.Unpatch(_harmony!));
                TryRemove("hidden-ui-frame-hook", () => HiddenUiFrameHook.Unpatch(_harmony!));
                TryRemove("menu-bar", () => MenuBarPatch.Remove(_harmony!));
                TryRemove("blinky", () => BlinkyPatches.Remove(_harmony!));
                TryRemove("its-so-shiny", () => ShinyPatches.Remove(_harmony!));
                TryRemove("camera-controller-override", () => CameraControllerOverridePatches.Remove(_harmony!));
                TryRemove("eternal-flame", () => EternalFlamePatches.Remove(_harmony!));
                TryRemove("kiwis-marbles", () => KiwisMarblesPatches.Remove(_harmony!));
                TryRemove("garrys-torch weld timing", () => GarrysTorchPatches.Remove(_harmony!));
                TryRemove("garrys-torch kitten scale", () => KittenScalePatches.Remove(_harmony!));
                TryRemove("glass", () => GlassPatches.Remove(_harmony!));
                TryRemove("i-feel-seen", () => IFeelSeenPatches.Remove(_harmony!));
                TryRemove("engine-emissive", () => EngineEmissivePatches.Remove(_harmony!));
                TryRemove("dont-stifle-me editor limits", () => EditorValueLimitPatches.Remove(_harmony!));
                TryRemove("dont-stifle-me", () => EditorScalePatches.Remove(_harmony!));
                TryRemove("vehicle-paint", () => VehiclePaintPatches.Remove(_harmony!));
                TryRemove("thug-life", () => ThugLifeRenderPatches.Remove(_harmony!));
                TryRemove("iva-force-render", () => IvaForceRender.Unpatch(_harmony!));
                TryRemove("kitten-animations", () => KittenAnimationPatches.Remove(_harmony!));
                TryRemove("sphinx", () => SphinxPatches.Remove(_harmony!));
                TryRemove("pyro", () => PyroPatches.Remove(_harmony!));
                TryRemove("graffiti", () => GraffitiPatches.Remove(_harmony!));
                TryRemove("free-fallin", () => FreeFallinPatches.Remove(_harmony!));
                TryRemove("pebbles", () => PebblesController?.RemovePatches(_harmony!));
                TryRemove("hot-pursuit", () => HotPursuitPatches.Remove(_harmony!));
            }
            VehiclePaint.Cleanup();
            EngineEmissive.Cleanup();
            _harmony = null;
            IFeelSeenTracker = null;
            CameraSequencePlayer = null;
            PebblesController = null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience: Error removing patches: {ex.Message}");
        }
    }

    private static void TryRemove(string feature, Action remove)
    {
        try
        {
            remove();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience: Failed to remove {feature} patches: {ex.Message}");
        }
    }
}

internal static class EternalFlamePatches
{
    public static void Apply(Harmony harmony)
    {
        var original = AccessTools.Method(typeof(Universe), nameof(Universe.ExecuteNextVehicleSolvers));
        var prefixMethod = AccessTools.Method(typeof(EternalFlamePatches), nameof(BeforeVehicleSolvers));

        if (original == null)
            throw new MissingMethodException(typeof(Universe).FullName, nameof(Universe.ExecuteNextVehicleSolvers));
        if (prefixMethod == null)
            throw new MissingMethodException(typeof(EternalFlamePatches).FullName, nameof(BeforeVehicleSolvers));

        harmony.Patch(original, prefix: new HarmonyMethod(prefixMethod) { priority = Priority.First });
    }

    public static void Remove(Harmony harmony)
    {
        var original = AccessTools.Method(typeof(Universe), nameof(Universe.ExecuteNextVehicleSolvers));
        var prefixMethod = AccessTools.Method(typeof(EternalFlamePatches), nameof(BeforeVehicleSolvers));
        if (original != null && prefixMethod != null)
            harmony.Unpatch(original, prefixMethod);
    }

    private static void BeforeVehicleSolvers()
    {
        try
        {
            EternalFlameSubmod.Instance?.UpdateBeforeVehicleSolvers();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"eternal-flame: Error in solver prefix: {ex.Message}\n{ex}");
        }
    }
}
