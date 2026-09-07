using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using KSA;
using MeowSci.PebblesLib;
using RenderCore;

namespace MeowSci.SphinxLib;

[StructLayout(LayoutKind.Sequential)]
internal struct StaticVertex { public Vector3 Position, Normal; public Vector2 Uv; }

internal sealed class StaticGeometry
{
    internal sealed record Primitive(StaticVertex[] Vertices, uint[] Indices, StaticObjectModel.PerDrawData Material, TextureReference[] Textures);
    public List<Primitive> Primitives { get; } = new();
    public Vector3 Min { get; private set; } = new(float.PositiveInfinity);
    public Vector3 Max { get; private set; } = new(float.NegativeInfinity);
    public int VertexCount { get; private set; }

    public static StaticGeometry Read(ClutterAssets assets, string id, ImportedPngTexture? png)
    {
        var mesh = assets.ResolveMesh(id);
        var recipes = assets.GlbMaterials(id);
        int[] slots = mesh.PrimitiveMaterialIds.Distinct().Order().ToArray();
        var geometry = new StaticGeometry();
        for (int i = 0; i < mesh.PrimitiveCount; i++)
        {
            var host = mesh.HostPrimitives[i];
            var positions = host.GetVertexSpan<Vector3>(MeshAttribute.Position);
            var normals = host.GetVertexSpan<Vector3>(MeshAttribute.Normal);
            var uv = host.GetVertexSpan<Vector2>(MeshAttribute.Uv0);
            var indexBuffer = host.IndexBuffer ?? throw new InvalidOperationException("Imported mesh has no index buffer.");
            var indices = indexBuffer.AsSpan<uint>().ToArray(); // Pebbles' GLB importer writes uint indices.
            var recipe = recipes[Array.IndexOf(slots, mesh.PrimitiveMaterialIds[i])];
            int positionCount = positions.Length;
            if (positions.Length == 0 || normals.Length != positions.Length || uv.Length != positions.Length ||
                indexBuffer.Stride != 4 || indices.Length % 3 != 0 || indices.Any(v => v >= positionCount))
                throw new InvalidOperationException("Imported mesh streams are incomplete.");
            int copies = recipe.DoubleSided ? 2 : 1;
            geometry.VertexCount += checked(positions.Length * copies);
            if (geometry.VertexCount > 2_000_000) throw new InvalidOperationException("This model exceeds two million rendered vertices. Simplify the GLB first.");
            var vertices = new StaticVertex[positions.Length * copies];
            for (int v = 0; v < positions.Length; v++)
            {
                vertices[v] = new() { Position = positions[v], Normal = normals[v], Uv = uv[v] };
                geometry.Min = Vector3.Min(geometry.Min, positions[v]); geometry.Max = Vector3.Max(geometry.Max, positions[v]);
                if (copies == 2) vertices[v + positions.Length] = new() { Position = positions[v], Normal = -normals[v], Uv = uv[v] };
            }
            if (copies == 2)
            {
                int count = indices.Length;
                Array.Resize(ref indices, checked(count * 2));
                for (int t = 0; t < count; t += 3)
                {
                    indices[count+t] = indices[t] + (uint)positions.Length;
                    indices[count+t+1] = indices[t+2] + (uint)positions.Length;
                    indices[count+t+2] = indices[t+1] + (uint)positions.Length;
                }
            }
            TextureReference Resolve(string name, TextureReference fallback) => name.Length == 0 ? fallback : assets.ResolveTexture(name);
            var diffuse = png?.Color ?? Resolve(recipe.DiffuseId, TextureReference.EmptyWhite);
            var normal = Resolve(recipe.NormalId, TextureReference.EmptyNormal);
            var pbr = Resolve(recipe.PbrId, TextureReference.EmptyWhite);
            var alpha = png != null ? png.Opacity : recipe.OpacityId.Length > 0 ? assets.ResolveTexture(recipe.OpacityId) : null;
            var material = new StaticObjectModel.PerDrawData
            {
                DiffuseTextureIndex = diffuse.BindlessHandle, NormalTextureIndex = normal.BindlessHandle,
                PbrTextureIndex = pbr.BindlessHandle, AlphaTextureIndex = alpha?.BindlessHandle ?? -1,
                EmissiveTextureIndex = -1, TfiTextureIndex = -1
            };
            geometry.Primitives.Add(new(vertices, indices, material, alpha == null ? new[] { diffuse, normal, pbr } : new[] { diffuse, normal, pbr, alpha }));
        }
        if (geometry.Primitives.Count == 0) throw new InvalidOperationException("The GLB has no drawable geometry.");
        return geometry;
    }
}
