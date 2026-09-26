using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Brutal.Numerics;

namespace Brutal.Numerics
{
public struct double4x4
{
}

public struct float3
{
    public float X, Y, Z;

    public float3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }
}
}

namespace KSA
{
// KSA 5482 part render data. The stock types are native-backed; these fixtures keep only the
// members PartRenderFilter and VehiclePaintPatches reflect or patch. Instance lists hold part tags
// (dent lists hold tag + 1000) so compaction order and dent alignment are observable.
[Flags]
public enum ViewportOptionFlags
{
    None = 0,
    RenderPartModels = 0x1000,
}

public interface IViewport
{
    ViewportOptionFlags Options { get; }
}

public static class ViewportEx
{
    public static bool HasAny(this IViewport inViewport, ViewportOptionFlags inOptionFlags) =>
        (inViewport.Options & inOptionFlags) != 0;
}

public sealed class TestViewport(ViewportOptionFlags options) : IViewport
{
    public ViewportOptionFlags Options { get; } = options;
}

public class Part(string id, int tag, Part? fullPart = null)
{
    public string Id { get; } = id;
    public int Tag { get; } = tag;
    public Part FullPart => fullPart ?? this;
}

public sealed class PartTree
{
}

public sealed class PartModelDynamicModule(Part parent)
{
    public Part Parent { get; } = parent;
}

public sealed class PartModel
{
    private readonly Dictionary<IViewport, ViewportData> _views = new();

    /// <summary>False models the raytraced-IVA / internal-outside-IVA branches, which append nothing.</summary>
    public bool RasterPath = true;

    public sealed class ViewportData
    {
        public readonly List<int> InstanceList = new();
        public readonly List<int> DentInstanceList = new();

        public static ViewportData Get(PartModel partModel, IViewport viewport) =>
            partModel._views.TryGetValue(viewport, out var data) ? data : partModel._views[viewport] = new ViewportData();
    }
}

public sealed class PartModelDynamic
{
    private readonly Dictionary<IViewport, ViewportData> _views = new();

    public sealed class ViewportData
    {
        public readonly List<int> InstanceList = new();
        public readonly List<int> DentInstanceList = new();

        public static ViewportData Get(PartModelDynamic partModelDynamic, IViewport viewport) =>
            partModelDynamic._views.TryGetValue(viewport, out var data) ? data : partModelDynamic._views[viewport] = new ViewportData();
    }
}

public sealed class PartModelGlass
{
    private readonly Dictionary<IViewport, ViewportData> _views = new();

    public sealed class ViewportData
    {
        public readonly List<int> InstanceList = new();

        public static ViewportData Get(PartModelGlass partModelGlass, IViewport viewport) =>
            partModelGlass._views.TryGetValue(viewport, out var data) ? data : partModelGlass._views[viewport] = new ViewportData();
    }
}

public sealed class PartTreeRenderData
{
    private sealed class Batch
    {
        public PartModel Model = null!;
        public Part[] Parts = new Part[16];
        public int[] StateBitFlags = new int[16];
        public int Count;
    }

    private sealed class DynamicBatch
    {
        public PartModelDynamic Model = null!;
        public PartModelDynamicModule[] Modules = new PartModelDynamicModule[16];
        public Part[] Parts = new Part[16];
        public int[] StateBitFlags = new int[16];
        public int Count;
    }

    private sealed class GlassBatch
    {
        public PartModelGlass Model = null!;
        public Part[] Parts = new Part[16];
        public int Count;
    }

    private readonly List<Batch> _batches = new();
    private readonly List<GlassBatch> _glassBatches = new();
    private readonly List<DynamicBatch> _dynamicBatches = new();
    private bool _allStatesDirty = true;

    public int StateRewrites { get; private set; }

    public void AddStatic(PartModel model, params Part[] parts)
    {
        var batch = new Batch { Model = model, Count = parts.Length };
        parts.CopyTo(batch.Parts, 0);
        _batches.Add(batch);
    }

    public void AddDynamic(PartModelDynamic model, params PartModelDynamicModule[] modules)
    {
        var batch = new DynamicBatch { Model = model, Count = modules.Length };
        for (int i = 0; i < modules.Length; i++)
        {
            batch.Modules[i] = modules[i];
            batch.Parts[i] = modules[i].Parent;
        }
        _dynamicBatches.Add(batch);
    }

    public void AddGlass(PartModelGlass model, params Part[] parts)
    {
        var batch = new GlassBatch { Model = model, Count = parts.Length };
        parts.CopyTo(batch.Parts, 0);
        _glassBatches.Add(batch);
    }

    public int StaticFlags(int batch, int slot) => _batches[batch].StateBitFlags[slot];
    public int DynamicFlags(int batch, int slot) => _dynamicBatches[batch].StateBitFlags[slot];

    public void InvalidateStates() => _allStatesDirty = true;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void EnsureBuilt(PartTree inTree, ulong inFrameCount)
    {
        if (!_allStatesDirty) return;
        foreach (Batch batch in _batches)
            for (int i = 0; i < batch.Count; i++) WriteState(batch, i, batch.Parts[i]);
        foreach (DynamicBatch batch in _dynamicBatches)
            for (int i = 0; i < batch.Count; i++) WriteDynamicState(batch, i, batch.Modules[i]);
        _allStatesDirty = false;
        StateRewrites++;
    }

    // Stock state bits stay below bit 11; paint must be ORed on top.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WriteState(Batch inBatch, int inSlot, Part inPart) => inBatch.StateBitFlags[inSlot] = 0b101;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void WriteDynamicState(DynamicBatch inBatch, int inSlot, PartModelDynamicModule inModule) =>
        inBatch.StateBitFlags[inSlot] = 0b11;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Compose(ref readonly double4x4 inMatrixVehicleAsmb2Ego, bool inIsEditedVehicle, IViewport inViewport, int inFrameIndex)
    {
        if (!inViewport.HasAny(ViewportOptionFlags.RenderPartModels)) return;
        foreach (Batch batch in _batches)
        {
            if (batch.Count == 0 || !batch.Model.RasterPath) continue;
            var data = PartModel.ViewportData.Get(batch.Model, inViewport);
            for (int j = 0; j < batch.Count; j++)
            {
                data.InstanceList.Add(batch.Parts[j].Tag);
                data.DentInstanceList.Add(batch.Parts[j].Tag + 1000);
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ComposeDynamic(ref readonly double4x4 inMatrixVehicleAsmb2Ego, bool inIsEditedVehicle, IViewport inViewport, int inFrameIndex)
    {
        if (!inViewport.HasAny(ViewportOptionFlags.RenderPartModels)) return;
        foreach (DynamicBatch batch in _dynamicBatches)
        {
            var data = PartModelDynamic.ViewportData.Get(batch.Model, inViewport);
            for (int j = 0; j < batch.Count; j++)
            {
                data.InstanceList.Add(batch.Parts[j].Tag);
                data.DentInstanceList.Add(batch.Parts[j].Tag + 1000);
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void ComposeGlass(ref readonly double4x4 inMatrixVehicleAsmb2Ego, bool inIsEditedVehicle, IViewport inViewport, int inFrameIndex)
    {
        if (!inViewport.HasAny(ViewportOptionFlags.RenderPartModels)) return;
        foreach (GlassBatch batch in _glassBatches)
        {
            var data = PartModelGlass.ViewportData.Get(batch.Model, inViewport);
            for (int j = 0; j < batch.Count; j++) data.InstanceList.Add(batch.Parts[j].Tag);
        }
    }
}
}
