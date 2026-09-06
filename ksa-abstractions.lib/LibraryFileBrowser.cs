using System;
using System.IO;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace MeowSci.KsaAbstractions;

/// <summary>
/// Reusable ImGui filesystem browser for a shared media catalog. Picking a file always copies it
/// through the configured library before notifying the consumer of its catalog file name.
/// </summary>
public sealed class LibraryFileBrowser
{
    private readonly SharedFileLibrary _library;
    private readonly string _id;
    private readonly string _windowTitle;
    private readonly ImInputString _filter = new(128);
    private bool _open;
    private string _currentDir = "";
    private string? _selectedFile;
    private string? _error;
    private string[] _dirs = Array.Empty<string>();
    private string[] _files = Array.Empty<string>();
    private string _listedDir = "";

    public LibraryFileBrowser(SharedFileLibrary library, string id, string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A stable ImGui id is required.", nameof(id));
        _library = library;
        _id = id;
        _windowTitle = windowTitle;
    }

    public bool Visible => _open;

    public void Open()
    {
        _error = null;
        try { _library.EnsureDir(); }
        catch (Exception ex) { _error = ex.Message; }
        _open = true;
        _selectedFile = null;
        if (string.IsNullOrEmpty(_currentDir) || !Directory.Exists(_currentDir))
            _currentDir = _library.DefaultBrowseDir();
        _listedDir = "";
    }

    /// <summary>Renders the browser and returns the imported catalog file name on success.</summary>
    public void Render(Action<string> onImported)
    {
        if (!_open)
            return;

        RefreshListing();
        ImGui.SetNextWindowSize(new float2(620, 480), ImGuiCond.FirstUseEver);
        if (ImGui.Begin($"{_windowTitle}###media_browser_{_id}", ref _open))
        {
            RenderQuickLinks();
            RenderPathRow();

            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint($"##media_browser_filter_{_id}", "filter..."u8, _filter);
            var filterText = _filter.ToString().Trim();

            var footerHeight = ImGui.GetFrameHeightWithSpacing() * 2f;
            ImGui.BeginChild($"##media_browser_list_{_id}", new float2(0, -footerHeight),
                ImGuiChildFlags.Borders);
            RenderEntries(filterText, onImported);
            ImGui.EndChild();

            RenderFooter(onImported);
        }
        ImGui.End();
    }

    private void RenderQuickLinks()
    {
        var first = true;
        foreach (var (label, path) in _library.QuickLinks())
        {
            if (!first) ImGui.SameLine(0, 6);
            first = false;
            if (ImGui.Button($" {label} ##media_ql_{_id}_{label}"))
                Navigate(path);
        }
        ImGui.Spacing();
    }

    private void RenderPathRow()
    {
        var parent = Directory.GetParent(_currentDir)?.FullName;
        if (parent == null) ImGui.BeginDisabled();
        if (ImGui.Button($" Up ##media_browser_up_{_id}") && parent != null)
            Navigate(parent);
        if (parent == null) ImGui.EndDisabled();
        ImGui.SameLine(0, 8);
        if (ImGui.Button($"Refresh##media_refresh_{_id}")) _listedDir = "";
        ImGui.TextDisabled(_currentDir);
    }

    private void RenderEntries(string filterText, Action<string> onImported)
    {
        foreach (var dir in _dirs)
        {
            if (!Matches(dir, filterText)) continue;
            if (ImGui.Selectable($"[dir]  {dir}##media_d_{_id}_{dir}", false,
                    ImGuiSelectableFlags.AllowDoubleClick)
                && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                Navigate(Path.Combine(_currentDir, dir));
                return;
            }
        }

        foreach (var file in _files)
        {
            if (!Matches(file, filterText)) continue;
            var selected = _selectedFile == file;
            if (ImGui.Selectable($"{file}##media_f_{_id}_{file}", selected,
                    ImGuiSelectableFlags.AllowDoubleClick))
            {
                _selectedFile = file;
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    Pick(onImported);
                    return;
                }
            }
        }

        if (_dirs.Length == 0 && _files.Length == 0)
            ImGui.TextDisabled($"No folders or {_library.FormatDescription} files here.");
    }

    private void RenderFooter(Action<string> onImported)
    {
        var canImport = _selectedFile != null;
        if (!canImport) ImGui.BeginDisabled();
        if (ImGui.Button($" Import ##media_browser_import_{_id}"))
            Pick(onImported);
        if (!canImport) ImGui.EndDisabled();
        ImGui.SameLine(0, 8);
        if (ImGui.Button($" Cancel ##media_browser_cancel_{_id}"))
            _open = false;
        ImGui.SameLine(0, 12);
        ImGui.AlignTextToFramePadding();
        if (!string.IsNullOrEmpty(_error))
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), _error);
        else
            ImGui.TextDisabled(_selectedFile ?? $"Select a {_library.FormatDescription} file");
    }

    private void Pick(Action<string> onImported)
    {
        if (_selectedFile == null)
            return;
        var importedName = _library.Import(Path.Combine(_currentDir, _selectedFile), out _error);
        if (importedName == null)
            return;
        onImported(importedName);
        _open = false;
    }

    private void Navigate(string dir)
    {
        _currentDir = dir;
        _selectedFile = null;
        _error = null;
        _filter.Clear();
        _listedDir = "";
    }

    private void RefreshListing()
    {
        if (_listedDir == _currentDir)
            return;
        _listedDir = _currentDir;
        _dirs = Array.Empty<string>();
        _files = Array.Empty<string>();
        try
        {
            _dirs = Directory.EnumerateDirectories(_currentDir)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !name.StartsWith('.'))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _files = Directory.EnumerateFiles(_currentDir)
                .Where(_library.Supports)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !name.StartsWith('.'))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _error = null;
        }
        catch (Exception ex)
        {
            _error = ex is UnauthorizedAccessException ? "Access denied." : ex.Message;
        }
    }

    private static bool Matches(string name, string filterText)
        => filterText.Length == 0 || name.Contains(filterText, StringComparison.OrdinalIgnoreCase);
}
