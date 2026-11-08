using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Brutal.GltfApi;
using KSA;

namespace MeowSci.RockyMcRockFaceLib;

/// <summary>A mesh inside a glTF-file asset (kitten, helmet, MMU...) offered for ring use.</summary>
public sealed class GltfMeshEntry
{
    public GltfMeshEntry(string id, string filePath, int meshIndex)
    {
        Id = id;
        FilePath = filePath;
        MeshIndex = meshIndex;
    }

    public string Id { get; }
    public string FilePath { get; }
    public int MeshIndex { get; }
}

/// <summary>
/// Enumerates the game's ModLibrary registries for meshes and textures that can be
/// offered as ring-object replacements. The registries (AllMeshes, AllFiles) are
/// internal static fields on ModLibrary, so a one-time reflection lookup per
/// collection is needed; the SerializedCollection API itself is public.
/// </summary>
public sealed class RingAssetCatalog
{
    private readonly Dictionary<string, MeshReference> _meshById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GltfMeshEntry> _gltfMeshById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextureReference> _textureById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextureReference> _normalById = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ids of all offerable meshes: regular meshes with retained CPU-side geometry
    /// (drop-in or convertible) plus meshes inside glTF-file assets ("GltfId:MeshName" —
    /// characters like the kitten load through that separate pipeline).
    /// </summary>
    public string[] MeshIds { get; private set; } = Array.Empty<string>();

    /// <summary>Ids of all bound textures (valid bindless handle).</summary>
    public string[] TextureIds { get; private set; } = Array.Empty<string>();

    /// <summary>Ids of bound textures referenced by material normal slots.</summary>
    public string[] NormalTextureIds { get; private set; } = Array.Empty<string>();

    public void Refresh()
    {
        RefreshMeshes();
        RefreshGltfMeshes();
        RefreshTextures();
        MeshIds = _meshById.Keys.Concat(_gltfMeshById.Keys)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public bool TryGetGltfMesh(string id, out GltfMeshEntry entry) => _gltfMeshById.TryGetValue(id, out entry!);

    public bool TryGetMesh(string id, out MeshReference mesh) => _meshById.TryGetValue(id, out mesh!);

    /// <summary>Index count of the mesh's first primitive (0 if unknown). Divide by 3 for triangles.</summary>
    public int GetMeshIndexCount(string id)
    {
        if (!_meshById.TryGetValue(id, out var mesh)) return 0;
        if (mesh.DeviceMeshesInterleaved is { Length: > 0 } && mesh.DeviceMeshesInterleaved[0] != null)
            return mesh.DeviceMeshesInterleaved[0].IndexCount;
        if (mesh.DevicePrimitives is { Length: > 0 } && mesh.DevicePrimitives[0] != null)
            return mesh.DevicePrimitives[0].IndexCount;
        return 0;
    }
    public bool TryGetTexture(string id, out TextureReference texture) => _textureById.TryGetValue(id, out texture!);
    public bool TryGetNormalTexture(string id, out TextureReference texture) => _normalById.TryGetValue(id, out texture!);

    private void RefreshMeshes()
    {
        _meshById.Clear();
        var meshes = Collection<MeshReference>("AllMeshes")?.GetList();
        if (meshes == null)
        {
            Console.WriteLine("rocky-mcrock-face: could not resolve ModLibrary.AllMeshes");
            MeshIds = Array.Empty<string>();
            return;
        }

        // Copy before iterating — GetList() returns the live registry list.
        foreach (var mesh in meshes.ToArray())
        {
            if (string.IsNullOrEmpty(mesh.Id)) continue;
            if (mesh.HostPrimitives is not { Length: > 0 } || mesh.HostPrimitives[0] == null) continue;
            // "_VM" meshes are invisible MeshView pick/collision hulls — not useful visually.
            if (mesh.Id.EndsWith("_VM", StringComparison.OrdinalIgnoreCase)) continue;
            _meshById[mesh.Id] = mesh;
        }
    }

    /// <summary>
    /// Enumerates glTF-file assets (ModLibrary.AllGltfs — characters, MMU, helmet...) and
    /// offers each of their meshes. Only the JSON header of each file is parsed here
    /// (GltfUtility.LoadModel); buffers load lazily at conversion time.
    /// </summary>
    private void RefreshGltfMeshes()
    {
        _gltfMeshById.Clear();
        var gltfs = Collection<Gltf2Reference>("AllGltfs")?.GetList();
        if (gltfs == null) return;

        foreach (var gltf in gltfs.ToArray())
        {
            if (string.IsNullOrEmpty(gltf.Id) || gltf.Source?.Value == null) continue;
            try
            {
                string filePath = gltf.Source.Value.ModPath;
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) continue;
                var json = GltfUtility.LoadModel(filePath);
                if (json?.Meshes == null) continue; // animation-only files carry no meshes
                for (int i = 0; i < json.Meshes.Length; i++)
                {
                    string meshName = string.IsNullOrEmpty(json.Meshes[i].Name) ? $"mesh_{i}" : json.Meshes[i].Name;
                    string id = $"{gltf.Id}:{meshName}";
                    if (_gltfMeshById.ContainsKey(id)) id = $"{gltf.Id}:{meshName}#{i}";
                    _gltfMeshById[id] = new GltfMeshEntry(id, filePath, i);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"rocky-mcrock-face: skipping glTF asset '{gltf.Id}': {ex.Message}");
            }
        }
    }

    private void RefreshTextures()
    {
        _textureById.Clear();
        _normalById.Clear();
        var files = Collection<FileReference>("AllFiles")?.GetList();
        if (files == null)
        {
            Console.WriteLine("rocky-mcrock-face: could not resolve ModLibrary.AllFiles");
            TextureIds = Array.Empty<string>();
            NormalTextureIds = Array.Empty<string>();
            return;
        }

        foreach (var file in files.ToArray())
        {
            if (file is not TextureReference texture) continue;
            if (string.IsNullOrEmpty(texture.Id)) continue;
            // Unbound textures (handle 0) have no GPU image view and would sample as empty.
            if (texture.BindlessHandle == 0) continue;
            _textureById[texture.Id] = texture;

        }
        // Normals are no longer a TextureReference subtype. Discover them through material slots.
        foreach (var material in Collection<PbrMaterialReference>("AllMaterials")?.GetList().ToArray()
                     ?? Array.Empty<PbrMaterialReference>())
            if (material.NormalReference?.Value is { } normal && _textureById.ContainsKey(normal.Id))
                _normalById[normal.Id] = normal;
        TextureIds = _textureById.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
        NormalTextureIds = _normalById.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static SerializedCollection<T>? Collection<T>(string fieldName) where T : ILibraryData, IListable
    {
        var field = typeof(ModLibrary).GetField(
            fieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        return field?.GetValue(null) as SerializedCollection<T>;
    }
}
