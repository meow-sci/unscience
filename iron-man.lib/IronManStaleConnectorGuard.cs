using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>
/// KSA 5866 added <c>PartInstance.DropStaleConnectors()</c>. World loads (<c>CelestialSystem</c>) and
/// blueprint loads (<c>UncompressedVehicleSave</c>) run it before construction to discard saved
/// connector references at or beyond the template's connector count. Owned anchors are exactly that
/// suffix, so without this guard every connected anchor would be dropped on load (with a yellow
/// "dropped connectors" alert) and the loss would become permanent on the next save.
/// The guard hides only a marked backpack's validated owned range from the native check; genuinely
/// stale indices, stock parts and the native child recursion are unchanged.
/// </summary>
internal static class IronManStaleConnectorGuard
{
    private static MethodInfo? _target;

    internal sealed class HiddenAnchors(List<PartConnectorInstance> original, HashSet<PartConnectorInstance> owned)
    {
        public List<PartConnectorInstance> Original { get; } = original;
        public HashSet<PartConnectorInstance> Owned { get; } = owned;
    }

    internal static void Apply(Harmony harmony)
    {
        _target = AccessTools.Method(typeof(PartInstance), nameof(PartInstance.DropStaleConnectors), Type.EmptyTypes)
            ?? throw new MissingMethodException("PartInstance.DropStaleConnectors()");
        harmony.Patch(_target,
            prefix: new HarmonyMethod(typeof(IronManStaleConnectorGuard), nameof(Prefix)),
            postfix: new HarmonyMethod(typeof(IronManStaleConnectorGuard), nameof(Postfix)));
    }

    internal static void Remove(Harmony harmony)
    {
        if (_target == null) return;
        harmony.Unpatch(_target, AccessTools.Method(typeof(IronManStaleConnectorGuard), nameof(Prefix)));
        harmony.Unpatch(_target, AccessTools.Method(typeof(IronManStaleConnectorGuard), nameof(Postfix)));
        _target = null;
    }

    private static void Prefix(PartInstance __instance, out HiddenAnchors? __state)
    {
        __state = null;
        List<PartConnectorInstance>? saved = __instance.Connectors;
        if (saved == null || saved.Count == 0 || __instance.Components?.OfType<IronManAnchorModule.SaveData>().Any() != true) return;
        IronManConnectorData? data;
        // Never throw here: the world-load call site is outside KSA's per-vehicle guard. A damaged
        // record is rejected by the constructor prefix inside that guard instead.
        try { data = IronManAnchorSerializers.Decode(__instance, __instance.GetTemplate()); }
        catch (Exception) { return; }
        if (data == null) return;
        int first = data.StockConnectorCount, end = first + data.Anchors.Count;
        var owned = saved.Where(c => c != null && c.Index >= first && c.Index < end).ToHashSet();
        if (owned.Count == 0) return;
        __state = new HiddenAnchors([.. saved], owned);
        saved.RemoveAll(owned.Contains);
    }

    private static void Postfix(PartInstance __instance, HiddenAnchors? __state)
    {
        if (__state == null) return;
        List<PartConnectorInstance> list = __instance.Connectors ??= new List<PartConnectorInstance>();
        var kept = list.ToHashSet();
        list.Clear();
        // Restore the saved order: native reconnection walks these entries in sequence.
        foreach (PartConnectorInstance entry in __state.Original)
            if (kept.Contains(entry) || __state.Owned.Contains(entry)) list.Add(entry);
    }
}
