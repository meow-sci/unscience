using System;
using System.Collections.Generic;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using MeowSci.KsaAbstractions;

namespace MeowSci.HumbleArteestLib;

/// <summary>
/// ISubmod implementation for the Kitten Coloring feature.
/// Provides an ImGui panel to tint kitten character models per-material by modifying
/// MaterialData.AlbedoColor in the GPU material buffer.
/// </summary>
public sealed partial class KittenColorSubmod : ISubmod
{
    public string Name => "Kitten Color";
    public string Tooltip => "Tints kitten character models and can hide their visor glass.";
    internal const string HeaderTooltip =
        "Tints character materials through the GPU material buffer.\n" +
        "Alpha < 0.1 hides surfaces using ModelPbr.frag.\n\n" +
        "Visor glass ignores material alpha; use Hide visor glass.\n" +
        "The visor toggle affects all kittens for this session.";

    // Active toggle
    private bool _active;

    // Global controls
    private float4 _globalColor = new float4(1f, 1f, 1f, 1f);
    private bool _applyToAll = true;

    // Per-material state
    private List<MaterialEntry> _materialEntries = new();
    private ImGuiTextFilter _matFilter = new();

    private string? _statusMessage;
    private bool _statusIsError;

    public void Initialize() { }
    public void Update(double dt) { }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##kc_content");

        bool headerOpen = ImGui.CollapsingHeader("Kitten Color (?)", ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.SetItemTooltip(HeaderTooltip);
        if (!headerOpen)
        {
            SubmodUI.EndContentArea();
            return;
        }

        RenderBody();

        SubmodUI.EndContentArea();
    }

    internal void RenderBody()
    {
        bool prevActive = _active;
        ImGui.Checkbox("Active##kc_active", ref _active);
        if (!_active)
        {
            KittenVisorPatches.Hidden = false;
            if (prevActive && KittenColor.IsInitialized)
            {
                KittenColor.ResetAll();
                foreach (var e in _materialEntries) e.Enabled = false;
            }
            return;
        }

        ImGui.Spacing();
        RenderVisorControl();
        RenderInitOrControls();
        RenderStatusMessage();
    }

    public void Dispose()
    {
        KittenVisorPatches.Hidden = false;
        if (KittenColor.IsInitialized)
            KittenColor.ResetAll();
        KittenColor.Cleanup();
    }

    // ---- Main rendering ----

    private static void RenderVisorControl()
    {
        ImGui.BeginDisabled(!KittenVisorPatches.IsApplied);
        if (ImGui.Button(KittenVisorPatches.Hidden ? "Show visor glass##kc_visor" : "Hide visor glass##kc_visor"))
            KittenVisorPatches.Hidden = !KittenVisorPatches.Hidden;
        ImGui.EndDisabled();
        ImGui.SetItemTooltip("Hides visor glass on all kittens, including newly spawned kittens.\n" +
            "Show restores the normal visor draw. Helmet and material colors are unchanged.");
        ImGui.SameLine();
        ImGui.TextDisabled(KittenVisorPatches.Hidden ? "Visors hidden" : "Visors shown");
        if (!KittenVisorPatches.IsApplied)
            ImGui.TextWrapped(KittenVisorPatches.LastError ?? "Visor toggle patch is not installed.");
        ImGui.Spacing();
    }

    private void RenderInitOrControls()
    {
        if (!KittenColor.IsInitialized)
        {
            if (ImGui.Button(" Initialize "))
            {
                if (KittenColor.Initialize())
                {
                    RebuildEntries();
                    SetStatus($"Ready — {_materialEntries.Count} materials found.", false);
                }
                else
                    SetStatus(KittenColor.LastError ?? "Initialization failed.", true);
            }
            ImGui.SameLine(0, 12);
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("Discovers GPU material system via reflection.");
            return;
        }

        RenderControls();
        if (!_applyToAll)
        {
            ImGui.Spacing();
            RenderMaterialTable();
        }
    }

    // ---- Controls ----

    private void RenderControls()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        var flags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        if (ImGui.BeginTable("##kc_controls", 2, flags))
        {
            ImGui.TableSetupColumn("##kc_lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##kc_widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            // Mode
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Mode");
            ImGui.TableNextColumn();
            bool prevApplyToAll = _applyToAll;
            ImGui.Checkbox("Apply to All##kc", ref _applyToAll);
            if (_applyToAll && !prevApplyToAll)
                KittenColor.ApplyToAll(_globalColor);

            // Color picker with alpha
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Color");
            ImGui.TableNextColumn();
            if (ImGui.ColorEdit4("##kc_color", ref _globalColor,
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
                OnGlobalColorChanged();
            ImGui.SameLine(0, 8);
            if (ImGui.Button(" Reset "))
            {
                _globalColor = new float4(1f, 1f, 1f, 1f);
                KittenColor.ResetAll();
                foreach (var e in _materialEntries)
                {
                    e.Color = _globalColor;
                    e.Enabled = false;
                }
                SetStatus("Colors reset to default.", false);
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding
    }

    private void OnGlobalColorChanged()
    {
        if (_applyToAll)
        {
            KittenColor.ApplyToAll(_globalColor);
            return;
        }
        // Propagate global color to all entries and apply to enabled ones
        foreach (var e in _materialEntries)
        {
            e.Color = _globalColor;
            if (e.Enabled)
                KittenColor.ApplyToMaterial(e.Handle, e.Color);
        }
    }

    // ---- Per-material table ----

    private void RenderMaterialTable()
    {
        if (_materialEntries.Count == 0)
        {
            ImGui.TextDisabled("No materials found.");
            return;
        }

        // Toolbar: All / None / Refresh / filter
        if (ImGui.Button(" All ##kc"))
        {
            foreach (var e in _materialEntries)
            {
                if (!_matFilter.PassFilter(e.Name)) continue;
                e.Enabled = true;
                KittenColor.ApplyToMaterial(e.Handle, e.Color);
            }
        }
        ImGui.SameLine(0, 4);
        if (ImGui.Button(" None ##kc"))
        {
            foreach (var e in _materialEntries)
            {
                if (!_matFilter.PassFilter(e.Name)) continue;
                e.Enabled = false;
                KittenColor.ResetMaterial(e.Handle);
            }
        }
        ImGui.SameLine(0, 8);
        if (ImGui.Button(" Refresh "))
        {
            KittenColor.RefreshMaterialCache();
            RebuildEntries();
            SetStatus($"Refreshed — {_materialEntries.Count} materials.", false);
        }
        ImGui.SameLine(0, 12);
        ImGui.SetNextItemWidth(-1f);
        _matFilter.Draw("##kc_filter");

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 4f));
        var tableFlags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX
                       | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH
                       | ImGuiTableFlags.ScrollY;

        float maxHeight = ImGui.GetTextLineHeightWithSpacing() * 12;
        if (ImGui.BeginTable("##kc_mats", 3, tableFlags, new float2(0, maxHeight)))
        {
            ImGui.TableSetupColumn("##chk", ImGuiTableColumnFlags.WidthFixed, 38f);
            ImGui.TableSetupColumn("##clr", ImGuiTableColumnFlags.WidthFixed, 50f);
            ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            for (int i = 0; i < _materialEntries.Count; i++)
            {
                var entry = _materialEntries[i];
                if (!_matFilter.PassFilter(entry.Name)) continue;
                ImGui.PushID(i);

                ImGui.TableNextRow();

                // Checkbox column
                ImGui.TableNextColumn();
                bool enabled = entry.Enabled;
                if (ImGui.Checkbox("##en", ref enabled))
                {
                    entry.Enabled = enabled;
                    if (enabled)
                        KittenColor.ApplyToMaterial(entry.Handle, entry.Color);
                    else
                        KittenColor.ResetMaterial(entry.Handle);
                }

                // Color picker column
                ImGui.TableNextColumn();
                var color = entry.Color;
                if (ImGui.ColorEdit4("##clr", ref color,
                    ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.AlphaBar))
                {
                    entry.Color = color;
                    if (entry.Enabled)
                        KittenColor.ApplyToMaterial(entry.Handle, color);
                }

                // Material name column
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(entry.Name);

                ImGui.PopID();
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding
    }

    // ---- Material entry cache ----

    private void RebuildEntries()
    {
        var newMaterials = KittenColor.GetMaterials();
        var existing = new Dictionary<string, MaterialEntry>();
        foreach (var e in _materialEntries)
            existing[e.Name] = e;

        var updated = new List<MaterialEntry>(newMaterials.Length);
        foreach (var (name, handle) in newMaterials)
        {
            if (existing.TryGetValue(name, out var prev))
            {
                prev.Handle = handle;
                updated.Add(prev);
            }
            else
            {
                updated.Add(new MaterialEntry(name, handle, _globalColor));
            }
        }
        _materialEntries = updated;
    }

    // ---- Status ----

    private void RenderStatusMessage()
    {
        if (string.IsNullOrEmpty(_statusMessage)) return;
        ImGui.Spacing();
        if (_statusIsError)
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), _statusMessage);
        else
            ImGui.TextColored(new float4(0.4f, 1f, 0.4f, 1f), _statusMessage);
    }

    private void SetStatus(string msg, bool isError)
    {
        _statusMessage = msg;
        _statusIsError = isError;
    }

    // ---- Material entry ----

    private sealed class MaterialEntry
    {
        public string Name;
        public int Handle;
        public float4 Color;
        public bool Enabled;

        public MaterialEntry(string name, int handle, float4 defaultColor)
        {
            Name = name;
            Handle = handle;
            Color = defaultColor;
            Enabled = false;
        }
    }
}
