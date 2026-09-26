using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using KSA;

namespace MeowSci.PebblesLib;

internal static class ClutterCapture
{
    public static string Signature(ClutterEcotypeReference ecotype)
    {
        var text = new StringBuilder(ecotype.Name);
        foreach (var item in ecotype.ClutterObjects)
        {
            text.Append('|').Append(item.Id);
            foreach (var lod in item.Lods)
            {
                text.Append(';').AppendJoin(',', lod.Meshes.Select(m => m.Id + "/" + m.PrimitiveCount));
                text.Append('/').AppendJoin(',', lod.MaterialReferences!.Select(m => m.Get().Id));
            }
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    public static PebblesRecipe Capture(Celestial body, GroundClutterReference graph)
    {
        var result = new PebblesRecipe();
        foreach (var e in graph.Ecotypes)
        {
            var p = e.Placement;
            var ecotype = new EcotypeRecipe
            {
                Name = e.Name, Signature = Signature(e), CollisionMode = (ClutterCollisionMode)e.CollisionType.Type,
                Placement = new PlacementRecipe
                {
                    AllBiomes = p.BiomeMask == uint.MaxValue,
                    Biomes = body.BodyTemplate.BiomesReference?.Biomes.Where(b => b.Id is >= 0 and < 32 && (p.BiomeMask & (1u << b.Id)) != 0).Select(b => b.Alias).ToList() ?? [],
                    DistributionId = p.DistributionTextureReference?.Get().Id ?? "", Separation = p.ObjectSeparation.InMeters(), Range = p.GenerationRange.InMeters(),
                    DistributionTiling = p.DistributionTextureTiling.Value, MinScale = Vector(p.MinScale), MaxScale = Vector(p.MaxScale),
                    Orientation = (OrientationMode)p.Orientation.Mode, MinRotation = (float)p.MinRotation.ToDegrees(), MaxRotation = (float)p.MaxRotation.ToDegrees(),
                    SlopeStrength = p.SlopeMaskStrength.Value, SlopeContrast = p.SlopeMaskContrast.Value, SlopeBias = p.SlopeMaskBias.Value,
                    AltitudeCurve = p.AltitudeDensityCurve.SplinePoints.Select(k => new CurvePoint { Altitude = k.Key.Value, Density = k.Value.Value, InTangent = k.InTangent.Value, OutTangent = k.OutTangent.Value }).ToList(),
                    UseObjectTypeTexture = p.UseObjectTypeTexture.Value, ObjectTypeTextureId = p.ObjectTypeTextureReference?.Get().Id ?? "",
                    ObjectTypeTiling = p.ObjectTypeTextureTiling.Value, ObjectTypeJitter = p.ObjectTypeTextureJitter.Value
                }
            };
            foreach (var o in e.ClutterObjects)
            {
                var item = new ObjectRecipe { SourceId = o.Id, Name = o.Id, MassKg = o.MassKg, Lods = [] };
                foreach (var l in o.Lods)
                    item.Lods.Add(new LodRecipe { MinScreenSize = l.MinScreenSizePixels, CastShadows = l.CastShadows,
                        MeshIds = l.Meshes.Select(m => m.Id).ToList(), Materials = l.MaterialReferences!.Select(m => Material(m.Get())).ToList() });
                for (var ci = 0; ci < o.Colliders.Count; ci++)
                {
                    var collider = Collider(o.Colliders[ci]);
                    collider.Id = $"stock:{ci}";
                    if (string.IsNullOrEmpty(collider.Name)) collider.Name = $"{collider.Kind} {ci + 1}";
                    item.Colliders.Add(collider);
                }
                ecotype.Objects.Add(item);
            }
            result.Ecotypes.Add(ecotype);
        }
        return result;
    }

    private static MaterialRecipe Material(GroundClutterMaterialReference m) => new()
    {
        SourceId = m.Id, DiffuseId = m.DiffuseReference?.Get().Id ?? "", NormalId = m.NormalReference?.Get().Id ?? "", PbrId = m.PBRMap?.Get().Id ?? "",
        OpacityId = m.OpacityMap?.Get().Id ?? "", ThicknessId = m.ThicknessMap?.Get().Id ?? "", UseTerrainMask = m.UseTerrainMask.Value,
        DoubleSided = m.DoubleSided.Value, KeepBackfaceNormals = m.KeepBackfaceNormals.Value, CastShadows = m.CastShadows.Value, ReceiveShadows = m.ReceiveShadows.Value,
        BiasNormalsUp = m.BiasNormalsUp.Value, ApplyExtraSpec = m.ApplyExtraSpec.Value, DistanceFadeDither = m.DistanceFadeDither.Value
    };

    internal static ColliderRecipe Collider(ColliderTemplate c)
    {
        var result = new ColliderRecipe { Id = c.Id, Name = c.Id, Position = Vector(c.LocationAsmb), RotationDegrees = Degrees(c.Collider2Asmb) };
        switch (c)
        {
            case BoxColliderTemplate b: result.Kind = ColliderKind.Box; result.Dimensions = new((float)b.LengthX.InMeters(), (float)b.LengthY.InMeters(), (float)b.LengthZ.InMeters()); break;
            case SphereColliderTemplate s: result.Kind = ColliderKind.Sphere; result.Dimensions = new((float)s.Radius.InMeters() * 2, (float)s.Radius.InMeters() * 2, (float)s.Radius.InMeters() * 2); break;
            case CapsuleColliderTemplate a: result.Kind = ColliderKind.Capsule; result.Dimensions = new((float)a.Radius.InMeters() * 2, (float)(a.LengthY.InMeters() + 2 * a.Radius.InMeters()), (float)a.Radius.InMeters() * 2); break;
            case CylinderColliderTemplate y: result.Kind = ColliderKind.Cylinder; result.Dimensions = new((float)y.Radius.InMeters() * 2, (float)y.LengthY.InMeters(), (float)y.Radius.InMeters() * 2); break;
            case ConvexHullColliderTemplate h: result.Kind = ColliderKind.ConvexHull; result.HullMeshId = h.Mesh.Get().Id; result.HullScale = h.Scale == null ? Vec3.One : Vector(h.Scale); break;
            default: throw new NotSupportedException($"Clutter collider {c.GetType().Name} cannot be represented safely.");
        }
        return result;
    }

    private static Vec3 Vector(Vector3Reference value) { var v = value.ToFloat3(); return new(v.X, v.Y, v.Z); }
    private static Vec3 Degrees(Vector3Reference value) { var v = Vector(value); return new(v.X * 180 / MathF.PI, v.Y * 180 / MathF.PI, v.Z * 180 / MathF.PI); }
}
