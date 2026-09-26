using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;

namespace MeowSci.PebblesLib;

/// <summary>Serializable vectors have properties, unlike Numerics vectors' public fields.</summary>
public readonly record struct Vec3(float X, float Y, float Z)
{
    public static Vec3 Zero => new(0, 0, 0);
    public static Vec3 One => new(1, 1, 1);
    [System.Text.Json.Serialization.JsonIgnore]
    public Vector3 Vector => new(X, Y, Z);
    public static Vec3 From(Vector3 value) => new(value.X, value.Y, value.Z);
}

public sealed class TransformRecipe
{
    public Vec3 Position { get; set; } = Vec3.Zero;
    public Vec3 RotationDegrees { get; set; } = Vec3.Zero;
    public Vec3 Scale { get; set; } = Vec3.One;
}

public enum ColliderKind { Box, Sphere, Capsule, Cylinder, ConvexHull }
public enum CollisionPolicy { KeepOriginal, None, Custom }
public enum ClutterCollisionMode { None, PrimitiveList, ConvexHullList }
public enum OrientationMode { Up, SurfaceNormal, SurfaceNormalAndGradient, SurfaceNormalSmooth }

public sealed class ColliderRecipe
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Box";
    public ColliderKind Kind { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public Vec3 Position { get; set; }
    public Vec3 RotationDegrees { get; set; }
    // Full XYZ box dimensions; sphere X=diameter; capsule/cylinder X=diameter,Y=total height.
    public Vec3 Dimensions { get; set; } = Vec3.One;
    public string HullMeshId { get; set; } = "";
    public Vec3 HullScale { get; set; } = Vec3.One;
}

public sealed class MaterialRecipe
{
    public string SourceId { get; set; } = "";
    public string DiffuseId { get; set; } = "";
    public string NormalId { get; set; } = "";
    public string PbrId { get; set; } = "";
    public string OpacityId { get; set; } = "";
    public string ThicknessId { get; set; } = "";
    public bool UseTerrainMask { get; set; }
    public bool DoubleSided { get; set; }
    public bool CastShadows { get; set; } = true;
    public bool ReceiveShadows { get; set; } = true;
    public bool BiasNormalsUp { get; set; }
    public bool ApplyExtraSpec { get; set; }
    public bool DistanceFadeDither { get; set; }
    public bool SourceColors { get; set; }
    /// <summary>KSA 5473 double-sided lighting flag (stock trees). Null (recipes saved before it existed,
    /// or imports) inherits the stock material named by <see cref="SourceId"/>, otherwise false.</summary>
    public bool? KeepBackfaceNormals { get; set; }

    public bool ResolveKeepBackfaceNormals(IReadOnlyDictionary<string, bool> stockFlags)
        => KeepBackfaceNormals ?? (stockFlags.TryGetValue(SourceId, out var stock) && stock);
}

public sealed class LodRecipe
{
    public float MinScreenSize { get; set; }
    public bool CastShadows { get; set; } = true;
    public List<string> MeshIds { get; set; } = new();
    public List<MaterialRecipe> Materials { get; set; } = new();
}

public sealed class ObjectRecipe
{
    public string SourceId { get; set; } = "";
    public string Name { get; set; } = "Object";
    public TransformRecipe Transform { get; set; } = new();
    public List<LodRecipe> Lods { get; set; } = new() { new(), new(), new(), new(), new() };
    public CollisionPolicy Collision { get; set; } = CollisionPolicy.KeepOriginal;
    public List<ColliderRecipe> Colliders { get; set; } = new();
    public double MassKg { get; set; } = 1;
}

public sealed class CurvePoint
{
    public double Altitude { get; set; }
    public double Density { get; set; } = 1;
    public double InTangent { get; set; }
    public double OutTangent { get; set; }
}

public sealed class PlacementRecipe
{
    public List<string> Biomes { get; set; } = new();
    public bool AllBiomes { get; set; } = true;
    public string DistributionId { get; set; } = "";
    public double Separation { get; set; } = 10;
    public double Range { get; set; } = 500;
    public float DistributionTiling { get; set; } = 250;
    public Vec3 MinScale { get; set; } = Vec3.One;
    public Vec3 MaxScale { get; set; } = Vec3.One;
    public OrientationMode Orientation { get; set; }
    public float MinRotation { get; set; }
    public float MaxRotation { get; set; } = 360;
    public float SlopeStrength { get; set; }
    public float SlopeContrast { get; set; } = 1;
    public float SlopeBias { get; set; }
    public List<CurvePoint> AltitudeCurve { get; set; } = new()
    {
        new() { Altitude = -5000 }, new() { Altitude = 10000 }
    };
    public bool UseObjectTypeTexture { get; set; }
    public string ObjectTypeTextureId { get; set; } = "";
    public float ObjectTypeTiling { get; set; } = 500;
    public float ObjectTypeJitter { get; set; }
}

public sealed class EcotypeRecipe
{
    public string Name { get; set; } = "";
    public string Signature { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public ClutterCollisionMode CollisionMode { get; set; }
    public PlacementRecipe Placement { get; set; } = new();
    public List<ObjectRecipe> Objects { get; set; } = new();
}

public sealed class PebblesRecipe
{
    public int Version { get; set; } = 1;
    public List<EcotypeRecipe> Ecotypes { get; set; } = new();
    public long CandidateBudget { get; set; } = 2_000_000;
    public long MeshVertexBudget { get; set; } = 2_000_000;
}

public static class RecipeCopy
{
    private static readonly JsonSerializerOptions Options = new() { MaxDepth = 64 };
    public static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)
        ?? throw new InvalidOperationException("Recipe cannot be null.");
}
