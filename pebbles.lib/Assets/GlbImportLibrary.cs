using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Brutal.Collections;
using KSA;
using RenderCore;
using MeowSci.KsaAbstractions;

namespace MeowSci.PebblesLib;

public sealed record GlbMeshOption(string Id, string Label);

/// <summary>Runtime import cache, independent of drafts. Exact content versions never replace one another.</summary>
internal sealed class GlbImportLibrary : IDisposable
{
    private sealed class Source(GlbDocument document, GlbIdentity identity) : IDisposable
    {
        public readonly GlbDocument Document = document;
        public readonly GlbIdentity Identity = identity;
        public readonly GlbMaterials Materials = new(document, identity.SourceKey);
        public readonly Dictionary<int, MeshReference> Meshes = [];
        public void Dispose()
        {
            // The caller has retired live/preview borrowers; this waits only if textures were
            // actually uploaded. Import-only CPU sources do not require a renderer to release.
            Materials.Dispose();
            foreach (var mesh in Meshes.Values) { mesh.Dispose(); foreach (var p in mesh.HostPrimitives) p.Dispose(); }
            Meshes.Clear(); Document.Dispose();
        }
    }
    private readonly Dictionary<string, Source> _sources = new(StringComparer.Ordinal);
    public IEnumerable<string> MeshIds => _sources.Values.SelectMany(Options).Select(o => o.Id);
    public IEnumerable<string> TextureIds => _sources.Values.SelectMany(s => s.Materials.TextureIds);
    public int SourceCount => _sources.Count;
    public IReadOnlyList<GlbMeshOption> Import(string path)
    {
        path = Path.GetFullPath(path);
        var document = GlbDocument.Load(path);
        var identity = new GlbIdentity(path, document.Hash, "");
        return Options(Adopt(document, identity));
    }
    // Consumes document ownership on success and failure. Caller never reopens a verified file.
    private Source Adopt(GlbDocument document, GlbIdentity identity)
    {
        if (_sources.TryGetValue(identity.SourceKey, out var existing)) { document.Dispose(); return existing; }
        if (_sources.Count >= 16) { document.Dispose(); throw new InvalidOperationException("Release imported GLB assets before loading more than 16 content versions."); }
        var source = new Source(document, identity);
        try
        {
            _ = Options(source); // Reject malformed option metadata before publishing a cache entry.
            _sources.Add(identity.SourceKey, source);
            return source;
        }
        catch { source.Dispose(); throw; }
    }
    public IReadOnlyList<GlbMeshOption> OptionsFor(string id) => Options(ResolveSource(id));
    private static GlbMeshOption[] Options(Source s) => new[] { new GlbMeshOption(s.Identity.MeshId(-1), "Complete scene (node transforms)") }
        .Concat(Enumerable.Range(0, s.Document.MeshCount).Select(i => new GlbMeshOption(s.Identity.MeshId(i), $"{i}: {s.Document.MeshName(i)} (mesh-local)"))).ToArray();
    private Source ResolveSource(string id)
    {
        var identity = GlbIdentity.Parse(id);
        if (_sources.TryGetValue(identity.SourceKey, out var source)) return source;
        // Only explicit import, preview refresh or Apply reaches this method, never draft restoration.
        // Save identities may carry a different machine's original library root. Resolve the
        // copied filename under this installation; the content hash still must match exactly.
        var document = GlbDocument.Load(GlbLibrary.Files.FullPath(identity.LibraryFileName));
        if (!document.Hash.Equals(identity.Hash, StringComparison.Ordinal))
        {
            document.Dispose();
            throw new InvalidOperationException("The GLB file has changed. Import it again and explicitly select the new version.");
        }
        return Adopt(document, identity);
    }
    public MeshReference ResolveMesh(string id)
    {
        var source = ResolveSource(id); var identity = GlbIdentity.Parse(id);
        if (!identity.Part.StartsWith("/mesh/", StringComparison.Ordinal) || !int.TryParse(identity.Part[6..], out int index) || index < -1 || index >= source.Document.MeshCount)
            throw new InvalidDataException("Unresolved GLB mesh selection.");
        if (source.Meshes.TryGetValue(index, out var mesh)) return mesh;
        var primitives = index == -1 ? source.Document.ReadScene() : source.Document.ReadMesh(index);
        long retained = _sources.Values.SelectMany(s => s.Meshes.Values).SelectMany(m => m.HostPrimitives).Sum(p => (long)p.VertexCount);
        if (retained + primitives.Sum(p => (long)p.Positions.Length) > 8_000_000) throw new InvalidOperationException("Imported GLB cache exceeds 8 million vertices. Release imports to reclaim it.");
        var hosts = new List<MeshAsset>();
        try
        {
            foreach (var p in primitives)
            {
                var minimum = p.Positions.Aggregate(Vector3.Min); var maximum = p.Positions.Aggregate(Vector3.Max);
                var host = new MeshAsset { VertexCount = p.Positions.Length, PositionMinimum = new(minimum.X, minimum.Y, minimum.Z), PositionMaximum = new(maximum.X, maximum.Y, maximum.Z) };
                hosts.Add(host);
                host.SetVertexList(MeshAttribute.Position, NativeStrideList.FromSpan<Vector3>(p.Positions));
                host.SetVertexList(MeshAttribute.Normal, NativeStrideList.FromSpan<Vector3>(p.Normals));
                host.SetVertexList(MeshAttribute.Uv0, NativeStrideList.FromSpan<Vector2>(p.Uvs));
                host.SetIndexBuffer(NativeStrideList.FromSpan<uint>(p.Indices));
            }
            mesh = new MeshReference { Id = id, PrimitiveCount = hosts.Count };
            typeof(MeshReference).GetProperty(nameof(MeshReference.HostPrimitives))!.SetValue(mesh, hosts.ToArray());
            typeof(MeshReference).GetProperty(nameof(MeshReference.PrimitiveMaterialIds))!.SetValue(mesh, primitives.Select(p => p.Material).ToArray());
            mesh.SetHash(); source.Meshes.Add(index, mesh); return mesh;
        }
        catch { foreach (var host in hosts) host.Dispose(); throw; }
    }
    public List<MaterialRecipe> MaterialsFor(string id)
    {
        var mesh = ResolveMesh(id); var source = ResolveSource(id);
        return mesh.PrimitiveMaterialIds.Distinct().Order().Select(source.Materials.GetMaterial).Select(RecipeCopy.Clone).ToList();
    }
    public TextureReference ResolveTexture(string id) => ResolveSource(id).Materials.ResolveTexture(id);
    public IReadOnlyList<string> WarningsFor(string id) => ResolveSource(id).Materials.Warnings;
    public void Dispose() { foreach (var source in _sources.Values) source.Dispose(); _sources.Clear(); }
}
