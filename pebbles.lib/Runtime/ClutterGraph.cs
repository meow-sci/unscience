using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Brutal.Numerics;
using KSA;

namespace MeowSci.PebblesLib;

/// <summary>Entirely private mutable reference graph; borrowed stock assets are read-only.</summary>
internal sealed class ClutterGraph : IDisposable
{
    private static readonly MethodInfo BuildIndirection = typeof(GroundClutterLodReference).GetMethod("BuildMaterialIndirection", BindingFlags.Instance | BindingFlags.NonPublic)!;
    public GroundClutterReference Reference { get; } = new();
    public ClutterGeometry Geometry { get; } = new();
    public List<GroundClutterMaterialReference> Materials { get; } = [];
    public HashSet<KeyHash> SourceColorMaterials { get; } = [];
    public float[][] PhysicalObjectRadii { get; private set; } = [];
    public double[] PhysicalRadii { get; private set; } = [];

    public void Build(Celestial body, GroundClutterReference baseline, PebblesRecipe recipe, ClutterAssets assets)
    {
        var recipes = recipe.Ecotypes.ToDictionary(e => e.Name, StringComparer.Ordinal);
        if (recipes.Keys.Any(name => baseline.Ecotypes.All(e => e.Name != name))) throw new InvalidOperationException("An ecotype target is unresolved.");
        var stock = ClutterCapture.Capture(body, baseline);
        var stockBackfaceNormals = StockBackfaceNormals(baseline);
        PhysicalRadii = new double[baseline.Ecotypes.Count];
        PhysicalObjectRadii = new float[baseline.Ecotypes.Count][];
        long candidates = 0;
        for (var ei = 0; ei < baseline.Ecotypes.Count; ei++)
        {
            var original = baseline.Ecotypes[ei];
            PhysicalObjectRadii[ei] = new float[original.ClutterObjects.Count];
            var r = recipes.GetValueOrDefault(original.Name) ?? stock.Ecotypes[ei];
            if (r.Signature != ClutterCapture.Signature(original)) throw new InvalidOperationException($"Ecotype {r.Name} changed since capture; recapture its stock recipe.");
            if (r.Objects.Count != original.ClutterObjects.Count) throw new InvalidOperationException("Changing variant count requires a new identity migration; retain the original slots.");
            var e = new ClutterEcotypeReference { Name = original.Name, Placement = Placement(body, r, assets), CollisionType = new((ClutterEcotypePhysicalData.CollisionType)r.CollisionMode) };
            if (!r.Enabled) { e.Placement.BiomeMask = 0; e.CollisionType.Type = ClutterEcotypePhysicalData.CollisionType.None; }
            var cellWidth = CubeCellGrid.GetCellWidth(body.MeanRadius, r.Placement.Separation);
            var k = Math.Ceiling((r.Placement.Range + cellWidth / Math.Sqrt(2)) / (16 * r.Placement.Separation));
            candidates = checked(candidates + (long)(256 * Math.Pow(2 * k + 1, 2)));
            if (candidates > recipe.CandidateBudget) throw new InvalidOperationException($"Clutter needs {candidates:N0} candidate slots, exceeding budget {recipe.CandidateBudget:N0}.");
            for (var oi = 0; oi < r.Objects.Count; oi++)
            {
                var source = original.ClutterObjects[oi]; var item = r.Objects[oi];
                if (item.SourceId != source.Id) throw new InvalidOperationException($"Variant {oi} identity changed; replace its meshes while retaining SourceId.");
                // KSA 5447: displaced bodies read AngularDamping per slot (0.5 on stock trees).
                var o = new ClutterObjectTemplate { Id = source.Id, MassKg = item.MassKg, VolumeM3 = source.VolumeM3, AngularDamping = source.AngularDamping };
                foreach (var l in item.Lods)
                {
                    if (l.MeshIds.Count == 0) throw new InvalidOperationException($"{r.Name}/{item.Name}: every LOD requires at least one mesh.");
                    var lod = new GroundClutterLodReference { MinScreenSizePixels = l.MinScreenSize, CastShadows = l.CastShadows, MeshIds = [], MaterialReferences = [] };
                    var meshes = l.MeshIds.Select(assets.ResolveMesh).ToArray();
                    var slots = new GlbMaterialSlots(meshes.Select(m => (m.Id, m.PrimitiveMaterialIds)));
                    if (l.Materials.Count != 1 && l.Materials.Count != slots.Count)
                        throw new InvalidOperationException($"{r.Name}/{item.Name}: mesh group has {slots.Count} material slots; assign one material or exactly that many.");
                    foreach (var mesh in meshes)
                    {
                        lod.Meshes.Add(Geometry.Copy(mesh, item.Transform, slots.Mapping(mesh.Id, mesh.PrimitiveMaterialIds)));
                        lod.MeshIds.Add(new SerializedReference { Id = mesh.Id });
                    }
                    if (Geometry.VertexCount > recipe.MeshVertexBudget) throw new InvalidOperationException($"Atlas needs {Geometry.VertexCount:N0} repeated vertices; budget is {recipe.MeshVertexBudget:N0}.");
                    for (var mi = 0; mi < slots.Count; mi++)
                    {
                        var mr = l.Materials[l.Materials.Count == 1 ? 0 : mi];
                        var material = Material(mr, assets, stockBackfaceNormals);
                        lod.MaterialReferences.Add(material); e.MaterialReferences.Add(material); Materials.Add(material);
                        if (mr.SourceColors) SourceColorMaterials.Add(material.Hash);
                    }
                    BuildIndirection.Invoke(lod, null);
                    o.Lods.Add(lod);
                }
                if (item.Collision == CollisionPolicy.KeepOriginal) foreach (var c in source.Colliders) o.Colliders.Add(ClutterColliders.Build(ClutterCapture.Collider(c), assets, stockHull: true));
                else if (item.Collision == CollisionPolicy.Custom)
                    foreach (var c in item.Colliders.Where(c => c.Enabled)) o.Colliders.Add(ClutterColliders.Build(c, assets));
                if (e.Collideable && o.Colliders.Count > 0 && item.MassKg <= 0) throw new InvalidOperationException("Objects with installed colliders require positive mass.");
                foreach (var c in o.Colliders) PhysicalObjectRadii[ei][oi] = Math.Max(PhysicalObjectRadii[ei][oi], (float)ClutterColliders.Reach(c));
                PhysicalRadii[ei] = Math.Max(PhysicalRadii[ei], PhysicalObjectRadii[ei][oi] * Math.Max(r.Placement.MaxScale.X, r.Placement.MinScale.X));
                e.ClutterObjects.Add(o);
            }
            if (e.Collideable && (r.Placement.MinScale.X != r.Placement.MinScale.Y || r.Placement.MinScale.Y != r.Placement.MinScale.Z || r.Placement.MaxScale.X != r.Placement.MaxScale.Y || r.Placement.MaxScale.Y != r.Placement.MaxScale.Z))
                throw new InvalidOperationException("Collidable ecotypes require uniform XYZ scale.");
            _ = e.ToParameters(); // Validate texture bindings and collision orientation before allocating render resources.
            Reference.Ecotypes.Add(e);
        }
    }

    private static GroundClutterPlacementReference Placement(Celestial body, EcotypeRecipe e, ClutterAssets assets)
    {
        var p = e.Placement;
        uint mask = p.AllBiomes ? uint.MaxValue : 0;
        if (!p.AllBiomes)
            foreach (var alias in p.Biomes)
            {
                var id = body.BodyTemplate.BiomesReference?.GetBiomeId(alias) ?? -1;
                if (id is < 0 or >= 32) throw new InvalidOperationException($"Biome '{alias}' cannot be resolved to a 32-bit mask.");
                mask |= 1u << id;
            }
        return new GroundClutterPlacementReference
        {
            BiomeMask = mask, RawBiomes = string.Join(",", p.Biomes), DistributionTextureReference = assets.ResolveTexture(p.DistributionId),
            ObjectSeparation = new(p.Separation), GenerationRange = new(p.Range), DistributionTextureTiling = new(p.DistributionTiling),
            MinScale = new(new float3(p.MinScale.X, p.MinScale.Y, p.MinScale.Z)), MaxScale = new(new float3(p.MaxScale.X, p.MaxScale.Y, p.MaxScale.Z)),
            Orientation = new((ClutterOrientationReference.OrientationMode)p.Orientation), MinRotation = new(p.MinRotation * Math.PI / 180), MaxRotation = new(p.MaxRotation * Math.PI / 180),
            SlopeMaskStrength = new(p.SlopeStrength), SlopeMaskContrast = new(p.SlopeContrast), SlopeMaskBias = new(p.SlopeBias),
            AltitudeDensityCurve = new CubicHermiteSpline { SplinePoints = p.AltitudeCurve.Select(k => new SplinePoint { Key = new(k.Altitude), Value = new(k.Density), InTangent = new(k.InTangent), OutTangent = new(k.OutTangent) }).ToList() },
            UseObjectTypeTexture = new(p.UseObjectTypeTexture), ObjectTypeTextureReference = string.IsNullOrEmpty(p.ObjectTypeTextureId) ? null : assets.ResolveTexture(p.ObjectTypeTextureId),
            ObjectTypeTextureTiling = new(p.ObjectTypeTiling), ObjectTypeTextureJitter = new(p.ObjectTypeJitter)
        };
    }

    /// <summary>Stock KeepBackfaceNormals by material ID, inherited by recipes that predate the flag.</summary>
    private static Dictionary<string, bool> StockBackfaceNormals(GroundClutterReference baseline)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var lod in baseline.Ecotypes.SelectMany(e => e.ClutterObjects).SelectMany(o => o.Lods))
            foreach (var material in lod.MaterialReferences!.Select(m => m.Get()))
                result.TryAdd(material.Id, material.KeepBackfaceNormals.Value);
        return result;
    }

    private static GroundClutterMaterialReference Material(MaterialRecipe r, ClutterAssets assets, IReadOnlyDictionary<string, bool> stockBackfaceNormals)
    {
        TextureReference? Texture(string id) => string.IsNullOrEmpty(id) ? null : assets.ResolveTexture(id);
        var normal = Texture(r.NormalId) ?? TextureReference.EmptyNormal;
        var m = new GroundClutterMaterialReference
        {
            Id = "Pebbles/" + Guid.NewGuid().ToString("N"), DiffuseReference = Texture(r.DiffuseId) ?? TextureReference.EmptyWhite,
            NormalReference = new BorrowedNormal(normal), PBRMap = Texture(r.PbrId) ?? TextureReference.EmptyWhite,
            OpacityMap = Texture(r.OpacityId), ThicknessMap = Texture(r.ThicknessId), UseTerrainMask = new(r.UseTerrainMask && !r.SourceColors),
            DoubleSided = new(r.DoubleSided), KeepBackfaceNormals = new(r.ResolveKeepBackfaceNormals(stockBackfaceNormals)), CastShadows = new(r.CastShadows), ReceiveShadows = new(r.ReceiveShadows),
            BiasNormalsUp = new(r.BiasNormalsUp), ApplyExtraSpec = new(r.ApplyExtraSpec), DistanceFadeDither = new(r.DistanceFadeDither)
        };
        m.SetHash(); m.ColorPipelineFlags = m.CreatePipelineFlags(); return m;
    }
    private sealed class BorrowedNormal(TextureReference source) : TexturePowerReference
    { public override TextureReference Get() => source; }
    public void Dispose() => Geometry.Dispose();
}
