using System;
namespace MeowSci.KsaAbstractions;

/// <summary>Common persistent GLBs. Picker ids are lazy file choices, never frozen asset versions.</summary>
public static class GlbLibrary
{
    private const string Prefix = "unscience-glb-file:";
    public static SharedFileLibrary Files { get; } = new("glbs", "GLB", new[] { ".glb" }, maxFileBytes: 128L * 1024 * 1024);
    public static string SelectionId(string name)
    {
        _ = Files.FullPath(name);
        return Prefix + name;
    }
    public static bool IsSelection(string id) => id.StartsWith(Prefix, StringComparison.Ordinal);
    public static string FileName(string id)
    {
        if (!IsSelection(id)) throw new ArgumentException("Not a shared GLB file selection.", nameof(id));
        string name = id[Prefix.Length..];
        _ = Files.FullPath(name);
        if (!Files.Supports(name)) throw new ArgumentException("Choose a GLB file.", nameof(id));
        return name;
    }
    public static string Label(string id) => FileName(id) + " · GLB library";
}
