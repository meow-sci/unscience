using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MeowSci.KsaAbstractions;

/// <summary>A flat, copied, non-overwriting media catalog shared by multiple tools.</summary>
public sealed class SharedFileLibrary
{
    private readonly HashSet<string> _extensions;
    public string DirectoryPath { get; }
    public string Label { get; }
    public string FormatDescription => string.Join(", ", _extensions);

    public SharedFileLibrary(string directory, string label, string[] extensions, string? dataRoot = null)
    {
        DirectoryPath = Path.Combine(dataRoot ?? KsaPaths.ModDataDir, directory);
        Label = label;
        _extensions = new(extensions, StringComparer.OrdinalIgnoreCase);
    }

    public void EnsureDir() => Directory.CreateDirectory(DirectoryPath);
    public bool Supports(string path) => _extensions.Contains(Path.GetExtension(path));

    public string FullPath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name is "." or "..")
            throw new ArgumentException("Choose a file from the shared library.", nameof(name));
        return Path.Combine(DirectoryPath, name);
    }

    public string[] Scan()
    {
        try
        {
            if (!Directory.Exists(DirectoryPath)) return Array.Empty<string>();
            return Directory.EnumerateFiles(DirectoryPath).Where(Supports)
                .Select(Path.GetFileName).OfType<string>().Where(n => !n.StartsWith('.'))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience/{Label}: scan failed: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    public string? Import(string sourcePath, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(sourcePath)) { error = "File not found."; return null; }
            if (!Supports(sourcePath)) { error = $"Supported files: {FormatDescription}."; return null; }
            EnsureDir();
            string source = Path.GetFullPath(sourcePath);
            string name = Path.GetFileName(source);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(source, Path.GetFullPath(FullPath(name)), comparison)) return name;
            string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
            for (int number = 2; File.Exists(FullPath(name)); number++) name = $"{stem} ({number}){extension}";
            File.Copy(source, FullPath(name), overwrite: false);
            return name;
        }
        catch (Exception ex) { error = $"Import failed: {ex.Message}"; return null; }
    }

    public string DefaultBrowseDir()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Directory.Exists(home) ? home : Directory.GetCurrentDirectory();
    }

    public IReadOnlyList<(string Label, string Path)> QuickLinks()
    {
        var links = new List<(string, string)>();
        void Add(string label, string path) { if (Directory.Exists(path)) links.Add((label,path)); }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add("Home", home);
        Add("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        Add("Downloads", Path.Combine(home, "Downloads"));
        Add("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        Add($"{Label} Library", DirectoryPath);
        if (OperatingSystem.IsWindows())
            try { foreach (var drive in DriveInfo.GetDrives()) if (drive.IsReady) Add(drive.Name, drive.RootDirectory.FullName); }
            catch (IOException) { }
        return links;
    }
}
