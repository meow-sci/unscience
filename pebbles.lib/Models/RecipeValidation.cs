using System;
using System.Collections.Generic;

namespace MeowSci.PebblesLib;

/// <summary>Detached structural validation; no registry, render or physics access.</summary>
public static class RecipeValidation
{
    public static void Validate(PebblesRecipe value)
    {
        Require(value != null && value.Version == 1, "Unsupported Pebbles recipe.");
        Require(value!.Ecotypes != null && value.Ecotypes.Count <= 128, "Invalid ecotype list.");
        Require(value.CandidateBudget is > 0 and <= 20_000_000 && value.MeshVertexBudget is > 0 and <= 20_000_000, "Budgets must be between 1 and 20 million.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in value.Ecotypes!)
        {
            Require(e != null && !string.IsNullOrWhiteSpace(e.Name) && names.Add(e.Name), "Ecotype names must be unique.");
            Require(e!.Signature != null && Enum.IsDefined(e.CollisionMode), "Invalid ecotype identity or collision mode.");
            Require(e.Objects != null && e.Objects.Count is > 0 and <= 51, "Each ecotype needs 1–51 variants (five LODs each).");
            Placement(e.Placement, e.CollisionMode != ClutterCollisionMode.None);
            foreach (var o in e.Objects!) Object(o);
        }
    }

    private static void Placement(PlacementRecipe p, bool collidable)
    {
        Require(p != null && p.Biomes != null && p.Biomes.Count <= 32 && p.AltitudeCurve != null && p.AltitudeCurve.Count is >= 2 and <= 256, "Invalid placement lists.");
        Require(p!.DistributionId != null && p.ObjectTypeTextureId != null, "Texture identity cannot be null.");
        Positive(p.Separation, "Separation"); Positive(p.Range, "Range"); Positive(p.DistributionTiling, "Distribution tiling"); Positive(p.ObjectTypeTiling, "Object type tiling");
        Vector(p.MinScale, true); Vector(p.MaxScale, true);
        Require(p.MaxScale.X >= p.MinScale.X && p.MaxScale.Y >= p.MinScale.Y && p.MaxScale.Z >= p.MinScale.Z, "Maximum scale must exceed minimum scale.");
        Require(Enum.IsDefined(p.Orientation), "Invalid orientation.");
        Require(!collidable || p.Orientation != OrientationMode.SurfaceNormalSmooth, "Smooth normal orientation cannot have native collision.");
        Require(!collidable || p.MinScale.X == p.MinScale.Y && p.MinScale.Y == p.MinScale.Z && p.MaxScale.X == p.MaxScale.Y && p.MaxScale.Y == p.MaxScale.Z, "Collidable ecotypes require uniform XYZ scale.");
        Finite(p.MinRotation); Finite(p.MaxRotation); Require(p.MaxRotation >= p.MinRotation, "Rotation maximum must exceed minimum.");
        Finite(p.SlopeStrength); Finite(p.SlopeContrast); Finite(p.SlopeBias); Finite(p.ObjectTypeJitter);
        double previous = double.NegativeInfinity;
        foreach (var point in p.AltitudeCurve!)
        {
            Require(point != null, "Missing altitude point."); Finite(point!.Altitude); Finite(point.Density); Finite(point.InTangent); Finite(point.OutTangent);
            Require(point.Altitude > previous && point.Density >= 0, "Altitude points must increase and density must be nonnegative."); previous = point.Altitude;
        }
        if (p.Curvature is { } curvature)
        {
            Finite(curvature.MinimumPerKm); Finite(curvature.MaximumPerKm);
            Require(curvature.MaximumPerKm > curvature.MinimumPerKm, "Curvature maximum must exceed minimum.");
            Require(curvature.TexelSpacing is 1 or 2 or 4 or 8, "Curvature texel spacing must be 1, 2, 4 or 8.");
            Require(curvature.Curve != null && curvature.Curve.Count is >= 2 and <= 256, "Invalid curvature curve.");
            double key = double.NegativeInfinity;
            foreach (var point in curvature.Curve!)
            {
                Require(point != null, "Missing curvature point.");
                Finite(point!.Altitude); Finite(point.Density); Finite(point.InTangent); Finite(point.OutTangent);
                Require(point.Altitude > key && point.Density >= 0, "Curvature keys must increase and density must be nonnegative.");
                key = point.Altitude;
            }
        }
        foreach (var biome in p.Biomes!) Require(!string.IsNullOrWhiteSpace(biome), "Missing biome identity.");
    }

    public static void Object(ObjectRecipe o)
    {
        Require(o != null && o.SourceId != null && o.Name != null && o.Transform != null, "Invalid object recipe.");
        Require(o!.Lods != null && o.Lods.Count == 5, "Clutter objects require all five LOD slots.");
        Require(Enum.IsDefined(o.Collision) && o.Colliders != null && o.Colliders.Count <= 128, "Invalid collider list.");
        Finite(o.MassKg); Require(o.MassKg >= 0, "Mass must be nonnegative."); Vector(o.Transform!.Position); Vector(o.Transform.RotationDegrees); Vector(o.Transform.Scale, true);
        float previousLod = float.PositiveInfinity;
        foreach (var lod in o.Lods!)
        {
            Require(lod != null && lod.MeshIds != null && lod.MeshIds.Count <= 128 && lod.Materials != null && lod.Materials.Count <= 256, "Invalid LOD mesh/material list.");
            Finite(lod!.MinScreenSize); Require(lod.MinScreenSize >= 0 && lod.MinScreenSize <= previousLod, "LOD thresholds must descend and be nonnegative."); previousLod = lod.MinScreenSize;
            foreach (var mesh in lod.MeshIds!) Require(!string.IsNullOrWhiteSpace(mesh), "Missing mesh identity.");
            foreach (var m in lod.Materials!) Material(m);
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in o.Colliders!)
        {
            Require(c != null && !string.IsNullOrWhiteSpace(c.Id) && ids.Add(c.Id) && c.Name != null && c.HullMeshId != null && Enum.IsDefined(c.Kind), "Collider identities must be unique.");
            Vector(c!.Position); Vector(c.RotationDegrees); Vector(c.Dimensions, true); Vector(c.HullScale, true);
            Require(c.Kind != ColliderKind.Capsule || c.Dimensions.Y >= c.Dimensions.X, "Capsule total height must be at least its diameter.");
            Require(c.Kind != ColliderKind.ConvexHull || !string.IsNullOrWhiteSpace(c.HullMeshId), "Convex hull requires a mesh.");
        }
    }
    public static void Material(MaterialRecipe m)
    {
        Require(m != null && m.SourceId != null && m.DiffuseId != null && m.NormalId != null && m.PbrId != null && m.OpacityId != null && m.ThicknessId != null, "Invalid material identity.");
        if (m!.TerrainFadeStart is { } start) Finite(start);
        if (m.TerrainFadeEnd is { } end) Finite(end);
        if (m.TerrainFadeStart.HasValue && m.TerrainFadeEnd.HasValue)
            Require(m.TerrainFadeEnd > m.TerrainFadeStart, "Terrain fade end must exceed start.");
    }
    public static void Vector(Vec3 v, bool positive = false) { Finite(v.X); Finite(v.Y); Finite(v.Z); Require(!positive || v.X > 0 && v.Y > 0 && v.Z > 0, "Dimensions and scales must be positive."); }
    private static void Positive(double v, string name) { Finite(v); Require(v > 0, name + " must be positive."); }
    private static void Finite(double v) => Require(double.IsFinite(v), "Values must be finite.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
