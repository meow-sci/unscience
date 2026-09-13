using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.VulkanApi;
using KSA;
using KSA.Rendering.Rings.Rendering;
using MeowSci.KsaAbstractions;

namespace MeowSci.RockyMcRockFaceLib;

/// <summary>A celestial body that declares planetary rings in its template.</summary>
public sealed class RingedBody
{
    public RingedBody(Celestial celestial, PlanetaryRingsReference rings)
    {
        Celestial = celestial;
        Rings = rings;
    }

    public Celestial Celestial { get; }
    public PlanetaryRingsReference Rings { get; }
    public string Id => Celestial.Id;
    public int LodCount => Math.Min(Rings.RingObjects.NumLods, RingSelection.MaxLods);
}

/// <summary>
/// Applies ring overrides by mutating the public XML-backed PlanetaryRingsReference
/// tree that PlanetaryRingsRenderData reads at construction, then forcing the game's
/// own renderer rebuild path (the same one its graphics settings use) so the per-planet
/// ring render data is rebuilt from the mutated references. Original values are
/// snapshotted per rings-reference so everything can be restored.
/// </summary>
public sealed class RingSwapController : IDisposable
{
    private sealed class Snapshot
    {
        public MeshReference?[] LodMeshes = Array.Empty<MeshReference?>();
        public TextureReference? Diffuse;
        public TexturePowerReference? Normal;
        public TextureReference? Pbr;
        public TextureReference? BandTexture;
        public DistanceReference Size = null!;
        public DoubleReference Density = null!;
        public DistanceReference RenderDistance = null!;
        public DistanceReference Thickness = null!;
    }

    private readonly Dictionary<PlanetaryRingsReference, Snapshot> _snapshots = new();
    private bool _anyApplied;
    private readonly Dictionary<PlanetaryRingsReference, (RingedBody Body, RingSelection Selection)> _appliedSelections = new();
    internal IEnumerable<(RingedBody Body, RingSelection Selection)> AppliedSelections => _appliedSelections.Values;

    public RingAssetCatalog Catalog { get; } = new();
    public RingMeshFactory MeshFactory { get; } = new();
    public List<RingedBody> Bodies { get; } = new();

    /// <summary>Rescans the current system for celestials with ring definitions.</summary>
    public void RefreshBodies()
    {
        Bodies.Clear();
        var system = Universe.CurrentSystem;
        if (system == null) return;

        foreach (var celestial in system.All.OfType<Celestial>())
        {
            var rings = celestial.BodyTemplate?.RingsReference;
            if (rings?.RingObjects == null || rings.RingObjects.NumLods == 0) continue;
            Bodies.Add(new RingedBody(celestial, rings));
            if (!_snapshots.ContainsKey(rings))
                _snapshots[rings] = TakeSnapshot(rings);
        }
    }

    /// <summary>
    /// Resolves and applies the selection onto the body's ring references. Does not
    /// rebuild the renderer — call <see cref="RebuildRenderer"/> afterwards.
    /// Returns false (with nothing mutated) if any referenced asset cannot be resolved.
    /// </summary>
    public bool Apply(RingedBody body, RingSelection selection, out string message)
    {
        var rings = body.Rings;
        if (!_snapshots.TryGetValue(rings, out var defaults))
        {
            message = "no default snapshot for this body (rescan first)";
            return false;
        }

        var objects = rings.RingObjects;
        int lodCount = body.LodCount;

        // Resolve everything up-front so a failed lookup leaves the game untouched.
        var meshes = new MeshReference?[lodCount];
        for (int i = 0; i < lodCount; i++)
        {
            string id = selection.LodMeshIds[i];
            if (id.Length == 0)
            {
                meshes[i] = defaults.LodMeshes[i];
                continue;
            }
            string? error;
            if (Catalog.TryGetMesh(id, out var source))
                meshes[i] = MeshFactory.GetRingUsable(source, out error);
            else if (Catalog.TryGetGltfMesh(id, out var gltfEntry))
                meshes[i] = MeshFactory.GetRingUsableFromGltf(gltfEntry, out error);
            else
            {
                message = $"unknown mesh '{id}'";
                return false;
            }
            if (meshes[i] == null)
            {
                message = error ?? $"mesh '{id}' is not usable";
                return false;
            }
        }

        if (!ResolveTexture(selection.DiffuseId, defaults.Diffuse, out var diffuse, out message)) return false;
        if (!ResolveTexture(selection.PbrId, defaults.Pbr, out var pbr, out message)) return false;
        if (!ResolveTexture(selection.BandTextureId, defaults.BandTexture, out var band, out message)) return false;

        TexturePowerReference? normal = defaults.Normal;
        if (selection.NormalId.Length > 0 && !Catalog.TryGetNormalTexture(selection.NormalId, out normal!))
        {
            message = $"unknown normal map '{selection.NormalId}'";
            return false;
        }

        for (int i = 0; i < lodCount; i++)
        {
            var fileReference = objects.Lods[i].MeshFileReference?.Get();
            if (fileReference != null) fileReference.Mesh = meshes[i];
        }
        objects.MaterialReference.DiffuseReference = diffuse;
        objects.MaterialReference.NormalReference = normal;
        objects.MaterialReference.PBRMap = pbr;
        if (band != null) rings.Texture = band;

        if (selection.OverrideFieldSettings)
        {
            objects.Size = new DistanceReference(selection.SizeM);
            objects.Density = DoubleReference.FromValue(selection.DensityPerKm3);
            objects.RenderDistance = new DistanceReference(selection.RenderDistanceKm, DistanceUnit.Kilometers);
            objects.Thickness = new DistanceReference(selection.ThicknessKm, DistanceUnit.Kilometers);
        }
        else
        {
            objects.Size = defaults.Size;
            objects.Density = defaults.Density;
            objects.RenderDistance = defaults.RenderDistance;
            objects.Thickness = defaults.Thickness;
        }

        _anyApplied = true;
        _appliedSelections[rings] = (body, selection.Clone());
        message = $"applied ring overrides to {body.Id}";
        return true;
    }

    /// <summary>Puts the game's original meshes, textures and field settings back.</summary>
    public void Restore(RingedBody body)
    {
        if (!_snapshots.TryGetValue(body.Rings, out var defaults)) return;
        var objects = body.Rings.RingObjects;
        int lodCount = Math.Min(body.LodCount, defaults.LodMeshes.Length);
        for (int i = 0; i < lodCount; i++)
        {
            var fileReference = objects.Lods[i].MeshFileReference?.Get();
            if (fileReference != null) fileReference.Mesh = defaults.LodMeshes[i];
        }
        objects.MaterialReference.DiffuseReference = defaults.Diffuse;
        objects.MaterialReference.NormalReference = defaults.Normal;
        objects.MaterialReference.PBRMap = defaults.Pbr;
        if (defaults.BandTexture != null) body.Rings.Texture = defaults.BandTexture;
        objects.Size = defaults.Size;
        objects.Density = defaults.Density;
        objects.RenderDistance = defaults.RenderDistance;
        objects.Thickness = defaults.Thickness;
        _appliedSelections.Remove(body.Rings);
    }

    internal void ResetForSaveLoad()
    {
        var applied = _appliedSelections.Values.ToArray();
        foreach (var entry in applied) Restore(entry.Body);
        if (applied.Length > 0 && IsRingsRendererCreated() && !RebuildRenderer(out var error))
        {
            foreach (var entry in applied) _appliedSelections[entry.Body.Rings] = entry;
            throw new InvalidOperationException(error);
        }
        _snapshots.Clear(); Bodies.Clear(); _appliedSelections.Clear(); _anyApplied = false;
        // A rebuilt renderer no longer borrows any private overlay meshes.
        if (applied.Length > 0) MeshFactory.PruneExcept(new HashSet<string>(StringComparer.Ordinal));
    }

    /// <summary>
    /// Forces the ring render data — meshes, index counts, descriptor sets, instance
    /// buffers — to be reconstructed from the current reference state.
    ///
    /// Program.RebuildRenderer alone is NOT enough: when the rings renderer already
    /// exists, PlanetTransparenciesRenderer.RebuildFrameResources only rebuilds its
    /// frame resources (pipelines/images), and PopulatePlanets — the only place
    /// PlanetaryRingsRenderData re-reads the ring reference tree — runs solely in the
    /// rings renderer's constructor. So the existing rings renderer is disposed first
    /// (after a device wait), which makes the game's rebuild take its own
    /// CreateRingsRenderer branch and rebuild everything from the mutated references.
    /// </summary>
    public bool RebuildRenderer(out string message)
    {
        try
        {
            var program = Program.Instance;
            if (program == null)
            {
                message = "game renderer not ready";
                return false;
            }
            DisposeRingsRendererForRecreation();
            program.RebuildRenderer();
            message = "renderer rebuilt";
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"rocky-mcrock-face: renderer rebuild failed: {ex}");
            message = $"renderer rebuild failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>True while the game's planetary rings renderer exists.</summary>
    public bool IsRingsRendererCreated()
    {
        var transparencies = ReflectionHelpers.GetFieldValue(Program.Instance, "_planetTransparenciesRenderer");
        return ReflectionHelpers.GetFieldValue(transparencies, "_ringRendererCreated") is true;
    }

    private static void DisposeRingsRendererForRecreation()
    {
        var transparencies = ReflectionHelpers.GetFieldValue(Program.Instance, "_planetTransparenciesRenderer");
        if (transparencies == null) return;
        if (ReflectionHelpers.GetFieldValue(transparencies, "_ringRendererCreated") is not true) return;
        if (ReflectionHelpers.GetFieldValue(transparencies, "_ringsRenderer") is not PlanetaryRingsRenderer ringsRenderer)
            return;
        // In-flight frames may still reference ring pipelines/buffers.
        Program.GetRenderer().Device.WaitIdle();
        ringsRenderer.Dispose();
        ReflectionHelpers.SetFieldValue(transparencies, "_ringRendererCreated", false);
    }

    public void Dispose()
    {
        try
        {
            if (_anyApplied)
            {
                foreach (var body in Bodies) Restore(body);
                if (IsRingsRendererCreated()) RebuildRenderer(out _);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"rocky-mcrock-face: restore on dispose failed: {ex.Message}");
        }
        // Only after the rebuild above: nothing references the converted meshes any more.
        MeshFactory.Dispose();
    }

    private bool ResolveTexture(string id, TextureReference? fallback, out TextureReference? result, out string message)
    {
        message = "";
        if (id.Length == 0)
        {
            result = fallback;
            return true;
        }
        if (Catalog.TryGetTexture(id, out var texture))
        {
            result = texture;
            return true;
        }
        result = null;
        message = $"unknown texture '{id}'";
        return false;
    }

    private static Snapshot TakeSnapshot(PlanetaryRingsReference rings)
    {
        var objects = rings.RingObjects;
        var lodMeshes = new MeshReference?[objects.NumLods];
        for (int i = 0; i < objects.NumLods; i++)
            lodMeshes[i] = objects.Lods[i].MeshFileReference?.Get().Mesh;

        return new Snapshot
        {
            LodMeshes = lodMeshes,
            Diffuse = objects.MaterialReference.DiffuseReference,
            Normal = objects.MaterialReference.NormalReference,
            Pbr = objects.MaterialReference.PBRMap,
            BandTexture = rings.Texture,
            Size = objects.Size,
            Density = objects.Density,
            RenderDistance = objects.RenderDistance,
            Thickness = objects.Thickness,
        };
    }
}
