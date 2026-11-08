using System;
using System.Collections.Generic;
using Brutal.TextureApi.Abstractions;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.RockyMcRockFaceLib;

namespace MeowSci.BloominOnionLib;

/// <summary>
/// Turns a <see cref="RingDefinition"/> into the game's XML-backed
/// <see cref="PlanetaryRingsReference"/> tree — the exact object shape
/// <c>PlanetaryRingsRenderData</c> reads at construction. Every asset is resolved up front
/// so a failed lookup produces an error message and leaves the game untouched.
/// </summary>
public sealed class RingReferenceBuilder
{
    private readonly RingAssetCatalog _catalog;
    private readonly RingMeshFactory _meshFactory;
    private readonly RingTextureFactory _textureFactory;
    private readonly StockRingAssets _stock;

    public RingReferenceBuilder(RingAssetCatalog catalog, RingMeshFactory meshFactory,
        RingTextureFactory textureFactory, StockRingAssets stock)
    {
        _catalog = catalog;
        _meshFactory = meshFactory;
        _textureFactory = textureFactory;
        _stock = stock;
    }

    /// <summary>Checks the numbers only (no asset resolution). Returns null when valid.</summary>
    public static string? Validate(RingDefinition definition, Celestial celestial)
    {
        if (definition.InnerRadiusKm <= 0) return "inner radius must be positive";
        if (definition.OuterRadiusKm <= definition.InnerRadiusKm) return "outer radius must be larger than inner radius";
        if (definition.InnerRadiusKm * 1000.0 < celestial.MeanRadius * 0.5)
            return $"inner radius is inside {celestial.Id} (radius {celestial.MeanRadius / 1000.0:F0} km)";
        if (definition.Lods.Count == 0) return "at least one LOD is required";
        if (definition.Lods.Count > RingDefinition.MaxLods) return $"the game allows at most {RingDefinition.MaxLods} LODs";
        if (definition.ObjectSizeM <= 0) return "rock size must be positive";
        if (definition.ObjectDensityPerKm3 <= 0) return "rock density must be positive";
        if (definition.ObjectRenderDistanceKm <= 0) return "rock draw distance must be positive";
        if (definition.ObjectThicknessKm <= 0) return "rock field thickness must be positive";
        if (definition.VolumeMaxThicknessKm < definition.VolumeMinThicknessKm) return "volumetric max thickness is below min thickness";
        if (definition.VolumeMaxRenderDistanceKm < definition.VolumeMinRenderDistanceKm) return "volumetric max render distance is below min";
        if (definition.StepMaxSizeKm < definition.StepMinSizeKm) return "raymarch max step is below min step";
        if (definition.StepMinSizeKm <= 0) return "raymarch min step must be positive";
        if (definition.UseEclipticFrame && celestial.Parent == null) return $"{celestial.Id} has no parent body - the ecliptic frame needs one";
        if (definition.BandSource == RingBandSource.Painted && !PaintedTextureReference.IsSupported)
            return "painted bands are unavailable in this game build - pick a texture instead";
        return null;
    }

    /// <summary>Builds the reference tree, or returns null with an error message.</summary>
    public PlanetaryRingsReference? Build(RingDefinition definition, Celestial celestial, out string error)
    {
        error = Validate(definition, celestial) ?? "";
        if (error.Length > 0) return null;

        if (!ResolveBand(definition, out var band, out var control, out error)) return null;
        if (!ResolveTexture(definition.DiffuseId, _stock.Diffuse, "rock diffuse", out var diffuse, out error)) return null;
        if (!ResolveTexture(definition.PbrId, _stock.Pbr, "rock AoRoughMetal", out var pbr, out error)) return null;
        if (!ResolveNormal(definition.NormalId, out var normal, out error)) return null;
        if (!ResolveLods(definition, out var lods, out error)) return null;

        var rings = new PlanetaryRingsReference
        {
            DefinitionFrame = definition.UseEclipticFrame ? OrbitDefinitionFrame.Ecliptic : OrbitDefinitionFrame.Equatorial,
            // The game normalizes these the same way when it loads XML (PlanetaryRingsReference.OnDataLoad).
            Inclination = new RadianReference(MathEx.ToDeviationAngle(Degrees(definition.InclinationDeg))),
            LongitudeOfAscendingNode = new RadianReference(MathEx.ToCompassAngle(Degrees(definition.LongitudeOfAscendingNodeDeg))),
            InnerRadius = Km(definition.InnerRadiusKm),
            OuterRadius = Km(definition.OuterRadiusKm),
            Texture = GameDataReference.To(band),
            ControlTexture = GameDataReference.To(control),
            DetailScale = DoubleReference.FromValue(definition.DetailScale),
            Volume = new PlanetaryRingsVolumeReference
            {
                MinThickness = Km(definition.VolumeMinThicknessKm),
                MaxThickness = Km(definition.VolumeMaxThicknessKm),
                MinRenderDistance = Km(definition.VolumeMinRenderDistanceKm),
                MaxRenderDistance = Km(definition.VolumeMaxRenderDistanceKm),
                Step = new RingRaymarchingStepReference
                {
                    Scale = definition.StepScale,
                    MinSize = Km(definition.StepMinSizeKm),
                    MaxSize = Km(definition.StepMaxSizeKm),
                },
                FadeToMeshes = new BoolReference(definition.FadeToMeshes),
            },
            RingObjects = new RingObjectsReference
            {
                Thickness = Km(definition.ObjectThicknessKm),
                Size = new DistanceReference(definition.ObjectSizeM),
                RenderDistance = Km(definition.ObjectRenderDistanceKm),
                Density = DoubleReference.FromValue(definition.ObjectDensityPerKm3),
                Lods = lods,
                MaterialReference = GameDataReference.To(new PbrMaterialReference
                {
                    DiffuseReference = GameDataReference.To(diffuse),
                    NormalReference = GameDataReference.To(normal),
                    PBRMap = GameDataReference.To(pbr),
                }),
            },
        };

        // Deliberately NOT gated on rings.IsValid(): DistanceReference.IsValid() demands > 100 km,
        // which the stock 10 m rocks / 1 km thickness / 20 km draw distance fail too — the game
        // never enforces it. Validate() above is the real check.
        return rings;
    }

    private bool ResolveBand(RingDefinition definition, out TextureReference band, out TextureReference control, out string error)
    {
        band = control = null!;
        if (definition.BandSource == RingBandSource.Painted)
        {
            var painted = _textureFactory.GetBand(definition, out var paintError);
            if (painted == null) { error = paintError ?? "band paint failed"; return false; }
            var paintedControl = _textureFactory.GetControl(definition, out paintError);
            if (paintedControl == null) { error = paintError ?? "control paint failed"; return false; }
            band = painted;
            control = paintedControl;
            error = "";
            return true;
        }

        if (!ResolveTexture(definition.BandTextureId, _stock.Band, "ring band", out var bandTexture, out error)) return false;
        if (!ResolveTexture(definition.ControlTextureId, _stock.Control, "ring control", out var controlTexture, out error)) return false;
        if (!IsCpuSampleable(controlTexture!))
        {
            error = $"control texture '{controlTexture!.Id}' is not uncompressed RGBA - the game samples it on the CPU";
            return false;
        }
        band = bandTexture!;
        control = controlTexture!;
        return true;
    }

    private bool ResolveTexture(string id, TextureReference? fallback, string slot, out TextureReference? result, out string error)
    {
        error = "";
        if (id.Length == 0)
        {
            result = fallback;
            if (result != null) return true;
            error = $"no stock {slot} texture available (load a system, then Rescan Assets)";
            return false;
        }
        if (_catalog.TryGetTexture(id, out var texture))
        {
            result = texture;
            return true;
        }
        result = null;
        error = $"unknown {slot} texture '{id}'";
        return false;
    }

    private bool ResolveNormal(string id, out TextureReference? result, out string error)
    {
        error = "";
        if (id.Length == 0)
        {
            result = _stock.Normal;
            if (result != null) return true;
            error = "no stock rock normal map available (load a system, then Rescan Assets)";
            return false;
        }
        if (_catalog.TryGetNormalTexture(id, out var normal))
        {
            result = normal;
            return true;
        }
        result = null;
        error = $"unknown normal map '{id}'";
        return false;
    }

    private bool ResolveLods(RingDefinition definition, out List<RingLodReference> lods, out string error)
    {
        error = "";
        lods = new List<RingLodReference>(definition.Lods.Count);
        for (int i = 0; i < definition.Lods.Count; i++)
        {
            var lod = definition.Lods[i];
            MeshReference? mesh;
            if (lod.MeshId.Length == 0)
            {
                mesh = _stock.LodMesh(i);
                if (mesh == null) { error = "no stock ring rock mesh available (load a system, then Rescan Assets)"; return false; }
            }
            else if (_catalog.TryGetMesh(lod.MeshId, out var source))
            {
                mesh = _meshFactory.GetRingUsable(source, out var meshError);
                if (mesh == null) { error = meshError ?? $"mesh '{lod.MeshId}' is not usable"; return false; }
            }
            else if (_catalog.TryGetGltfMesh(lod.MeshId, out var gltfEntry))
            {
                mesh = _meshFactory.GetRingUsableFromGltf(gltfEntry, out var meshError);
                if (mesh == null) { error = meshError ?? $"mesh '{lod.MeshId}' is not usable"; return false; }
            }
            else
            {
                error = $"unknown mesh '{lod.MeshId}' for LOD {i}";
                return false;
            }

            lods.Add(new RingLodReference
            {
                MinScreenSizePixels = lod.MinScreenSizePixels,
                MeshFileReference = GameDataReference.To(new MeshFileReference { Mesh = mesh }),
            });
        }
        return true;
    }

    /// <summary>The ring renderer CPU-samples the control strip assuming 4 bytes per texel.</summary>
    public static bool IsCpuSampleable(TextureReference texture)
    {
        try
        {
            var asset = texture.TextureAsset;
            if (asset?.Texture == null) return false;
            var descriptor = asset.Texture.Format.Descriptor();
            return !descriptor.IsBlockCompressed && descriptor.BlockSizeInBytes == 4;
        }
        catch
        {
            return false;
        }
    }

    private static DistanceReference Km(double kilometers) => new(kilometers, DistanceUnit.Kilometers);
    private static double Degrees(double degrees) => degrees * Math.PI / 180.0;
}
