using System;
using System.IO;
using MeowSci.KsaAbstractions;
using MeowSci.PebblesLib;

internal static class SharedGlbChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "unscience-glb-copy-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);
            var files = new SharedFileLibrary("glbs", "GLB", new[] { ".glb" }, root);
            string original = Path.Combine(root, "model.GLB");
            File.WriteAllBytes(original, GlbChecks.Fixture());
            string name = files.Import(original, out var error) ?? throw new Exception(error);
            using var before = GlbDocument.Load(original);
            File.Delete(original);
            using var copied = GlbDocument.Load(files.FullPath(name));
            Check(copied.Hash == before.Hash && copied.ReadScene().Count > 0, "Copied GLB retains exact geometry/content after original deletion");
            var frozen = new GlbIdentity(files.FullPath(name), copied.Hash, "");
            Check(GlbIdentity.Parse(frozen.MeshId(-1)).Path == files.FullPath(name), "Recipe points at managed copy");
            File.WriteAllBytes(files.FullPath(name), GlbChecks.Fixture(json => json["extras"] = "changed"));
            using var changed = GlbDocument.Load(files.FullPath(name));
            Check(changed.Hash != frozen.Hash, "Changed catalog contents cannot match frozen recipe version");
            string lazy = GlbLibrary.SelectionId(name);
            Check(GlbLibrary.IsSelection(lazy) && GlbLibrary.FileName(lazy) == name && GlbLibrary.Label(lazy).Contains(name), "Library picker ids retain filename without hashing/decoding");
            try { GlbLibrary.FileName(GlbLibrary.SelectionId("../escape.glb")); throw new Exception("Traversal allowed"); } catch (ArgumentException) { }
            try { GlbIdentity.Parse(lazy); throw new Exception("Lazy file accepted as frozen identity"); } catch (InvalidDataException) { }
            var small = new SharedFileLibrary("small", "GLB", new[] { ".glb" }, root, maxFileBytes: 1);
            Check(small.Import(files.FullPath(name),out error)==null && error!=null && small.Scan().Length==0, "Reject oversized file before copy");
            Console.WriteLine("PASS: shared GLB copied content, stable paths, version changes, lazy choices and size limit");
        }
        finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
    private static void Check(bool value, string message) { if(!value) throw new Exception(message); }
}
namespace MeowSci.KsaAbstractions
{
    internal static class KsaPaths
    {
        // Only token/path helpers use this facade; actual IO checks pass an isolated root above.
        public static string ModDataDir => Path.Combine(Path.GetTempPath(), "unscience-glb-id-checks");
    }
}
