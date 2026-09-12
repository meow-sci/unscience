using System;
using System.Collections.Generic;
using System.Linq;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.BloominOnionLib;

/// <summary>
/// Bloomin' Onion — define brand-new planetary rings at runtime and put them on any celestial
/// body. Every parameter KSA's ring XML exposes is editable (geometry, painted or picked band
/// texture, volumetric dust, instanced rock field and its material), definitions are saved as
/// presets, and applying rebuilds the renderer so the ring appears immediately.
/// </summary>
public sealed partial class BloominOnionSubmod : ISubmod
{
    public string Name => "Bloomin' Onion - Ring Builder";

    public string Tooltip =>
        "Define new planetary rings at runtime and apply them to any celestial body.\n" +
        "Paint the ring band (stripes, gaps, ringlet noise) or pick a texture, set the\n" +
        "geometry, volumetric dust and rock field, then Apply. Definitions save as presets;\n" +
        "body assignments persist with KSA saves. Applying rebuilds the renderer (brief hitch).";

    private const double RefreshIntervalSeconds = 2.0;

    private readonly RingDefinitionController _controller = new();
    private readonly RingPresetStore _presets = new();
    private readonly List<Celestial> _bodies = new();

    private RingDefinition _editor = RingDefinition.CreateDefault();
    private string[] _controlTextureIds = Array.Empty<string>();
    private bool _catalogReady;
    private double _refreshTimer;

    /// <summary>The controller, for other mods / RPC hosts that want to drive rings programmatically.</summary>
    public RingDefinitionController Controller => _controller;
    public RingPresetStore Presets => _presets;

    public void Initialize()
    {
        _presets.Initialize();
        Console.WriteLine("bloomin-onion: initialized");
    }

    public void Update(double dt)
    {
        _refreshTimer -= dt;
        if (_refreshTimer > 0) return;
        _refreshTimer = RefreshIntervalSeconds;
        try
        {
            RefreshBodies();
            if (!_catalogReady)
            {
                _controller.Catalog.Refresh();
                _catalogReady = _controller.Catalog.MeshIds.Length > 0 && _controller.Catalog.TextureIds.Length > 0;
                if (_catalogReady) RefreshControlTextureIds();
            }
            if (_catalogReady && !_controller.Stock.IsComplete)
                _controller.Stock.Refresh(_controller.Catalog);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"bloomin-onion: update failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        try { _controller.Dispose(); }
        catch (Exception ex) { Console.WriteLine($"bloomin-onion: dispose failed: {ex.Message}"); }
    }

    /// <summary>Full asset rescan (catalog, stock fallbacks, control-texture candidates).</summary>
    public void RescanAssets()
    {
        _controller.RefreshAssets();
        _catalogReady = _controller.Catalog.MeshIds.Length > 0 && _controller.Catalog.TextureIds.Length > 0;
        RefreshControlTextureIds();
        RefreshBodies();
    }

    private void RefreshBodies()
    {
        var current = CelestialProvider.GetAllCelestials();
        _bodies.Clear();
        _bodies.AddRange(current.OrderBy(c => c.Id, StringComparer.OrdinalIgnoreCase));
        _controller.RebindBodies(current);
    }

    /// <summary>Only uncompressed 4-byte-per-texel textures may be used as a control strip (CPU sampled).</summary>
    private void RefreshControlTextureIds()
    {
        var ids = new List<string>();
        foreach (var id in _controller.Catalog.TextureIds)
        {
            if (_controller.Catalog.TryGetTexture(id, out var texture) && RingReferenceBuilder.IsCpuSampleable(texture))
                ids.Add(id);
        }
        _controlTextureIds = ids.ToArray();
    }
}
