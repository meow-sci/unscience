using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HarmonyLib;
using KSA;

namespace MeowSci.KsaAbstractions;

/// <summary>
/// Range bookkeeping over the private per-model batches of <c>PartTreeRenderData</c>
/// (<c>_batches</c>/<c>_dynamicBatches</c>/<c>_glassBatches</c>, each batch exposing
/// <c>Model</c>, <c>Parts</c> and <c>Count</c>). A batch's range is filtered only when the compose
/// call appended exactly <c>Count</c> entries, i.e. the raster path; raytraced IVA and internal
/// meshes outside IVA append nothing and are left alone.
/// </summary>
public static partial class PartRenderFilter
{
    private static AccessTools.FieldRef<PartTreeRenderData, IList>? _staticBatches;
    private static AccessTools.FieldRef<PartTreeRenderData, IList>? _dynamicBatches;
    private static AccessTools.FieldRef<PartTreeRenderData, IList>? _glassBatches;
    private static AccessTools.FieldRef<object, PartModel>? _staticModel;
    private static AccessTools.FieldRef<object, PartModelDynamic>? _dynamicModel;
    private static AccessTools.FieldRef<object, PartModelGlass>? _glassModel;
    private static AccessTools.FieldRef<object, Part[]>? _staticParts, _dynamicParts, _glassParts;
    private static AccessTools.FieldRef<object, int>? _staticCount, _dynamicCount, _glassCount;

    // Range starts recorded by each prefix, indexed like the batch list. Compose calls never nest.
    private static readonly List<int> StaticStarts = new();
    private static readonly List<int> DynamicStarts = new();
    private static readonly List<int> GlassStarts = new();
    private static bool[] _hidden = new bool[64];
    private static bool _faulted;

    private static void ResolveBatchAccessors()
    {
        Type batch = Inner("Batch"), dynamic = Inner("DynamicBatch"), glass = Inner("GlassBatch");
        _staticBatches = AccessTools.FieldRefAccess<PartTreeRenderData, IList>("_batches");
        _dynamicBatches = AccessTools.FieldRefAccess<PartTreeRenderData, IList>("_dynamicBatches");
        _glassBatches = AccessTools.FieldRefAccess<PartTreeRenderData, IList>("_glassBatches");
        _staticModel = AccessTools.FieldRefAccess<PartModel>(batch, "Model");
        _dynamicModel = AccessTools.FieldRefAccess<PartModelDynamic>(dynamic, "Model");
        _glassModel = AccessTools.FieldRefAccess<PartModelGlass>(glass, "Model");
        _staticParts = AccessTools.FieldRefAccess<Part[]>(batch, "Parts");
        _dynamicParts = AccessTools.FieldRefAccess<Part[]>(dynamic, "Parts");
        _glassParts = AccessTools.FieldRefAccess<Part[]>(glass, "Parts");
        _staticCount = AccessTools.FieldRefAccess<int>(batch, "Count");
        _dynamicCount = AccessTools.FieldRefAccess<int>(dynamic, "Count");
        _glassCount = AccessTools.FieldRefAccess<int>(glass, "Count");
    }

    private static Type Inner(string name) => AccessTools.Inner(typeof(PartTreeRenderData), name)
        ?? throw new MissingMemberException(nameof(PartTreeRenderData), name);

    private static bool Active(IViewport viewport) =>
        !_faulted && _filters.Length > 0 && viewport.HasAny(ViewportOptionFlags.RenderPartModels);

    // ---- Static part models (instance + dent lists) ----

    private static void ComposePrefix(PartTreeRenderData __instance, IViewport inViewport, out bool __state)
    {
        __state = Active(inViewport);
        if (!__state) return;
        try
        {
            StaticStarts.Clear();
            IList batches = _staticBatches!(__instance);
            for (int i = 0; i < batches.Count; i++)
            {
                object batch = batches[i]!; // batch lists never hold null
                StaticStarts.Add(_staticCount!(batch) == 0 ? -1
                    : PartModel.ViewportData.Get(_staticModel!(batch), inViewport).InstanceList.Count);
            }
        }
        catch (Exception ex) { __state = false; Fault(ex); }
    }

    private static void ComposePostfix(PartTreeRenderData __instance, IViewport inViewport, bool __state)
    {
        if (!__state) return;
        try
        {
            IList batches = _staticBatches!(__instance);
            for (int i = 0; i < batches.Count && i < StaticStarts.Count; i++)
            {
                object batch = batches[i]!; // batch lists never hold null
                int start = StaticStarts[i];
                if (start < 0) continue;
                var data = PartModel.ViewportData.Get(_staticModel!(batch), inViewport);
                int count = _staticCount!(batch);
                if (data.DentInstanceList.Count - start != count) continue;
                if (!MarkHidden(_staticParts!(batch), count, data.InstanceList.Count - start)) continue;
                Compact(data.InstanceList, start, count);
                Compact(data.DentInstanceList, start, count);
            }
        }
        catch (Exception ex) { Fault(ex); }
    }

    // ---- Dynamic part models (instance + dent lists) ----

    private static void ComposeDynamicPrefix(PartTreeRenderData __instance, IViewport inViewport, out bool __state)
    {
        __state = Active(inViewport);
        if (!__state) return;
        try
        {
            DynamicStarts.Clear();
            IList batches = _dynamicBatches!(__instance);
            for (int i = 0; i < batches.Count; i++)
            {
                object batch = batches[i]!; // batch lists never hold null
                DynamicStarts.Add(_dynamicCount!(batch) == 0 ? -1
                    : PartModelDynamic.ViewportData.Get(_dynamicModel!(batch), inViewport).InstanceList.Count);
            }
        }
        catch (Exception ex) { __state = false; Fault(ex); }
    }

    private static void ComposeDynamicPostfix(PartTreeRenderData __instance, IViewport inViewport, bool __state)
    {
        if (!__state) return;
        try
        {
            IList batches = _dynamicBatches!(__instance);
            for (int i = 0; i < batches.Count && i < DynamicStarts.Count; i++)
            {
                object batch = batches[i]!; // batch lists never hold null
                int start = DynamicStarts[i];
                if (start < 0) continue;
                var data = PartModelDynamic.ViewportData.Get(_dynamicModel!(batch), inViewport);
                int count = _dynamicCount!(batch);
                if (data.DentInstanceList.Count - start != count) continue;
                if (!MarkHidden(_dynamicParts!(batch), count, data.InstanceList.Count - start)) continue;
                Compact(data.InstanceList, start, count);
                Compact(data.DentInstanceList, start, count);
            }
        }
        catch (Exception ex) { Fault(ex); }
    }

    // ---- Glass part models (instance list only) ----

    private static void ComposeGlassPrefix(PartTreeRenderData __instance, IViewport inViewport, out bool __state)
    {
        __state = Active(inViewport);
        if (!__state) return;
        try
        {
            GlassStarts.Clear();
            IList batches = _glassBatches!(__instance);
            for (int i = 0; i < batches.Count; i++)
            {
                object batch = batches[i]!; // batch lists never hold null
                GlassStarts.Add(_glassCount!(batch) == 0 ? -1
                    : PartModelGlass.ViewportData.Get(_glassModel!(batch), inViewport).InstanceList.Count);
            }
        }
        catch (Exception ex) { __state = false; Fault(ex); }
    }

    private static void ComposeGlassPostfix(PartTreeRenderData __instance, IViewport inViewport, bool __state)
    {
        if (!__state) return;
        try
        {
            IList batches = _glassBatches!(__instance);
            for (int i = 0; i < batches.Count && i < GlassStarts.Count; i++)
            {
                object batch = batches[i]!; // batch lists never hold null
                int start = GlassStarts[i];
                if (start < 0) continue;
                var data = PartModelGlass.ViewportData.Get(_glassModel!(batch), inViewport);
                int count = _glassCount!(batch);
                if (!MarkHidden(_glassParts!(batch), count, data.InstanceList.Count - start)) continue;
                Compact(data.InstanceList, start, count);
            }
        }
        catch (Exception ex) { Fault(ex); }
    }

    // ---- Shared helpers ----

    /// <summary>Fills <see cref="_hidden"/> for a complete raster range; false when nothing to remove.</summary>
    private static bool MarkHidden(Part[] parts, int count, int appended)
    {
        if (count == 0 || appended != count) return false;
        if (_hidden.Length < count) _hidden = new bool[Math.Max(count, _hidden.Length * 2)];
        bool any = false;
        for (int j = 0; j < count; j++)
        {
            bool hide = ShouldHide(parts[j]);
            _hidden[j] = hide;
            any |= hide;
        }
        return any;
    }

    /// <summary>Removes the slots marked in <see cref="_hidden"/> from the list's tail range, in order.</summary>
    private static void Compact<T>(List<T> list, int start, int count)
    {
        Span<T> range = CollectionsMarshal.AsSpan(list).Slice(start, count);
        int write = 0;
        for (int read = 0; read < count; read++)
        {
            if (_hidden[read]) continue;
            range[write++] = range[read];
        }
        list.RemoveRange(start + write, count - write);
    }

    /// <summary>A filter bug must never break the render loop: log once and render everything.</summary>
    private static void Fault(Exception ex)
    {
        _faulted = true;
        Console.WriteLine($"ksa-abstractions: PartRenderFilter disabled after an error: {ex}");
    }
}
