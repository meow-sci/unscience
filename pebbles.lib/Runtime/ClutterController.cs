using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Brutal.VulkanApi;
using HarmonyLib;
using KSA;

namespace MeowSci.PebblesLib;

public sealed class ClutterLiveRecord
{
    public string BodyId { get; internal set; } = "";
    public PebblesRecipe Recipe { get; internal set; } = new();
    public string Status { get; internal set; } = "Waiting";
    public long VertexCount => Resources?.Graph.Geometry.VertexCount ?? 0;
    public int MaterialCount => Resources?.Graph.Materials.Count ?? 0;
    public int EcotypeCount => Recipe.Ecotypes.Count;
    internal Celestial Body = null!;
    internal GroundClutterRenderer? Owner;
    internal CelestialTemplate? OriginalTemplate;
    internal CelestialTemplate? OwnedTemplate;
    internal GroundClutterPlacementData[] OriginalPlacement = [];
    internal ClutterEcotypeRenderData[] OriginalRender = [];
    internal ClutterEcotypePhysicalData[] OriginalPhysical = [];
    internal float OriginalRadius;
    internal readonly Dictionary<string, Dictionary<CubeCellGrid.Cell, GroundClutterRenderer.ExclusionData>> Exclusions = new(StringComparer.Ordinal);
    internal ClutterResources? Resources;
}

/// <summary>Queued, per-body transactions. All mutations happen after CPU solvers and GPU work complete.</summary>
public sealed class ClutterController : IDisposable
{
    private readonly ClutterAssets _assets;
    private readonly Dictionary<string, Celestial> _bodies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClutterLiveRecord> _live = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PebblesRecipe?> _pending = new(StringComparer.Ordinal);
    private readonly List<ClutterResources> _failedRetirements = [];
    private readonly List<string> _faults = [];
    public IReadOnlyList<string> Faults => _faults;
    private CelestialSystem? _system;
    private bool _processing;
    public string[] BodyIds { get; private set; } = [];
    public IReadOnlyCollection<ClutterLiveRecord> Live => _live.Values;
    public bool NeedsHooks => _live.Count != 0 || _pending.Count != 0;
    public string Status { get; private set; } = "Ready";
    public ClutterController(ClutterAssets assets) => _assets = assets;
    internal static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);

    public void Refresh()
    {
        _bodies.Clear();
        var system = Universe.CurrentSystem;
        if (system != null)
            foreach (var body in system.All.OfType<Celestial>())
                if (body.BodyTemplate.GroundClutterReference != null) _bodies.Add(body.Id, body);
        BodyIds = _bodies.Keys.Order(StringComparer.Ordinal).ToArray();
        _system ??= system;
    }
    public string[] BiomeIds(string bodyId) => _bodies.TryGetValue(bodyId, out var body)
        ? body.BodyTemplate.BiomesReference?.Biomes.Where(b => b.Id is >= 0 and < 32).Select(b => b.Alias).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() ?? [] : [];

    public PebblesRecipe CaptureOriginal(string bodyId)
    {
        Refresh();
        if (_live.TryGetValue(bodyId, out var live) && live.OriginalTemplate != null) return ClutterCapture.Capture(live.Body, live.OriginalTemplate.GroundClutterReference!);
        if (!_bodies.TryGetValue(bodyId, out var body)) throw new InvalidOperationException("Unresolved celestial.");
        return ClutterCapture.Capture(body, body.BodyTemplate.GroundClutterReference!);
    }

    public PebblesRecipe Capture(string bodyId)
    {
        Refresh();
        if (_live.TryGetValue(bodyId, out var live)) return RecipeCopy.Clone(live.Recipe);
        if (!_bodies.TryGetValue(bodyId, out var body)) throw new InvalidOperationException($"Celestial '{bodyId}' has no available clutter.");
        return ClutterCapture.Capture(body, body.BodyTemplate.GroundClutterReference!);
    }
    public void QueueApply(string bodyId, PebblesRecipe recipe)
    {
        RecipeValidation.Validate(recipe);
        Refresh();
        if (!_bodies.ContainsKey(bodyId)) throw new InvalidOperationException($"Celestial '{bodyId}' is unresolved.");
        _pending[bodyId] = RecipeCopy.Clone(recipe); Status = "Waiting for the next safe frame";
    }
    public void RestoreEcotype(string bodyId, string ecotypeName)
    {
        if (!_live.TryGetValue(bodyId, out var live) || live.OriginalTemplate == null) throw new InvalidOperationException("No applied body override.");
        var recipe = RecipeCopy.Clone(live.Recipe);
        var original = ClutterCapture.Capture(live.Body, live.OriginalTemplate.GroundClutterReference!);
        var stock = original.Ecotypes.Single(e => e.Name == ecotypeName);
        var index = recipe.Ecotypes.FindIndex(e => e.Name == ecotypeName);
        if (index >= 0) recipe.Ecotypes[index] = stock; else recipe.Ecotypes.Add(stock);
        QueueApply(bodyId, recipe);
    }
    public void QueueRestore(string bodyId) { _pending[bodyId] = null; Status = "Restore queued"; }
    public void ApplyPatches(Harmony harmony) => ClutterHooks.Apply(harmony, this);
    public void RemovePatches(Harmony harmony) => ClutterHooks.Remove(harmony, this);
    public void Update()
    {
        if (_system != null && !ReferenceEquals(_system, Universe.CurrentSystem))
        {
            Dispose(); _system = Universe.CurrentSystem; Refresh();
        }
        var renderer = ActiveRenderer();
        foreach (var live in _live.Values)
            if (live.Owner == null && renderer != null && !_pending.ContainsKey(live.BodyId)) _pending[live.BodyId] = RecipeCopy.Clone(live.Recipe);
    }

    internal void Process()
    {
        if (_processing || _pending.Count == 0) return;
        _processing = true;
        try
        {
            Quiesce();
            foreach (var request in _pending.ToArray())
            {
                if (request.Value != null && ActiveRenderer() == null) continue;
                _pending.Remove(request.Key);
                try { if (request.Value == null) Restore(request.Key); else Apply(request.Key, request.Value); }
                catch (Exception ex)
                {
                    Status = $"{request.Key}: {ex.GetBaseException().Message}";
                    Console.WriteLine($"pebbles: {ex}");
                    if (_live.TryGetValue(request.Key, out var old)) old.Status = "Previous override retained. " + Status;
                }
            }
        }
        finally { _processing = false; }
    }

    private void Apply(string id, PebblesRecipe recipe)
    {
        Refresh();
        if (!_bodies.TryGetValue(id, out var body)) throw new InvalidOperationException("Target celestial disappeared.");
        var owner = ActiveRenderer() ?? throw new InvalidOperationException("Ground clutter is disabled.");
        _live.TryGetValue(id, out var old);
        if (old?.Owner != null) CheckOwnership(old);
        var record = old ?? new ClutterLiveRecord { BodyId = id, Body = body };
        var originalTemplate = record.Owner == null ? body.BodyTemplate : record.OriginalTemplate!;
        if (!ReferenceEquals(record.Body, body)) throw new InvalidOperationException("Target belongs to a different universe generation.");
        var next = new ClutterResources(owner);
        try { next.Build(body, originalTemplate.GroundClutterReference!, recipe, _assets); }
        catch (Exception ex)
        {
            Retire(next, "Failed preparation");
            if (!_failedRetirements.Contains(next)) _failedRetirements.Add(next);
            _faults.Add("Native preparation failed; any constructor-local allocations may require renderer restart: " + ex.GetBaseException().Message);
            throw;
        }
        var clone = (CelestialTemplate)AccessTools.Method(typeof(object), "MemberwiseClone").Invoke(originalTemplate, null)!;
        clone.GroundClutterReference = next.Graph.Reference;
        try
        {
            DrainExclusions(body, owner);
            var before = owner.PlanetPlacementData[body.Hash];
            var sourceGraph = body.BodyTemplate.GroundClutterReference!;
            RememberMasks(record, sourceGraph, before);
            ReplayMasks(record, next.Graph.Reference, next.Placement, next.Render, next.Physical);
            ClearStatics(body);
            if (record.Owner == null)
            {
                record.OriginalTemplate = originalTemplate;
                record.OriginalPlacement = before; record.OriginalRender = owner.PlanetEcotypeRenderData[body.Hash]; record.OriginalPhysical = owner.PlanetPhysicalData[body.Hash];
                record.OriginalRadius = Bounds(owner).GetValueOrDefault(body.Hash);
            }
            SetTemplate(body, clone);
            owner.PlanetPlacementData[body.Hash] = next.Placement; owner.PlanetEcotypeRenderData[body.Hash] = next.Render; owner.PlanetPhysicalData[body.Hash] = next.Physical;
            Bounds(owner)[body.Hash] = next.MaximumRadius;
            var outgoing = record.Resources;
            record.Owner = owner; record.Resources = next; record.OwnedTemplate = clone; record.Recipe = RecipeCopy.Clone(recipe); record.Status = "Applied; cells generate as the game updates";
            _live[id] = record; ClutterHooks.Track(next);
            if (outgoing != null) { ClutterHooks.Forget(outgoing); Retire(outgoing, "Replaced resources"); }
            Status = $"Applied to {id}";
        }
        catch
        {
            // A committed bundle remains owned even if retiring its predecessor reports an error.
            if (!ReferenceEquals(record.Resources, next)) Retire(next, "Uncommitted resources");
            throw;
        }
    }

    private void Restore(string id)
    {
        if (!_live.TryGetValue(id, out var record)) { Status = "No applied override"; return; }
        if (record.Owner == null) { _live.Remove(id); return; }
        CheckOwnership(record);
        RestoreState(record);
        _live.Remove(id); Status = $"Restored {id}";
    }

    private void RestoreState(ClutterLiveRecord record)
    {
        var owner = record.Owner!; var body = record.Body; var resources = record.Resources!;
        DrainExclusions(body, owner);
        RememberMasks(record, resources.Graph.Reference, resources.Placement);
        ReplayMasks(record, record.OriginalTemplate!.GroundClutterReference!, record.OriginalPlacement, record.OriginalRender, record.OriginalPhysical);
        ClearStatics(body);
        SetTemplate(body, record.OriginalTemplate!);
        owner.PlanetPlacementData[body.Hash] = record.OriginalPlacement; owner.PlanetEcotypeRenderData[body.Hash] = record.OriginalRender; owner.PlanetPhysicalData[body.Hash] = record.OriginalPhysical;
        Bounds(owner)[body.Hash] = record.OriginalRadius;
        foreach (var physical in record.OriginalPhysical) physical.InvalidateGeneratedClutter();
        ClutterHooks.Forget(resources);
        record.Resources = null; record.Owner = null; record.OwnedTemplate = null;
        Retire(resources, "Restored resources");
    }

    private void Retire(ClutterResources resources, string operation)
    {
        try { resources.Dispose(); }
        catch (Exception ex)
        {
            _failedRetirements.Add(resources);
            var fault = operation + ": native retirement incomplete; restart the renderer. " + ex.GetBaseException().Message;
            _faults.Add(fault); Console.WriteLine("pebbles: " + fault); Status = fault;
        }
    }

    internal void RebuildOriginals(GroundClutterRenderer owner, Brutal.VulkanApi.Abstractions.DeviceEx device)
    {
        foreach (var record in _live.Values.Where(r => ReferenceEquals(r.Owner, owner)))
            foreach (var render in record.OriginalRender) render.RebuildFrameResources(device);
    }
    private static GroundClutterRenderer? ActiveRenderer()
    {
        var planet = Program.GetPlanetRenderer();
        return planet != null && (bool)Field(typeof(PlanetRenderer), "_groundClutterRendererCreated").GetValue(planet)! ? planet.GroundClutterRenderer : null;
    }

    internal void RendererDisposing(GroundClutterRenderer owner)
    {
        var affected = _live.Values.Where(r => ReferenceEquals(r.Owner, owner)).ToArray();
        if (affected.Length == 0) return;
        Quiesce();
        foreach (var record in affected)
        {
            CheckOwnership(record); RestoreState(record);
            record.Status = "Suspended during renderer recreation";
        }
    }
    internal void Report(Exception ex) { Status = ex.Message; Console.WriteLine($"pebbles: {ex}"); }
    public void Release()
    {
        _pending.Clear();
        foreach (var id in _live.Keys) _pending[id] = null;
        Status = "Release queued for the next safe frame";
    }
    /// <summary>Synchronously retires old-world borrowers before native save deserialization.</summary>
    internal void ResetForSaveLoad()
    {
        Dispose();
        _system = null; _bodies.Clear(); BodyIds = [];
        if (_faults.Count != 0) throw new InvalidOperationException(string.Join("; ", _faults));
    }
    /// <summary>Replays at the host's idle post-deserialization boundary, with observable failures.</summary>
    internal void ApplyForSaveLoad(string bodyId, PebblesRecipe recipe)
    {
        RecipeValidation.Validate(recipe); Quiesce(); Apply(bodyId, recipe);
    }
    public void Dispose()
    {
        _pending.Clear();
        if (_live.Count == 0) return;
        Quiesce();
        foreach (var id in _live.Keys.ToArray()) Restore(id);
    }

    private static void CheckOwnership(ClutterLiveRecord r)
    {
        if (!ReferenceEquals(r.Body.BodyTemplate, r.OwnedTemplate) || !ReferenceEquals(r.Owner!.PlanetEcotypeRenderData.GetValueOrDefault(r.Body.Hash), r.Resources!.Render)
            || !ReferenceEquals(r.Owner.PlanetPhysicalData.GetValueOrDefault(r.Body.Hash), r.Resources.Physical) || !ReferenceEquals(r.Owner.PlanetPlacementData.GetValueOrDefault(r.Body.Hash), r.Resources.Placement))
            throw new InvalidOperationException("Clutter ownership changed externally; refusing to overwrite newer game state.");
    }
    private static void SetTemplate(Celestial body, CelestialTemplate template)
    {
        Field(typeof(Celestial), "<BodyTemplate>k__BackingField").SetValue(body, template);
        Field(typeof(Astronomical), "bodyTemplate").SetValue(body, template);
    }
    private static Dictionary<KeyHash, float> Bounds(GroundClutterRenderer renderer) => (Dictionary<KeyHash, float>)Field(typeof(GroundClutterRenderer), "_planetClutterMaxBoundingRadius").GetValue(renderer)!;
    private static PhysicsBubble[] Bubbles() => ((VehicleUpdateTask)Field(typeof(Universe), "_vehicleUpdateTask").GetValue(null)!).SyncWindowBubbles.ToArray();
    private static void Quiesce()
    {
        JobSystems.VehicleSolver.Wait(); JobSystems.ClothSolvers.Wait();
        Program.GetRenderer()?.Device.WaitIdle();
        _ = Bubbles(); // The game asserts the vehicle task is idle.
    }
    private static void ClearStatics(Celestial body)
    { foreach (var bubble in Bubbles()) if (ReferenceEquals(bubble.Parent, body)) bubble.GroundClutterStatics.Clear(bubble.ConstraintSim); }
    private static void DrainExclusions(Celestial body, GroundClutterRenderer renderer)
    {
        var bubbles = Bubbles(); var events = new List<BubbleClutterStatics.ClutterInstanceKey>();
        foreach (var bubble in bubbles) if (ReferenceEquals(bubble.Parent, body)) bubble.PopulatePendingExclusions(events);
        foreach (var e in events)
        {
            renderer.ExcludeInstance(e.CelestialHash, (uint)e.EcotypeIndex, e.Cell, e.SubCellId);
            foreach (var bubble in bubbles) bubble.RemoveExcludedClutterInstance(in e);
        }
    }
    private static string GridKey(ClutterEcotypeReference ecotype) => ecotype.Name + "|" + ecotype.Placement.ObjectSeparation.InMeters().ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    private static Dictionary<CubeCellGrid.Cell, GroundClutterRenderer.ExclusionData> Masks(GroundClutterPlacementData placement)
        => (Dictionary<CubeCellGrid.Cell, GroundClutterRenderer.ExclusionData>)Field(typeof(GroundClutterPlacementData), "_exclusionCache").GetValue(placement)!;
    private static void RememberMasks(ClutterLiveRecord record, GroundClutterReference graph, GroundClutterPlacementData[] placement)
    {
        for (var i = 0; i < placement.Length; i++)
        {
            var key = GridKey(graph.Ecotypes[i]);
            if (!record.Exclusions.TryGetValue(key, out var saved)) record.Exclusions[key] = saved = [];
            foreach (var pair in Masks(placement[i]))
            {
                var mask = saved.GetValueOrDefault(pair.Key, GroundClutterRenderer.ExclusionData.AllIncluded);
                for (var word = 0; word < 8; word++) mask[word] &= pair.Value[word];
                saved[pair.Key] = mask;
            }
        }
    }
    private static void ReplayMasks(ClutterLiveRecord record, GroundClutterReference graph, GroundClutterPlacementData[] placement,
        ClutterEcotypeRenderData[] render, ClutterEcotypePhysicalData[] physics)
    {
        for (var i = 0; i < placement.Length; i++)
        {
            if (!record.Exclusions.TryGetValue(GridKey(graph.Ecotypes[i]), out var saved)) continue;
            var destination = Masks(placement[i]);
            foreach (var pair in saved)
            {
                var mask = destination.GetValueOrDefault(pair.Key, GroundClutterRenderer.ExclusionData.AllIncluded);
                for (var word = 0; word < 8; word++) mask[word] &= pair.Value[word];
                destination[pair.Key] = mask; render[i].QueueExclusionUpload(pair.Key); physics[i].QueueExclusionUpload(pair.Key);
            }
        }
    }
}
