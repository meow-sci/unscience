using System;
using System.Collections.Generic;
using Brutal.VulkanApi;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.GraffitiLib;

/// <summary>
/// Graffiti — click-to-place PNG decals. Pick a PNG from the decal library (imported through a
/// file browser or dropped into <c>.unscience/pngs</c> by hand), arm placement, click anywhere
/// in the 3D world, and a projected decal is painted onto the vehicle, parachute, or terrain under the
/// cursor. This file holds state, lifecycle, the per-frame driver and the public API; the ImGui
/// panels live in the partial files.
/// </summary>
public sealed partial class GraffitiSubmod : ISubmod
{
    public string Name => "Graffiti - PNG Decals";
    public string Tooltip => "Paint PNG decals onto vehicles, deployed parachutes, and terrain with a click.";

    public static GraffitiSubmod? Instance { get; private set; }

    private static volatile bool _renderActive;

    /// <summary>Read by the render postfix on the main thread: true while the draw path is live.</summary>
    public static bool RenderActive => _renderActive;

    private readonly List<DecalEntry> _decals = new();
    public IReadOnlyList<DecalEntry> Decals => _decals;

    private readonly DecalTextureCache _textures = new();
    private DecalRenderer? _renderer;
    private DecalEntry[] _published = Array.Empty<DecalEntry>();
    private bool _dirty;
    private bool _gpuFailed;
    private bool _drawFaultLogged;

    /// <summary>Draw the magenta decal-space checker instead of the PNG (a placement debugging aid).</summary>
    public bool DebugBox;

    public void Initialize()
    {
        Instance = this;
        PngLibrary.EnsureDir();
        RefreshLibrary();
    }

    /// <summary>
    /// Game-thread driver, once per frame BEFORE the scene renders (the ordering the anchor
    /// re-resolution needs): re-resolves anchors, recomposes the live decal matrices against this
    /// frame's camera, brings the GPU path up or down, and publishes the entry array.
    /// </summary>
    public void Update(double dt)
    {
        _textures.Drain();
        if (_decals.Count == 0 && _renderer == null && !_dirty)
        {
            _renderActive = false;
            return;
        }

        var camera = SafeMainCamera();
        var live = 0;
        foreach (var entry in _decals)
        {
            ResolveAnchor(entry);
            entry.Live = entry.TextureHandle >= 0 && camera != null
                                                 && DecalAnchors.TryCompose(entry, camera);
            if (entry.Live)
                live++;
        }

        if (live > 0 && !_gpuFailed && _renderer == null)
            EnsureGpu();

        // The pipeline tracks registry emptiness, not liveness: rebuilding it costs a device-wide
        // WaitIdle and two shaderc compiles, and a decal goes dormant on every scene switch — so
        // a dormant entry keeps it.
        if (_decals.Count == 0 && _renderer != null)
            FreeGpu();

        if (_dirty)
        {
            _published = _decals.ToArray();
            _dirty = false;
        }

        _renderActive = live > 0 && !_gpuFailed && _renderer is { IsValid: true };
    }

    public void Dispose()
    {
        _renderActive = false;
        _decals.Clear();
        _published = Array.Empty<DecalEntry>();
        FreeGpu();
        WaitIdle();
        _textures.DisposeAll();
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    // ---- render hook (called from the Harmony postfix on the main thread) ----

    /// <summary>Records the decal pass for this frame. Self-disables on the first fault.</summary>
    public void RecordPass(CommandBuffer commandBuffer)
    {
        if (_renderer is not { IsValid: true } renderer)
            return;
        try
        {
            renderer.RecordPass(commandBuffer, _published, DebugBox);
        }
        catch (Exception ex)
        {
            _renderActive = false; // bail the postfix immediately; one log, no per-frame spam
            _gpuFailed = true;
            _lastError = ex.Message;
            if (!_drawFaultLogged)
            {
                _drawFaultLogged = true;
                Console.WriteLine($"graffiti: decal draw disabled after an error: {ex.Message}");
            }
        }
    }

    // ---- public API ----

    /// <summary>
    /// Raycasts from the mouse cursor into the world and places a decal on the nearest vehicle,
    /// deployed parachute, or terrain hit within <paramref name="range"/> metres.
    /// </summary>
    /// <param name="imageName">A shared PNG-library file name (see <see cref="PngLibrary"/>).</param>
    /// <param name="range">Maximum hit distance, metres.</param>
    /// <param name="width">Decal width, metres.</param>
    /// <param name="height">Decal height, metres.</param>
    /// <param name="rollDeg">Roll relative to the "reads upright from here" default, degrees.</param>
    /// <param name="alpha">Opacity in [0, 1].</param>
    /// <param name="brightness">Lighting gain in (0, 8].</param>
    /// <param name="depth">
    /// Projection-box depth, metres; null scales with the decal (see <see cref="AutoDepth"/>).
    /// The visible decal is the surface ∩ box, so too shallow a box crops a wide decal on a
    /// curved hull to its central region (looks "zoomed in"); too deep a box projects through
    /// thin geometry and paints the far side too.
    /// </param>
    public (DecalEntry? Decal, string? Error) PlaceAtCursor(string imageName, double range,
        double width, double height, double rollDeg, double alpha, double brightness,
        double? depth = null)
    {
        var handle = _textures.Resolve(imageName, out var state);
        if (handle == null)
        {
            var detail = state == DecalTextureState.Missing
                ? "not found in the decal library"
                : $"failed to load ({_textures.LastError})";
            return (null, $"Decal image '{imageName}' {detail}.");
        }

        if (!DecalPicker.TryPick(range, out var pick))
            return (null, $"Nothing hit within {range:0} m.");

        var entry = new DecalEntry
        {
            ImageName = imageName,
            Kind = pick.Kind,
            TargetId = pick.Kind == DecalAnchorKind.Terrain ? pick.Body!.Id : pick.Vehicle!.Id,
            PartInstanceId = pick.Part?.InstanceId ?? 0,
            ParachuteInstanceId = pick.Parachute?.InstanceId ?? 0,
            ParachuteCanopyIndex = pick.ParachuteCanopyIndex,
            ClothNodeA = pick.ClothNodeA,
            ClothNodeB = pick.ClothNodeB,
            ClothNodeC = pick.ClothNodeC,
            ClothBarycentric = pick.ClothBarycentric,
            ClothNormalSign = pick.ClothNormalSign,
            Position = pick.Position,
            Normal = pick.Normal,
            // The picker's rotation is the "reads upright from here" default; the caller's roll
            // turns the decal relative to that rather than replacing it.
            RotationDeg = pick.RotationDeg + rollDeg,
            Width = width,
            Height = height,
            // The pick can demand a deeper box than the size heuristic (a KittenEva anchor is a
            // bounding-sphere point, not a mesh point — the box must reach the avatar inside).
            Depth = depth ?? Math.Max(AutoDepth(pick.Kind, width, height), pick.SuggestedMinDepth),
            Alpha = Math.Clamp(alpha, 0.0, 1.0),
            Brightness = Math.Clamp(brightness, 0.01, 8.0),
            Vehicle = pick.Vehicle,
            Part = pick.Part,
            SaveTarget = pick.Vehicle != null && pick.Part != null
                ? MeowSci.KsaAbstractions.Persistence.SavedPartReference.Capture(pick.Vehicle, pick.Part) : null,
            Parachute = pick.Parachute,
            Body = pick.Body,
            TextureHandle = handle.Value,
            TextureState = DecalTextureState.Ready,
        };
        _decals.Add(entry);
        _dirty = true;
        Console.WriteLine($"graffiti: placed decal #{entry.Id} '{imageName}' on {DescribeTarget(entry)} "
                          + $"(hit {pick.Distance:0.0} m)");
        return (entry, null);
    }

    /// <summary>Removes the given decals and frees any images nothing references any more.</summary>
    public void RemoveDecals(IReadOnlyCollection<DecalEntry> toRemove)
    {
        if (toRemove.Count == 0) return;
        foreach (var entry in toRemove)
            if (_decals.Remove(entry))
                Console.WriteLine($"graffiti: removed decal #{entry.Id}");
        _dirty = true;
        ReconcileTextures();
    }

    /// <summary>Removes every placed decal.</summary>
    public void ClearDecals()
    {
        if (_decals.Count == 0) return;
        _decals.Clear();
        _dirty = true;
        ReconcileTextures();
        Console.WriteLine("graffiti: cleared all decals");
    }

    /// <summary>
    /// Rescans the decal library folder and re-resolves every placed decal's texture (hot-swaps
    /// images whose file changed on disk).
    /// </summary>
    public void RefreshLibrary()
    {
        var selectedName = _selectedLibraryIndex >= 0 && _selectedLibraryIndex < _libraryNames.Length
            ? _libraryNames[_selectedLibraryIndex]
            : null;
        _libraryNames = PngLibrary.Scan();
        _selectedLibraryIndex = selectedName == null
            ? (_libraryNames.Length > 0 ? 0 : -1)
            : Array.FindIndex(_libraryNames,
                name => string.Equals(name, selectedName, StringComparison.OrdinalIgnoreCase));
        if (_selectedLibraryIndex < 0 && _libraryNames.Length > 0)
            _selectedLibraryIndex = 0;
        foreach (var entry in _decals)
        {
            entry.TextureHandle = _textures.Resolve(entry.ImageName, out var state) ?? -1;
            entry.TextureState = state;
        }
        ReconcileTextures();
        _dirty = true;
    }

    // ---- internal ----

    /// <summary>
    /// Re-resolves the anchor against the live system every frame — cheap, and the only way a
    /// decal survives a vehicle switch, a scene reload or a staged-away part coming back. A decal
    /// whose anchor vanished goes dormant, never pruned: a vehicle can respawn.
    /// </summary>
    private static void ResolveAnchor(DecalEntry entry)
    {
        var system = Universe.CurrentSystem;
        if (entry.Kind == DecalAnchorKind.Terrain)
        {
            entry.Body = system?.Get(entry.TargetId) as Celestial;
            return;
        }

        var vehicle = system?.Get(entry.TargetId) as Vehicle;
        entry.Vehicle = vehicle;
        entry.Part = vehicle == null ? null : FindPart(vehicle, entry.PartInstanceId);
        entry.Parachute = entry.Kind == DecalAnchorKind.Parachute && vehicle != null
            ? FindParachute(vehicle, entry.ParachuteInstanceId, entry.PartInstanceId,
                entry.ParachuteCanopyIndex)
            : null;
    }

    private static Parachute? FindParachute(Vehicle vehicle, ulong instanceId, uint partInstanceId,
        int canopyIndex)
    {
        foreach (var parachute in vehicle.Parts.Modules.Get<Parachute>())
            if (parachute.InstanceId == instanceId)
                return parachute;
        foreach (var parachute in vehicle.Parts.Modules.Get<Parachute>())
            if (parachute.Parent.InstanceId == partInstanceId && parachute.CanopyIndex == canopyIndex)
                return parachute;
        return null;
    }

    /// <summary>Finds a part or sub-part by its stable instance id; null once it is gone.</summary>
    private static Part? FindPart(Vehicle vehicle, uint instanceId)
    {
        foreach (var part in vehicle.Parts.Parts)
            if (FindSubpart(part, instanceId) is { } found) return found;
        return null;
    }

    private static Part? FindSubpart(Part part, uint instanceId)
    {
        if (part.InstanceId == instanceId) return part;
        foreach (var child in part.SubParts)
            if (FindSubpart(child, instanceId) is { } found) return found;
        return null;
    }

    private void ReconcileTextures()
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _decals)
            referenced.Add(entry.ImageName);
        _textures.Reconcile(referenced);
    }

    /// <summary>Brings the pipeline, mesh and descriptor ring up once, on the first live decal.</summary>
    private void EnsureGpu()
    {
        try
        {
            var renderer = Program.GetRenderer()
                           ?? throw new InvalidOperationException("the renderer is not running yet");
            _renderer = new DecalRenderer(renderer);
            _lastError = "";
            Console.WriteLine("graffiti: decal renderer initialized");
        }
        catch (Exception ex)
        {
            _gpuFailed = true;
            _lastError = ex.Message;
            try { _renderer?.Dispose(); } catch { /* best-effort */ }
            _renderer = null;
            Console.WriteLine($"graffiti: decal renderer init failed (feature disabled): {ex.Message}");
        }
    }

    /// <summary>
    /// Frees the pipeline, mesh and descriptor ring. The render gate is cleared first so an
    /// in-flight postfix bails before any handle goes away (and on the main thread it cannot even
    /// overlap this), then the device is drained.
    /// </summary>
    private void FreeGpu()
    {
        if (_renderer is not { } renderer)
            return;
        _renderActive = false;
        WaitIdle();
        try { renderer.Dispose(); }
        catch (Exception ex) { Console.WriteLine($"graffiti: renderer dispose failed: {ex.Message}"); }
        _renderer = null;
    }

    /// <summary>
    /// Drains the graphics queue — KSA has no deferred-destroy helper, so this is the only way to
    /// know no recorded frame still references the pipeline or the images.
    /// </summary>
    private static void WaitIdle()
    {
        try
        {
            Program.GetRenderer()?.GraphicsAndCompute?.WaitIdle();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"graffiti: teardown wait failed: {ex.Message}");
        }
    }

    private static Camera? SafeMainCamera()
    {
        try
        {
            return Program.GetMainCamera();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Default projection-box depth: half the decal's larger side, floored at 0.3 m on hulls
    /// (hull curvature falls away faster the wider the decal — a fixed shallow box crops a wide
    /// decal to its centre) and at 2 m on terrain (slack for GPU tessellation detail and any
    /// residual CPU/GPU height divergence — on open ground extra depth is visually free).
    /// </summary>
    public static double AutoDepth(DecalAnchorKind kind, double width, double height)
        => Math.Max(kind == DecalAnchorKind.Terrain ? 2.0 : 0.3, 0.5 * Math.Max(width, height));

    /// <summary>Human-readable anchor description for the list and status lines.</summary>
    internal static string DescribeTarget(DecalEntry entry)
    {
        if (entry.Kind == DecalAnchorKind.Terrain)
            return $"{entry.TargetId} ({entry.Position.X:0.00}°, {entry.Position.Y:0.00}°)";
        if (entry.Kind == DecalAnchorKind.Parachute)
            return $"{entry.TargetId}/parachute";
        var part = entry.Part != null ? $"/{entry.Part.Id}" : "";
        return $"{entry.TargetId}{part}";
    }
}
