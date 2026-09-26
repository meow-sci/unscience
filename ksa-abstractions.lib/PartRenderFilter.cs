using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace MeowSci.KsaAbstractions;

/// <summary>
/// Hides selected parts' meshes while the parts stay in their vehicle (KSA 5482+).
///
/// KSA 5482 removed the per-module <c>*Module.UpdateRenderData</c> submissions. Part render data
/// is now cached per tree in <c>PartTreeRenderData</c>, and its public <c>Compose</c>,
/// <c>ComposeDynamic</c> and <c>ComposeGlass</c> append one contiguous, slot-ordered range per
/// model to the per-viewport instance lists every frame. This filter records where each range
/// starts and compacts hidden slots out of it before <c>PartModelRenderer.UpdateRenderData</c>
/// uploads the lists. Cached render data is never modified, so predicate changes take effect on
/// the next frame without invalidation, and draw counts and shadow bounds stay consistent.
///
/// Every owner shares one patch set: two independent compactions would each shift the ranges the
/// other measured. Raytraced IVA submissions bypass the instance lists and are not filtered.
/// </summary>
public static partial class PartRenderFilter
{
    private static readonly Dictionary<string, Func<Part, bool>> Owners = new(StringComparer.Ordinal);
    private static readonly List<(MethodBase Original, MethodInfo Patch)> Installed = new();
    private static Func<Part, bool>[] _filters = Array.Empty<Func<Part, bool>>();

    /// <summary>True while the shared compaction patches are attached.</summary>
    public static bool IsInstalled => Installed.Count > 0;

    /// <summary>
    /// Adds or replaces an owner's predicate. <paramref name="shouldHide"/> receives each full part
    /// (<c>Part.FullPart</c>) once per model instance per viewport per frame on the main thread.
    /// The first registration installs the shared patches with <paramref name="harmony"/>; it
    /// throws when the game no longer exposes the render-data members this filter relies on.
    /// </summary>
    public static void Register(Harmony harmony, string owner, Func<Part, bool> shouldHide)
    {
        if (!IsInstalled) Install(harmony);
        Owners[owner] = shouldHide;
        _filters = Owners.Values.ToArray();
    }

    /// <summary>Removes an owner's predicate; the last owner removes the shared patches.</summary>
    public static void Unregister(Harmony harmony, string owner)
    {
        if (!Owners.Remove(owner)) return;
        _filters = Owners.Values.ToArray();
        if (Owners.Count == 0) Uninstall(harmony);
    }

    private static bool ShouldHide(Part part)
    {
        Part fullPart = part.FullPart;
        foreach (Func<Part, bool> filter in _filters)
            if (filter(fullPart)) return true;
        return false;
    }

    private static void Install(Harmony harmony)
    {
        ResolveBatchAccessors();
        try
        {
            Patch(harmony, nameof(PartTreeRenderData.Compose), nameof(ComposePrefix), nameof(ComposePostfix));
            Patch(harmony, nameof(PartTreeRenderData.ComposeDynamic), nameof(ComposeDynamicPrefix), nameof(ComposeDynamicPostfix));
            Patch(harmony, nameof(PartTreeRenderData.ComposeGlass), nameof(ComposeGlassPrefix), nameof(ComposeGlassPostfix));
        }
        catch
        {
            Uninstall(harmony); // Never leave a partial filter that hides some model kinds only.
            throw;
        }
        Console.WriteLine("ksa-abstractions: PartRenderFilter patches applied");
    }

    private static void Uninstall(Harmony harmony)
    {
        foreach (var (original, patch) in Installed)
        {
            try { harmony.Unpatch(original, patch); }
            catch (Exception ex) { Console.WriteLine($"ksa-abstractions: PartRenderFilter unpatch failed: {ex.Message}"); }
        }
        if (Installed.Count > 0) Console.WriteLine("ksa-abstractions: PartRenderFilter patches removed");
        Installed.Clear();
    }

    /// <summary>All three compose methods share the exact signature
    /// <c>(ref readonly double4x4, bool, IViewport inViewport, int)</c>.</summary>
    private static void Patch(Harmony harmony, string target, string prefix, string postfix)
    {
        MethodInfo original = AccessTools.Method(typeof(PartTreeRenderData), target, new[]
        {
            typeof(double4x4).MakeByRefType(), typeof(bool), typeof(IViewport), typeof(int)
        }) ?? throw new MissingMethodException(nameof(PartTreeRenderData), target);
        MethodInfo prefixMethod = AccessTools.Method(typeof(PartRenderFilter), prefix);
        MethodInfo postfixMethod = AccessTools.Method(typeof(PartRenderFilter), postfix);
        harmony.Patch(original, prefix: new HarmonyMethod(prefixMethod), postfix: new HarmonyMethod(postfixMethod));
        Installed.Add((original, prefixMethod));
        Installed.Add((original, postfixMethod));
    }
}
