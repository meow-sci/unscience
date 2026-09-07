using System;
using System.Numerics;
using Brutal.Numerics;
using KSA;

namespace MeowSci.SphinxLib;

internal sealed class SphinxEntry : IDisposable
{
    public int Id;
    public string MeshId = "";
    public string? Png;
    public GroundAnchor Anchor;
    public bool Visible = true, Align = true;
    public float3 Scale = new(1), Rotation, Offset;
    public TextureMapping Mapping = TextureMapping.Identity;
    public required StaticModelResources Model;
    public CollisionMode Collision = CollisionMode.Auto;
    public StaticCollider? Collider;

    public float4x4 Matrix(Camera camera)
    {
        var local = PlacementMath.GroundedLocal(Model.Min, Model.Max, Vector(Scale), Vector(Rotation), Vector(Offset));
        var matrix = new double4x4(local.M11,local.M12,local.M13,local.M14,local.M21,local.M22,local.M23,local.M24,
            local.M31,local.M32,local.M33,local.M34,local.M41,local.M42,local.M43,local.M44);
        return float4x4.Pack(matrix * GroundPlacement.Frame(Anchor, Align, camera));
    }
    public static Vector3 Vector(float3 v) => new(v.X,v.Y,v.Z);
    public void Dispose()
    {
        SphinxPhysics.Detach(this);
        Collider?.Dispose(); Collider = null;
        Model.Dispose();
    }
}
