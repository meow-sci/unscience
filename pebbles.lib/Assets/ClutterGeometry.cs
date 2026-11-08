using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Brutal;
using NativeStrideList = Brutal.Collections.NativeStrideList;
using Brutal.Numerics;
using KSA;
using RenderCore;

namespace MeowSci.PebblesLib;

internal sealed class ClutterGeometry : IDisposable
{
    private readonly List<MeshAsset> _owned = [];
    private static readonly PropertyInfo Hosts = typeof(MeshReference).GetProperty(nameof(MeshReference.HostPrimitives))!;
    private static readonly PropertyInfo Materials = typeof(MeshReference).GetProperty(nameof(MeshReference.PrimitiveMaterialIds))!;
    public long VertexCount { get; private set; }

    internal static NativeStrideList CopyNative<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        var list = new NativeStrideList(ByteSize.Of<T>(), values.Length);
        try { list.AddRange<T>(values); return list; }
        catch { list.Dispose(); throw; }
    }

    public MeshReference Copy(MeshReference source, TransformRecipe transform, IReadOnlyDictionary<int, int> materialMap)
    {
        if (source.HostPrimitives is not { Length: > 0 }) throw new InvalidOperationException($"Mesh {source.Id} has no retained CPU geometry.");
        var scale = transform.Scale.Vector;
        if (scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0) throw new InvalidOperationException("Mesh scales must be positive.");
        var radians = transform.RotationDegrees.Vector * (MathF.PI / 180);
        var matrix = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationX(radians.X) * Matrix4x4.CreateRotationY(radians.Y) * Matrix4x4.CreateRotationZ(radians.Z) * Matrix4x4.CreateTranslation(transform.Position.Vector);
        if (!Matrix4x4.Invert(matrix, out var inverse)) throw new InvalidOperationException("Mesh transform is singular.");
        var normalsMatrix = Matrix4x4.Transpose(inverse);
        var primitives = new MeshAsset[source.PrimitiveCount];
        var materialIds = new int[primitives.Length];
        for (var i = 0; i < primitives.Length; i++)
        {
            var input = source.HostPrimitives[i];
            var positions = input.GetVertexSpan<float3>(MeshAttribute.Position);
            var normals = input.GetVertexSpan<float3>(MeshAttribute.Normal);
            var uv = input.GetVertexSpan<float2>(MeshAttribute.Uv0);
            if (positions.Length == 0 || normals.Length != positions.Length || uv.Length != positions.Length || input.IndexCount == 0 || input.IndexCount % 3 != 0)
                throw new InvalidOperationException($"Mesh {source.Id}, primitive {i} needs triangle indices and matching position, normal and UV0 streams.");
            var p = new float3[positions.Length]; var n = new float3[p.Length];
            var min = new Vector3(float.PositiveInfinity); var max = new Vector3(float.NegativeInfinity);
            for (var j = 0; j < p.Length; j++)
            {
                var v = Vector3.Transform(new Vector3(positions[j].X, positions[j].Y, positions[j].Z), matrix);
                var normal = Vector3.TransformNormal(new Vector3(normals[j].X, normals[j].Y, normals[j].Z), normalsMatrix);
                if (!float.IsFinite(v.X + v.Y + v.Z) || normal.LengthSquared() < 1e-20f) throw new InvalidOperationException($"Mesh {source.Id} contains invalid geometry.");
                normal = Vector3.Normalize(normal);
                p[j] = new float3(v.X, v.Y, v.Z); n[j] = new float3(normal.X, normal.Y, normal.Z);
                min = Vector3.Min(min, v); max = Vector3.Max(max, v);
            }
            var indices = new uint[input.IndexCount];
            if (input.IndexBuffer!.Stride == ByteSize.Of<ushort>())
            { var span = input.IndexBuffer.AsSpan<ushort>(); for (var j = 0; j < indices.Length; j++) indices[j] = span[j]; }
            else if (input.IndexBuffer.Stride == ByteSize.Of<uint>()) input.IndexBuffer.AsSpan<uint>().CopyTo(indices);
            else throw new InvalidOperationException("Only ushort and uint triangle indices are supported.");
            foreach (var index in indices) if (index >= p.Length) throw new InvalidOperationException($"Mesh {source.Id} has an out-of-range index.");
            var output = new MeshAsset { VertexCount = p.Length, PositionMinimum = new double3(min.X, min.Y, min.Z), PositionMaximum = new double3(max.X, max.Y, max.Z) };
            _owned.Add(output);
            output.SetVertexList(MeshAttribute.Position, CopyNative<float3>(p));
            output.SetVertexList(MeshAttribute.Normal, CopyNative<float3>(n));
            output.SetVertexList(MeshAttribute.Uv0, CopyNative<float2>(uv));
            // Always uint source storage: avoids native ushort->uint staging underallocation in mixed atlases.
            output.SetIndexBuffer(CopyNative<uint>(indices));
            primitives[i] = output; VertexCount += p.Length;
            var oldMaterial = source.PrimitiveMaterialIds[i];
            materialIds[i] = materialMap[oldMaterial];
        }
        var result = new MeshReference { Id = source.Id, PrimitiveCount = primitives.Length };
        result.SetHash(); Hosts.SetValue(result, primitives); Materials.SetValue(result, materialIds);
        return result;
    }

    public void Dispose() { foreach (var mesh in _owned) mesh.Dispose(); _owned.Clear(); }
}
