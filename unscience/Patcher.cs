using System;
using HarmonyLib;
using KSA;
using MeowSci.BlinkyLib;
using MeowSci.CameraControllerOverrideLib;
using MeowSci.CameraControllerOverrideLib.Animation;
using MeowSci.EternalFlameLib;
using MeowSci.GlassLib;
using MeowSci.GarrysTorchLib;
using MeowSci.GodzillaLib;
using MeowSci.IFeelSeenLib;
using MeowSci.HumbleArteestLib;
using MeowSci.ItsSoShinyLib;
using MeowSci.KittenAnimationsLib;
using MeowSci.KitchenSinkLib;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.SphinxLib;
using MeowSci.IronManLib;
using MeowSci.KiwisMarblesLib;
using MeowSci.TheTickLib;
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
        var report = new PatchApplicationReport();
        try
        {
            _harmony = new Harmony("MeowSci.Unscience");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience: Error creating Harmony instance: {ex}");
            Console.WriteLine("unscience: Harmony patch startup aborted: no patch groups attempted.");
            return;
        }

        // Each feature is applied in isolation: a single feature failing to patch (e.g. a
        // stale reflection/field target after a game update) logs and is skipped instead of
        // aborting every feature registered after it in the chain.
        report.TryApply("hotkey-guard", () => HotkeyGuard.Patch(_harmony!));
        // Replays Mod.UpdateSubmods while the HUD is hidden (F2), since StarMap's
        // BeforeGui/AfterGui targets are skipped by the game in that state. Callbacks are
        // registered in Mod.OnFullyLoaded before Patch() runs.
        report.TryApply("hidden-ui-frame-hook", () => HiddenUiFrameHook.Patch(_harmony!));
        report.TryApply("thug-life", () => ThugLifeRenderPatches.Apply(_harmony!));
        report.TryApply("menu-bar", () =>
        {
            MenuBarPatch.ToggleWindow = MenuBarToggle;
            MenuBarPatch.Apply(_harmony!);
        });
        report.TryApply("scene saves", () => NativeSaveHooks.Apply(_harmony!));
        report.TryApply("dent-wizard physics handoff", () => PhysicsFrameHook.Apply(_harmony!));
        report.TryApply("blinky", () => BlinkyPatches.Apply(_harmony!));
        report.TryApply("its-so-shiny", () => ShinyPatches.Apply(_harmony!));
        report.TryApply("camera-controller-override", () =>
        {
            CameraControllerOverridePatches.SequencePlayer = CameraSequencePlayer;
            CameraControllerOverridePatches.Apply(_harmony!);
        });
        report.TryApply("eternal-flame", () => EternalFlamePatches.Apply(_harmony!));
        report.TryApply("kiwis-marbles", () => KiwisMarblesPatches.Apply(_harmony!));
        report.TryApply("garrys-torch weld timing", () => GarrysTorchPatches.Apply(_harmony!));
        report.TryApply("garrys-torch kitten scale", () => KittenScalePatches.Apply(_harmony!));
        report.TryApply("godzilla collider scale", () => ColliderScalePatches.Apply(_harmony!));
        report.TryApply("godzilla visual scale", () => VisualScalePatches.Apply(_harmony!));
        report.TryApply("glass", () => GlassPatches.Apply(_harmony!));
        report.TryApply("i-feel-seen", () => IFeelSeenPatches.Apply(_harmony!, IFeelSeenTracker!));
        report.TryApply("vehicle-paint", () => VehiclePaintPatches.Apply(_harmony!));
        report.TryApply("engine-emissive", () => EngineEmissivePatches.Apply(_harmony!));
        report.TryApply("kitten-visor", () => KittenVisorPatches.Apply(_harmony!));
        report.TryApply("iva-force-render", () => IvaForceRender.Patch(_harmony!));
        report.TryApply("kitchen-sink capsule glass", () => CapsuleGlassExperiment.Apply(_harmony!));
        report.TryApply("kitchen-sink G-load protection", () => GLoadProtectionPatches.Apply(_harmony!));
        report.TryApply("kitchen-sink IVA camera", () => IvaCameraUnlock.Apply(_harmony!));
        report.TryApply("the-tick indestructibility", () => TheTickPatches.Apply(_harmony!));
        report.TryApply("dont-stifle-me", () => EditorScalePatches.Apply(_harmony!));
        report.TryApply("dont-stifle-me editor limits", () => EditorValueLimitPatches.Apply(_harmony!));
        report.TryApply("kitten-animations", () => KittenAnimationPatches.Apply(_harmony!));
        report.TryApply("pyro", () => PyroPatches.Apply(_harmony!));
        report.TryApply("iron-man", () => IronManPatches.Apply(_harmony!));
        report.TryApply("sphinx", () => SphinxPatches.Apply(_harmony!));
        report.TryApply("graffiti", () => GraffitiPatches.Apply(_harmony!));
        report.TryApply("free-fallin", () => FreeFallinPatches.Apply(_harmony!));
        report.TryApply("pebbles", () => PebblesController?.ApplyPatches(_harmony!));
        report.TryApply("hot-pursuit", () => HotPursuitPatches.Apply(_harmony!));
        Console.WriteLine(report.Summary);
    }

    public static void Unload()
    {
        try
        {
            if (_harmony != null)
            {
                TryRemove("scene saves", () => NativeSaveHooks.Remove(_harmony!));
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
                TryRemove("godzilla collider scale", () => ColliderScalePatches.Remove(_harmony!));
                TryRemove("godzilla visual scale", () => VisualScalePatches.Remove(_harmony!));
                TryRemove("glass", () => GlassPatches.Remove(_harmony!));
                TryRemove("i-feel-seen", () => IFeelSeenPatches.Remove(_harmony!));
                TryRemove("engine-emissive", () => EngineEmissivePatches.Remove(_harmony!));
                TryRemove("kitten-visor", () => KittenVisorPatches.Remove(_harmony!));
                TryRemove("dont-stifle-me editor limits", () => EditorValueLimitPatches.Remove(_harmony!));
                TryRemove("dont-stifle-me", () => EditorScalePatches.Remove(_harmony!));
                TryRemove("vehicle-paint", () => VehiclePaintPatches.Remove(_harmony!));
                TryRemove("thug-life", () => ThugLifeRenderPatches.Remove(_harmony!));
                TryRemove("kitchen-sink capsule glass", () => CapsuleGlassExperiment.Remove(_harmony!));
                TryRemove("iva-force-render", () => IvaForceRender.Unpatch(_harmony!));
                TryRemove("kitchen-sink G-load protection", () => GLoadProtectionPatches.Remove(_harmony!));
                TryRemove("kitchen-sink IVA camera", () => IvaCameraUnlock.Remove(_harmony!));
                TryRemove("the-tick indestructibility", () => TheTickPatches.Remove(_harmony!));
                TryRemove("kitten-animations", () => KittenAnimationPatches.Remove(_harmony!));
                TryRemove("iron-man", () => IronManPatches.Remove(_harmony!));
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
            Console.WriteLine($"unscience: Error removing patches: {ex}");
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
            Console.WriteLine($"unscience: Failed to remove {feature} patches: {ex}");
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
