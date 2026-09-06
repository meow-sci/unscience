using System;
using System.Collections.Generic;
namespace MeowSci.KsaAbstractions;

/// <summary>Compatibility facade for the common PNG catalog and copied imports.</summary>
public static class PngLibrary
{
    internal static readonly SharedFileLibrary Catalog = new("pngs", "PNG", new[] { ".png" });
    public static string PngsDir => Catalog.DirectoryPath;
    public static void EnsureDir()
    {
        try { Catalog.EnsureDir(); }
        catch (Exception ex) { Console.WriteLine($"unscience/pngs: {ex.Message}"); }
    }
    public static string FullPath(string name) => Catalog.FullPath(name);
    public static string[] Scan() => Catalog.Scan();
    public static string? Import(string sourcePath, out string? error) => Catalog.Import(sourcePath, out error);
    public static string DefaultBrowseDir() => Catalog.DefaultBrowseDir();
    public static IReadOnlyList<(string Label, string Path)> QuickLinks() => Catalog.QuickLinks();
}
