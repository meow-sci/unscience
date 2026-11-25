using System;
using System.Collections.Generic;

// Managed stand-ins for the KSA 5638 per-viewport clutter residency surface that
// pebbles.lib/Runtime/ClutterResidency.cs calls. Semantics follow ClutterEcotypeRenderData
// ReleaseViewport/ResolvePendingReleases: a slot release clears that slot's mask bit on each loaded
// cell, and a cell returns its pooled slot only once no viewport mask remains. Since KSA 5875 those
// arrays are indexed by GroundClutterRenderer's clutter slot, which only Main/Secondary viewports own;
// resolving any other registered viewport throws. Since KSA 5919 ReleaseViewport takes that resolved
// slot instead of the viewport. KSA 5919/5920 per-planet buffers follow AllocatePlanetResources /
// DisposePlanetResources: both are guarded by HasPlanetResources, and disposing while residences remain
// is what native logs as "disposed while ecotype ... still holds cells". No game source is copied.
namespace KSA
{
public interface IViewport
{
    int ShaderSlot { get; }
}

public static class ViewportRegistry
{
    internal static IViewport[] Registered = [];
    public static ReadOnlySpan<IViewport> Views => Registered;
}

public static class GroundClutterRenderer
{
    private static readonly Dictionary<int, int> Lookup = [];

    /// <summary>Fixture control: the viewports the renderer constructor mapped (Main/Secondary in game).</summary>
    internal static void MapClutterViewports(params IViewport[] viewports)
    {
        Lookup.Clear();
        foreach (var viewport in viewports) Lookup[viewport.ShaderSlot] = Lookup.Count;
    }

    public static int ResolveViewportSlot(IViewport viewport) =>
        Lookup.TryGetValue(viewport.ShaderSlot, out int slot)
            ? slot
            : throw new Exception($"Viewport {viewport.ShaderSlot} not registered for clutter rendering");

    public static bool HasViewportSlot(IViewport viewport) => Lookup.ContainsKey(viewport.ShaderSlot);
}

public sealed class ClutterEcotypeRenderData
{
    private readonly HashSet<int>[] _loaded = new HashSet<int>[5];
    private readonly Dictionary<int, uint> _masks = [];
    private readonly List<int> _pendingRelease = [];
    public IViewport? GenerationViewport;
    public int FreeSlots { get; private set; }
    public int Resolves { get; private set; }
    public bool HasPlanetResources { get; private set; }
    /// <summary>Fixture probes: per-planet buffer frees, and frees that happened while cells were still resident.</summary>
    public int PlanetFrees { get; private set; }
    public int FreesWhileHoldingCells { get; private set; }

    /// <summary>What native AccumulateViewportCells does for the nearby body's attached arrays.</summary>
    public void AllocatePlanetResources() => HasPlanetResources = true;

    public void DisposePlanetResources()
    {
        if (!HasPlanetResources) return;
        if (_masks.Count != 0) FreesWhileHoldingCells++;
        PlanetFrees++;
        HasPlanetResources = false;
        GenerationViewport = null;
    }

    public ClutterEcotypeRenderData(int slots)
    {
        FreeSlots = slots;
        for (var i = 0; i < _loaded.Length; i++) _loaded[i] = [];
    }

    public int LoadedCells(IViewport viewport) => _loaded[GroundClutterRenderer.ResolveViewportSlot(viewport)].Count;

    public void Load(IViewport viewport, int cell)
    {
        int slot = GroundClutterRenderer.ResolveViewportSlot(viewport);
        if (!_masks.ContainsKey(cell)) { _masks[cell] = 0; FreeSlots--; }
        _masks[cell] |= 1u << slot;
        _loaded[slot].Add(cell);
        GenerationViewport = viewport;
    }

    // KSA 5919+: callers resolve the clutter slot first (native OnViewportUnregistered does too).
    public void ReleaseViewport(int slot)
    {
        var loaded = _loaded[slot];
        if (loaded.Count == 0) return;
        var bit = 1u << slot;
        foreach (var cell in loaded)
        {
            if ((_masks[cell] & bit) == 0) continue;
            _masks[cell] &= ~bit;
            if (_masks[cell] == 0) _pendingRelease.Add(cell);
        }
        loaded.Clear();
    }

    public void ResolvePendingReleases()
    {
        Resolves++;
        foreach (var cell in _pendingRelease)
            if (_masks.TryGetValue(cell, out var mask) && mask == 0) { _masks.Remove(cell); FreeSlots++; }
        _pendingRelease.Clear();
    }
}
}

internal sealed class TestViewport(int shaderSlot) : KSA.IViewport
{
    public int ShaderSlot { get; } = shaderSlot;
}
