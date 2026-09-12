using System;
using System.Collections.Generic;
using KSA;
using MeowSci.RockyMcRockFaceLib;

namespace MeowSci.BloominOnionLib;

/// <summary>A custom ring currently applied to a celestial body.</summary>
public sealed class AppliedRing
{
    public AppliedRing(Celestial celestial, RingDefinition definition, PlanetaryRingsReference reference)
    {
        Celestial = celestial;
        Definition = definition;
        Reference = reference;
    }

    public Celestial Celestial { get; }
    /// <summary>A private copy of the definition as it was applied.</summary>
    public RingDefinition Definition { get; }
    public PlanetaryRingsReference Reference { get; }
    public string BodyId => Celestial.Id;
}

/// <summary>
/// Owns the runtime ring state: which bodies carry a custom ring, what their template's ring
/// reference was before (null for most bodies, the stock ring for Saturn), and the asset
/// caches that back the built references. Applying swaps the template's
/// <c>RingsReference</c> and rebuilds the renderer; removing puts the original back.
/// The save adapter captures detached definitions and reconstructs this runtime ownership.
/// </summary>
public sealed class RingDefinitionController : IDisposable
{
    private readonly Dictionary<CelestialTemplate, PlanetaryRingsReference?> _originals = new();
    private readonly Dictionary<string, AppliedRing> _applied = new(StringComparer.OrdinalIgnoreCase);

    public RingAssetCatalog Catalog { get; } = new();
    public RingMeshFactory MeshFactory { get; } = new();
    public RingTextureFactory TextureFactory { get; } = new();
    public StockRingAssets Stock { get; } = new();
    public RingReferenceBuilder Builder { get; }

    public IReadOnlyCollection<AppliedRing> Applied => _applied.Values;

    public RingDefinitionController()
    {
        Builder = new RingReferenceBuilder(Catalog, MeshFactory, TextureFactory, Stock);
    }

    public void RefreshAssets()
    {
        Catalog.Refresh();
        Stock.Refresh(Catalog);
    }

    public bool TryGetApplied(Celestial celestial, out AppliedRing applied) =>
        _applied.TryGetValue(celestial.Id, out applied!);

    /// <summary>True when the body's template shipped with a ring definition (e.g. Saturn).</summary>
    public bool HasStockRings(Celestial celestial)
    {
        var template = celestial.BodyTemplate;
        if (template == null) return false;
        return _originals.TryGetValue(template, out var original)
            ? original != null
            : template.RingsReference != null;
    }

    /// <summary>
    /// Builds the definition and puts it on the body, replacing any previous custom ring there
    /// (or the stock ring, which is restored on Remove). Rebuilds the renderer on success.
    /// </summary>
    public bool Apply(Celestial celestial, RingDefinition definition, out string message)
    {
        var template = celestial.BodyTemplate;
        if (template == null)
        {
            message = $"{celestial.Id} has no body template";
            return false;
        }

        var reference = Builder.Build(definition, celestial, out message);
        if (reference == null) return false;

        if (!_originals.ContainsKey(template)) _originals[template] = template.RingsReference;
        var previous = template.RingsReference;
        template.RingsReference = reference;

        if (!RingRendererRebuilder.Rebuild(out var rebuildMessage))
        {
            template.RingsReference = previous;
            message = rebuildMessage;
            return false;
        }

        _applied[celestial.Id] = new AppliedRing(celestial, definition.Clone(), reference);
        RingRendererRebuilder.SyncDistantSphereShadow(celestial);
        PruneUnusedAssets();
        message = $"ring '{definition.Name}' applied to {celestial.Id}; {rebuildMessage}";
        return true;
    }

    /// <summary>Restores the body's original ring reference (usually none) and rebuilds.</summary>
    public bool Remove(Celestial celestial, out string message)
    {
        if (!_applied.Remove(celestial.Id))
        {
            message = $"{celestial.Id} has no custom ring";
            return false;
        }
        RestoreTemplate(celestial);
        bool rebuilt = RingRendererRebuilder.Rebuild(out var rebuildMessage);
        RingRendererRebuilder.SyncDistantSphereShadow(celestial);
        if (rebuilt) PruneUnusedAssets();
        message = rebuilt ? $"custom ring removed from {celestial.Id}" : rebuildMessage;
        return rebuilt;
    }

    /// <summary>Restores every body and rebuilds once.</summary>
    public bool RemoveAll(out string message)
    {
        if (_applied.Count == 0)
        {
            message = "no custom rings applied";
            return true;
        }
        var bodies = new List<Celestial>();
        foreach (var applied in _applied.Values) bodies.Add(applied.Celestial);
        _applied.Clear();
        foreach (var celestial in bodies) RestoreTemplate(celestial);

        bool rebuilt = RingRendererRebuilder.Rebuild(out var rebuildMessage);
        foreach (var celestial in bodies) RingRendererRebuilder.SyncDistantSphereShadow(celestial);
        if (rebuilt) PruneUnusedAssets();
        message = rebuilt ? $"removed {bodies.Count} custom ring(s)" : rebuildMessage;
        return rebuilt;
    }

    /// <summary>
    /// After a save/system reload the Celestial objects are new instances but their templates
    /// (and so our ring references) survive, and the game's fresh renderer already draws them.
    /// Re-point applied entries at the new instances; drop (and restore the template of) any
    /// whose body is gone or whose template no longer carries our reference.
    /// </summary>
    public void RebindBodies(IReadOnlyCollection<Celestial> current)
    {
        if (_applied.Count == 0) return;
        var alive = new HashSet<Celestial>(current);
        var byId = new Dictionary<string, Celestial>(StringComparer.OrdinalIgnoreCase);
        foreach (var celestial in current) byId.TryAdd(celestial.Id, celestial);

        foreach (var id in new List<string>(_applied.Keys))
        {
            var entry = _applied[id];
            if (alive.Contains(entry.Celestial)) continue;
            if (byId.TryGetValue(id, out var replacement)
                && ReferenceEquals(replacement.BodyTemplate?.RingsReference, entry.Reference))
            {
                _applied[id] = new AppliedRing(replacement, entry.Definition, entry.Reference);
                continue;
            }
            RestoreTemplate(entry.Celestial);
            _applied.Remove(id);
        }
    }

    public void Dispose()
    {
        try
        {
            if (_applied.Count > 0 && RingRendererRebuilder.IsRingsRendererCreated())
                RemoveAll(out _);
            else if (_applied.Count > 0)
            {
                foreach (var applied in new List<AppliedRing>(_applied.Values)) RestoreTemplate(applied.Celestial);
                _applied.Clear();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"bloomin-onion: restore on dispose failed: {ex.Message}");
        }
        // Only after the rebuild above: nothing references the painted textures / mesh clones.
        TextureFactory.Dispose();
        MeshFactory.Dispose();
    }

    /// <summary>Restore reused celestial templates before native reconstruction; clear old baselines.</summary>
    internal void ResetForSaveLoad()
    {
        var applied = new List<AppliedRing>(_applied.Values);
        foreach (var entry in applied) RestoreTemplate(entry.Celestial);
        if (applied.Count > 0 && !RingRendererRebuilder.Rebuild(out var error)) throw new InvalidOperationException(error);
        foreach (var entry in applied) RingRendererRebuilder.SyncDistantSphereShadow(entry.Celestial);
        _applied.Clear(); _originals.Clear();
        PruneUnusedAssets();
    }

    private void RestoreTemplate(Celestial celestial)
    {
        var template = celestial.BodyTemplate;
        if (template != null && _originals.TryGetValue(template, out var original))
            template.RingsReference = original;
    }

    /// <summary>
    /// Frees painted textures and converted meshes no longer used by any applied ring. Safe only
    /// right after a successful rebuild (the fresh render data references exactly what is applied).
    /// </summary>
    private void PruneUnusedAssets()
    {
        var textureIds = new HashSet<string>(StringComparer.Ordinal);
        var meshIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var applied in _applied.Values)
        {
            var definition = applied.Definition;
            if (definition.BandSource == RingBandSource.Painted)
            {
                textureIds.Add(RingBandPainter.BandId(definition));
                textureIds.Add(RingBandPainter.ControlId(definition));
            }
            foreach (var lod in definition.Lods)
                if (lod.MeshId.Length > 0) meshIds.Add(lod.MeshId);
        }
        TextureFactory.PruneExcept(textureIds);
        MeshFactory.PruneExcept(meshIds);
    }
}
