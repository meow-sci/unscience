using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using KSA;

namespace MeowSci.KsaAbstractions;

/// <summary>
/// Forces IVA (interior) parts to render even when not in IVA camera mode
/// by directly mutating Template.Internal on all loaded PartModel instances.
/// </summary>
public static class IvaForceRender
{
    private static bool _enabled;
    private static readonly List<PartModelModule.Template> _mutatedTemplates = new();
    // Editor-only reveal: internal templates known so far, and those revealed for one Compose call.
    private static readonly List<PartModelModule.Template> _internalTemplates = new();
    private static readonly List<PartModelModule.Template> _revealed = new();
    private static bool _internalTemplatesDirty = true;

    private static MethodBase? _ctorOriginal;
    private static MethodInfo? _ctorPostfix;
    private static MethodBase? _composeOriginal;
    private static MethodInfo? _composePrefix;
    private static MethodInfo? _composeFinalizer;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (value)
                ForceInternalVisible();
            else
                RestoreInternalHidden();
        }
    }

    /// <summary>
    /// Apply IVA force-render Harmony patches. Call from Patcher.Patch().
    /// </summary>
    public static void Patch(Harmony harmony)
    {
        _ctorOriginal = AccessTools.Constructor(typeof(PartModel), new[] { typeof(PartModelModule.Template) });
        _ctorPostfix = typeof(IvaForceRender).GetMethod(nameof(CtorPostfix), BindingFlags.NonPublic | BindingFlags.Static)!;
        harmony.Patch(_ctorOriginal, postfix: new HarmonyMethod(_ctorPostfix));

        // KSA 5482 raster-composes static part models from cached batches and applies the
        // internal/IVA gate itself, without calling PartModel.AddInstance.
        _composeOriginal = AccessTools.Method(typeof(PartTreeRenderData), nameof(PartTreeRenderData.Compose), new[] { typeof(Brutal.Numerics.double4x4).MakeByRefType(), typeof(bool), typeof(IViewport), typeof(int) })
            ?? throw new MissingMethodException(nameof(PartTreeRenderData), nameof(PartTreeRenderData.Compose));
        _composePrefix = typeof(IvaForceRender).GetMethod(nameof(ComposePrefix), BindingFlags.NonPublic | BindingFlags.Static)!;
        _composeFinalizer = typeof(IvaForceRender).GetMethod(nameof(ComposeFinalizer), BindingFlags.NonPublic | BindingFlags.Static)!;
        harmony.Patch(_composeOriginal, prefix: new HarmonyMethod(_composePrefix), finalizer: new HarmonyMethod(_composeFinalizer));

        Console.WriteLine("ksa-abstractions: IvaForceRender patches applied");
    }

    /// <summary>
    /// Remove IVA force-render Harmony patches. Call from Patcher.Unload().
    /// </summary>
    public static void Unpatch(Harmony harmony)
    {
        if (_ctorOriginal != null && _ctorPostfix != null)
            harmony.Unpatch(_ctorOriginal, _ctorPostfix);
        if (_composeOriginal != null && _composePrefix != null)
            harmony.Unpatch(_composeOriginal, _composePrefix);
        if (_composeOriginal != null && _composeFinalizer != null)
            harmony.Unpatch(_composeOriginal, _composeFinalizer);

        _ctorOriginal = null;
        _ctorPostfix = null;
        _composeOriginal = null;
        _composePrefix = null;
        _composeFinalizer = null;
        _internalTemplates.Clear();
        _internalTemplatesDirty = true;

        Console.WriteLine("ksa-abstractions: IvaForceRender patches removed");
    }

    /// <summary>
    /// Called by the constructor patch to handle parts created after the toggle is enabled.
    /// </summary>
    public static void TrackMutated(PartModelModule.Template template)
    {
        if (!_mutatedTemplates.Contains(template))
            _mutatedTemplates.Add(template);
    }

    /// <summary>
    /// Postfix for PartModel constructor — mutates Template.Internal = false on new internal parts
    /// so they render outside IVA mode when the toggle is active.
    /// </summary>
    private static void CtorPostfix(PartModel __instance)
    {
        _internalTemplatesDirty = true;
        if (!_enabled) return;
        if (!__instance.Template.Internal) return;

        __instance.Template.Internal = false;
        TrackMutated(__instance.Template);
    }

    /// <summary>
    /// Keeps internal meshes visible in the vehicle editor outside IVA. Reveals every internal
    /// template for this one Compose call so stock code appends instances and dents consistently;
    /// <see cref="ComposeFinalizer"/> restores them even when Compose throws. Only the main-thread
    /// render path reads <c>Template.Internal</c>, so no other reader observes the change.
    /// </summary>
    private static void ComposePrefix(IViewport inViewport)
    {
        if (Program.Editor == null || inViewport.Mode == CameraMode.IVA
            || !inViewport.HasAny(ViewportOptionFlags.RenderPartModels)) return;
        if (_internalTemplatesDirty) RebuildInternalTemplates();
        foreach (var template in _internalTemplates)
        {
            if (!template.Internal) continue;
            template.Internal = false;
            _revealed.Add(template);
        }
    }

    private static Exception? ComposeFinalizer(Exception? __exception)
    {
        foreach (var template in _revealed)
            template.Internal = true;
        _revealed.Clear();
        return __exception;
    }

    private static void RebuildInternalTemplates()
    {
        _internalTemplates.Clear();
        foreach (var pm in PartModel.Instances)
        {
            var template = pm.Template;
            if (template.Internal && template.RayTracing != PartModelModule.RaytracingMode.ShadowProxy
                && !_internalTemplates.Contains(template))
                _internalTemplates.Add(template);
        }
        _internalTemplatesDirty = false;
    }

    private static void ForceInternalVisible()
    {
        _mutatedTemplates.Clear();
        foreach (var pm in PartModel.Instances)
        {
            if (pm.Template.Internal)
            {
                _mutatedTemplates.Add(pm.Template);
                pm.Template.Internal = false;
            }
        }
        Console.WriteLine($"ksa-abstractions: IvaForceRender forced {_mutatedTemplates.Count} internal templates visible");
    }

    private static void RestoreInternalHidden()
    {
        foreach (var t in _mutatedTemplates)
            t.Internal = true;
        _internalTemplatesDirty = true;
        Console.WriteLine($"ksa-abstractions: IvaForceRender restored {_mutatedTemplates.Count} internal templates");
        _mutatedTemplates.Clear();
    }
}
