using System;
using System.Collections.Generic;

// Managed stand-ins for the KSA 5638 per-viewport clutter residency surface that
// pebbles.lib/Runtime/ClutterResidency.cs calls. Semantics follow ClutterEcotypeRenderData
// ReleaseViewport/ResolvePendingReleases: a slot release clears that slot's mask bit on each loaded
// cell, and a cell returns its pooled slot only once no viewport mask remains. No game source is copied.
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

public sealed class ClutterEcotypeRenderData
{
    private readonly HashSet<int>[] _loaded = new HashSet<int>[8];
    private readonly Dictionary<int, uint> _masks = [];
    private readonly List<int> _pendingRelease = [];
    public IViewport? GenerationViewport;
    public int FreeSlots { get; private set; }
    public int Resolves { get; private set; }

    public ClutterEcotypeRenderData(int slots)
    {
        FreeSlots = slots;
        for (var i = 0; i < _loaded.Length; i++) _loaded[i] = [];
    }

    public int LoadedCells(int shaderSlot) => _loaded[shaderSlot].Count;

    public void Load(IViewport viewport, int cell)
    {
        if (!_masks.ContainsKey(cell)) { _masks[cell] = 0; FreeSlots--; }
        _masks[cell] |= 1u << viewport.ShaderSlot;
        _loaded[viewport.ShaderSlot].Add(cell);
        GenerationViewport = viewport;
    }

    public void ReleaseViewport(IViewport viewport)
    {
        var loaded = _loaded[viewport.ShaderSlot];
        if (loaded.Count == 0) return;
        var bit = 1u << viewport.ShaderSlot;
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
