using System;
using System.Linq;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.SkittlesLib;

public sealed partial class SkittlesSubmod : ISubmod
{
    public string Name => "Skittles - UI Themes";
    public string Tooltip => "Manages and applies ImGui theme configurations for UI customization.";

    private ThemeManager _themeManager = null!;
    private int _selectedThemeIndex;
    private readonly ImInputString _filterInput = new(256);
    private readonly ImInputString _themeNameInput = new(128);
    private bool _showSaveInput;
    private bool _editorVisible;
    private bool _pendingOpenDelete;

    public void Initialize()
    {
        _themeManager = new ThemeManager();
        _themeManager.Initialize();
        _selectedThemeIndex = FindThemeIndex(_themeManager.ActiveThemeName);
    }

    public void Update(double dt) { }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##sk_content");

        string active = _sceneThemeRestored ? "Saved scene style" : _themeManager.ActiveThemeName ?? "Game Default";
        string[] themeNames = _themeManager.GetThemeNames();
        string preview = (_selectedThemeIndex >= 0 && _selectedThemeIndex < themeNames.Length)
            ? themeNames[_selectedThemeIndex]
            : "Select Theme...";
        bool canDelete = _selectedThemeIndex >= 0
            && _selectedThemeIndex < themeNames.Length
            && !_themeManager.AvailableThemes[_selectedThemeIndex].IsBuiltIn;

        var tableFlags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##sk_selector", 2, tableFlags))
        {
            ImGui.TableSetupColumn("##sk_lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##sk_widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            // Active theme status row
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Active");
            ImGui.TableNextColumn();
            ImGui.TextColored(new float4(0.3f, 1.0f, 0.3f, 1.0f), active);

            // Theme selector row
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding(); ImGui.Text("Theme");
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            if (ImGui.BeginCombo("##sk_themedropdown", preview))
            {
                if (ImGui.IsWindowAppearing())
                    _filterInput.Clear();

                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint("##sk_filter", "filter..."u8, _filterInput);
                ImGui.Separator();

                string filterText = _filterInput.ToString();
                for (int i = 0; i < themeNames.Length; i++)
                {
                    if (!string.IsNullOrEmpty(filterText) &&
                        !themeNames[i].Contains(filterText, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool selected = _selectedThemeIndex == i;
                    if (ImGui.Selectable(themeNames[i], selected))
                    {
                        _selectedThemeIndex = i;
                        _themeManager.ApplyTheme(themeNames[i]);
                        _sceneThemeRestored = false;
                    }
                }
                ImGui.EndCombo();
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding

        // Deferred popup open at content area scope (outside table ID stack)
        if (_pendingOpenDelete)
        {
            ImGui.OpenPopup("##sk_confirm_delete");
            _pendingOpenDelete = false;
        }
        RenderDeleteConfirmPopup();

        ImGui.Spacing();
        if (ImGui.Button(" Open Theme Editor ##sk"))
            _editorVisible = true;
        ImGui.SameLine(0, 8);
        if (!canDelete) ImGui.BeginDisabled();
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(KSAColor.Xkcd.Scarlet));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(KSAColor.Xkcd.PaleGrey));
        if (ImGui.Button(" Delete ##sk"))
            _pendingOpenDelete = true;
        ImGui.PopStyleColor();
        ImGui.PopStyleColor();
        if (!canDelete) ImGui.EndDisabled();

        SubmodUI.EndContentArea();
    }

    public void RenderFloatingWindows()
    {
        if (_editorVisible)
            RenderEditorWindow();
    }

    private void RenderDeleteConfirmPopup()
    {
        string[] themeNames = _themeManager.GetThemeNames();
        bool canDelete = _selectedThemeIndex >= 0 && _selectedThemeIndex < themeNames.Length
            && !_themeManager.AvailableThemes[_selectedThemeIndex].IsBuiltIn;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(20f, 16f));
        bool open = true;
        bool began = ImGui.BeginPopupModal("##sk_confirm_delete", ref open, ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleVar();
        if (!began)
            return;

        if (canDelete)
        {
            string deleteName = themeNames[_selectedThemeIndex];
            ImGui.Text($"Delete theme '{deleteName}'?");
            ImGui.Spacing();
            if (ImGui.Button(" Yes, Delete ##sk"))
            {
                _themeManager.DeleteTheme(deleteName);
                _selectedThemeIndex = FindThemeIndex(_themeManager.ActiveThemeName);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine(0, 8);
            if (ImGui.Button(" Cancel ##sk"))
                ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void RenderEditorWindow()
    {
        ImGui.SetNextWindowSize(new float2(700, 800), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Skittles \u2014 Theme Editor###sk_editor", ref _editorVisible))
        {

            ThemeEntry? activeEntry = _themeManager.AvailableThemes
                .FirstOrDefault(t => t.Name == _themeManager.ActiveThemeName && !t.IsBuiltIn);
            bool isCustom = activeEntry is not null;

            if (!_showSaveInput)
            {
                if (isCustom)
                {
                    if (ImGui.Button($"Save \"{activeEntry!.Name}\"##sk"))
                    {
                        _themeManager.SaveCurrentAsTheme(activeEntry.Name);
                        Console.WriteLine($"unscience/skittles: Saved theme '{activeEntry.Name}'");
                        UpdateSelectedIndex();
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Save as New...##sk"))
                    {
                        _showSaveInput = true;
                        _themeNameInput.Clear();
                    }
                }
                else
                {
                    if (ImGui.Button("Save as New Theme...##sk"))
                    {
                        _showSaveInput = true;
                        _themeNameInput.Clear();
                    }
                }
            }
            else
            {
                ImGui.Text("Theme Name:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(250);
                ImGui.InputText("##sk_themename", _themeNameInput);
                ImGui.SameLine();

                string nameStr = _themeNameInput.ToString().Trim();
                bool nameValid = !string.IsNullOrWhiteSpace(nameStr);

                if (!nameValid) ImGui.BeginDisabled();
                if (ImGui.Button("Save##sk_save"))
                {
                    _themeManager.SaveCurrentAsTheme(nameStr);
                    Console.WriteLine($"unscience/skittles: Saved theme '{nameStr}'");
                    _showSaveInput = false;
                    _themeNameInput.Clear();
                    UpdateSelectedIndex();
                }
                if (!nameValid) ImGui.EndDisabled();

                ImGui.SameLine();
                if (ImGui.Button("Cancel##sk_cancel"))
                {
                    _showSaveInput = false;
                    _themeNameInput.Clear();
                }
            }

            ImGui.Separator();
            ImGui.ShowStyleEditor();
        }
        ImGui.End();
    }

    public void Dispose()
    {
        _themeManager?.RestoreDefaults();
    }

    private void UpdateSelectedIndex()
    {
        string[] names = _themeManager.GetThemeNames();
        string? activeName = _themeManager.ActiveThemeName;
        for (int i = 0; i < names.Length; i++)
        {
            if (names[i] == activeName)
            {
                _selectedThemeIndex = i;
                return;
            }
        }
        _selectedThemeIndex = 0;
    }

    private int FindThemeIndex(string? themeName)
    {
        if (themeName == null) return 0;
        string[] names = _themeManager.GetThemeNames();
        for (int i = 0; i < names.Length; i++)
            if (names[i] == themeName) return i;
        return 0;
    }
}
