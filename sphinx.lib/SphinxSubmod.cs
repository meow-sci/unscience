using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using Brutal.VulkanApi;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.PebblesLib;

namespace MeowSci.SphinxLib;

public sealed partial class SphinxSubmod : ISubmod
{
    public static SphinxSubmod? Instance { get; private set; }
    public string Name => "Sphinx";
    public string Tooltip => "Place imported textured GLBs as decorative statics fixed to a planet's surface.";
    private readonly ClutterAssets _assets = new();
    private readonly List<SphinxEntry> _entries = new();
    private readonly Queue<Action> _pending = new();
    private int _nextId = 1;
    private double _scanTime;
    private bool _disposed;
    private string _status = "Choose a GLB, then click the ground or place beside your vessel.";

    public void Initialize() { Instance = this; RefreshLibrary(); }
    public void Update(double dt)
    {
        if (_disposed) return;
        int count = _pending.Count;
        for (int i = 0; i < count; i++) Attempt(_pending.Dequeue());
        var bodies = CelestialProvider.GetAllCelestials();
        foreach (var entry in _entries.Where(e => !bodies.Contains(e.Anchor.Body)).ToArray()) Remove(entry);
        _scanTime -= dt;
        if (_scanTime <= 0) { _scanTime = 2; RefreshLibrary(); }
    }
    private void RefreshLibrary() { _glbs = GlbLibrary.Files.Scan(); _pngs = PngLibrary.Scan(); }
    private void Attempt(Action action)
    {
        try { action(); }
        catch (Exception ex) { _status = ex.Message; Console.WriteLine($"sphinx: {ex}"); }
    }
    private void QueuePlacement(GroundAnchor anchor)
    {
        string file = _file ?? "";
        string? png = _png;
        var mapping = _mapping;
        var scale = _scale; var rotation = _rotation; var offset = _offset; bool align = _align;
        _pending.Enqueue(() =>
        {
            if (!SphinxPatches.Ready) throw new InvalidOperationException("Sphinx's render hooks are unavailable. Check the game log.");
            if (_entries.Count >= 32) throw new InvalidOperationException("Sphinx has 32 statics. Remove one before placing another.");
            if (!CelestialProvider.GetAllCelestials().Contains(anchor.Body)) throw new InvalidOperationException("That body is no longer available.");
            string meshId = _assets.ImportGlb(GlbLibrary.Files.FullPath(file))[0].Id;
            var resource = new StaticModelResources(_assets, meshId, png, 8_000_000 - _entries.Sum(e => e.Model.VertexCount), mapping);
            try
            {
                _ = PlacementMath.GroundedLocal(resource.Min, resource.Max, SphinxEntry.Vector(scale), SphinxEntry.Vector(rotation), SphinxEntry.Vector(offset));
                var entry = new SphinxEntry { Id = _nextId++, MeshId = meshId, Png = png, Anchor = anchor, Model = resource,
                    Scale = scale, Rotation = rotation, Offset = offset, Align = align, Mapping = mapping };
                _entries.Add(entry); Select(entry);
                _status = $"Placed #{entry.Id} on {anchor.Body.Id}. " + string.Join(" ", _assets.GlbWarnings(meshId));
            }
            catch { resource.Dispose(); throw; }
        });
    }
    private void Remove(SphinxEntry entry)
    {
        // Retire before removing ownership, so a failed device wait remains retryable.
        entry.Dispose(); _entries.Remove(entry);
        if (_selectedId == entry.Id) _selectedId = 0;
    }
    private void Clear()
    {
        _armed = false;
        foreach (var entry in _entries.ToArray()) Remove(entry);
        _assets.ReleaseGlbImports();
        _status = "Removed all Sphinx statics; shared files are kept.";
    }
    internal void Prepare(IViewport viewport, int frame)
    {
        if (_disposed || Program.EditorFlag) return;
        var camera = viewport.GetCamera();
        foreach (var entry in _entries)
        {
            if (!entry.Visible || !ReferenceEquals(entry.Anchor.Body, camera.NearbyCelestial)) continue;
            try { entry.Model.Prepare(viewport, frame, entry.Matrix(camera)); }
            catch (Exception ex) { entry.Visible = false; _status = $"#{entry.Id} hidden: {ex.Message}"; Console.WriteLine($"sphinx: {ex}"); }
        }
    }
    internal void Record(CommandBuffer command, IViewport viewport, int frame, bool prepass, bool alpha)
    {
        if (_disposed || Program.EditorFlag) return;
        var camera = viewport.GetCamera();
        var entries = _entries.Where(e => e.Visible && ReferenceEquals(e.Anchor.Body, camera.NearbyCelestial));
        if (alpha) entries = entries.OrderByDescending(e =>
            (camera.GetPositionEgo(e.Anchor.Body) + e.Anchor.PositionCcf.Transform(e.Anchor.Body.GetCcf2Cce())).LengthSquared());
        foreach (var entry in entries)
            try { entry.Model.Record(command, viewport, frame, prepass, alpha); }
            catch (Exception ex) { entry.Visible = false; _status = $"#{entry.Id} hidden: {ex.Message}"; Console.WriteLine($"sphinx: {ex}"); }
    }
    public void Dispose()
    {
        _disposed = true; _pending.Clear();
        if (ReferenceEquals(Instance, this)) Instance = null;
        Clear(); _assets.Dispose();
    }
}
