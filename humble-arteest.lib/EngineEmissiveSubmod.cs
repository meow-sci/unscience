using System.Collections.Generic;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.HumbleArteestLib;

/// <summary>
/// ISubmod implementation for the Engine Emissive feature.
/// Provides an ImGui panel to control Temperature and TFI overrides on dynamic engine parts.
/// </summary>
public sealed partial class EngineEmissiveSubmod : ISubmod
{
    public string Name => "Engine Emissive";
    public string Tooltip => "Overrides engine emissive temperature to control part glow intensity.";

    // Active toggle
    private bool _active;

    // Global controls
    private float _globalTemp;
    private float _globalTfi;
    private bool _applyToAll = true;

    // Per-engine entries
    private List<EngineEntry> _entries = new();
    private ImGuiTextFilter _engineFilter = new();

    private string? _statusMessage;
    private bool _statusIsError;

    public void Initialize() { }
    public void Update(double dt) { }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##ee_content");

        bool headerOpen = ImGui.CollapsingHeader("Engine Emissive (?)", ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.SetItemTooltip(
            "Overrides the Temperature field on dynamic engine parts to control\n" +
            "their emissive glow. Uses the game's existing per-instance Temperature\n" +
            "data path — no shader modifications needed.\n\n" +
            "Temperature drives the DynamicMeshIndirect fragment shader's emissive\n" +
            "color lookup table, making engines glow from cool to hot.");
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
        ImGui.Checkbox("Active##ee_active", ref _active);
        if (!_active)
        {
            if (prevActive)
                EngineEmissive.Cleanup();
            return;
        }

        ImGui.Spacing();
        RenderControls();
        RenderStatusMessage();
        if (!_applyToAll)
        {
            ImGui.Spacing();
            RenderEngineTable();
        }
    }

    public void Dispose()
    {
        EngineEmissive.Cleanup();
    }

    // ---- Controls ----

    private void RenderControls()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        var flags = ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX;
        if (ImGui.BeginTable("##ee_ctrl", 2, flags))
        {
            ImGui.TableSetupColumn("##ee_lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##ee_widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            // Mode
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Mode");
            ImGui.TableNextColumn();
            bool prevAll = _applyToAll;
            ImGui.Checkbox("Apply to all engines##ee", ref _applyToAll);
            if (_applyToAll && !prevAll)
            {
                // Clear per-engine overrides so global isn't shadowed by stale table entries
                EngineEmissive.ClearAll();
                foreach (var e in _entries) e.Enabled = false;
                EngineEmissive.GlobalEnabled = true;
                EngineEmissive.GlobalTemperature = _globalTemp;
                EngineEmissive.GlobalTfi = _globalTfi;
            }
            else if (!_applyToAll && prevAll)
            {
                EngineEmissive.GlobalEnabled = false;
            }

            // Temperature
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Temperature");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("##ee_temp", ref _globalTemp, 0f, 1f, "%.2f"))
            {
                if (_applyToAll)
                    EngineEmissive.GlobalTemperature = _globalTemp;
                else
                    PropagateGlobalToEntries();
            }

            // TFI
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("TFI");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("##ee_tfi", ref _globalTfi, 0f, 1f, "%.2f"))
            {
                if (_applyToAll)
                    EngineEmissive.GlobalTfi = _globalTfi;
                else
                    PropagateGlobalToEntries();
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding

        // Apply-to-all: continuously sync global state
        if (_applyToAll)
        {
            EngineEmissive.GlobalEnabled = true;
            EngineEmissive.GlobalTemperature = _globalTemp;
            EngineEmissive.GlobalTfi = _globalTfi;
        }
    }

    // ---- Per-engine table ----

    private void RenderEngineTable()
    {
        // Toolbar: All / None / Scan / filter
        if (ImGui.Button(" All ##ee"))
        {
            foreach (var e in _entries)
            {
                if (!_engineFilter.PassFilter(e.Label)) continue;
                e.Enabled = true;
                EngineEmissive.SetEngine(e.Model, e.Temp, e.Tfi);
            }
        }
        ImGui.SameLine(0, 4);
        if (ImGui.Button(" None ##ee"))
        {
            foreach (var e in _entries)
            {
                if (!_engineFilter.PassFilter(e.Label)) continue;
                e.Enabled = false;
                EngineEmissive.ClearEngine(e.Model);
            }
        }
        ImGui.SameLine(0, 8);
        if (ImGui.Button(" Scan "))
        {
            ScanEntries();
            SetStatus($"Found {_entries.Count} engine(s).", false);
        }
        ImGui.SameLine(0, 12);
        ImGui.SetNextItemWidth(-1f);
        _engineFilter.Draw("##ee_filter");

        ImGui.Spacing();

        if (_entries.Count == 0)
        {
            ImGui.TextDisabled("No engines found. Press Scan to discover dynamic parts.");
            return;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 4f));
        var tableFlags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX
                       | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH
                       | ImGuiTableFlags.ScrollY;

        float maxHeight = ImGui.GetTextLineHeightWithSpacing() * 12;
        if (ImGui.BeginTable("##ee_parts", 4, tableFlags, new float2(0, maxHeight)))
        {
            ImGui.TableSetupColumn("##chk", ImGuiTableColumnFlags.WidthFixed, 38f);
            ImGui.TableSetupColumn("Engine", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Temp", ImGuiTableColumnFlags.WidthFixed, 110f);
            ImGui.TableSetupColumn("TFI", ImGuiTableColumnFlags.WidthFixed, 110f);
            ImGui.TableHeadersRow();

            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (!_engineFilter.PassFilter(entry.Label)) continue;
                ImGui.PushID(i);

                ImGui.TableNextRow();

                // Checkbox
                ImGui.TableNextColumn();
                bool enabled = entry.Enabled;
                if (ImGui.Checkbox("##en", ref enabled))
                {
                    entry.Enabled = enabled;
                    if (enabled)
                        EngineEmissive.SetEngine(entry.Model, entry.Temp, entry.Tfi);
                    else
                        EngineEmissive.ClearEngine(entry.Model);
                }

                // Label
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(entry.Label);

                // Temp slider
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1f);
                float temp = entry.Temp;
                if (ImGui.SliderFloat("##t", ref temp, 0f, 1f, "%.2f"))
                {
                    entry.Temp = temp;
                    if (entry.Enabled)
                        EngineEmissive.SetEngine(entry.Model, entry.Temp, entry.Tfi);
                }

                // TFI slider
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1f);
                float tfi = entry.Tfi;
                if (ImGui.SliderFloat("##f", ref tfi, 0f, 1f, "%.2f"))
                {
                    entry.Tfi = tfi;
                    if (entry.Enabled)
                        EngineEmissive.SetEngine(entry.Model, entry.Temp, entry.Tfi);
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar(); // CellPadding
    }

    // ---- Helpers ----

    private void PropagateGlobalToEntries()
    {
        foreach (var e in _entries)
        {
            e.Temp = _globalTemp;
            e.Tfi = _globalTfi;
            if (e.Enabled)
                EngineEmissive.SetEngine(e.Model, e.Temp, e.Tfi);
        }
    }

    private void ScanEntries()
    {
        var scanned = EngineEmissive.ScanAllDynamicParts();
        var existing = new Dictionary<PartModelDynamic, EngineEntry>(ReferenceEqualityComparer.Instance);
        foreach (var e in _entries)
            existing[e.Model] = e;

        var updated = new List<EngineEntry>(scanned.Count);
        foreach (var (label, model) in scanned)
        {
            if (existing.TryGetValue(model, out var prev))
            {
                prev.Label = label;
                updated.Add(prev);
            }
            else
            {
                updated.Add(new EngineEntry(label, model, _globalTemp, _globalTfi));
            }
        }
        _entries = updated;
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

    // ---- Engine entry ----

    private sealed class EngineEntry
    {
        public string Label;
        public PartModelDynamic Model;
        public bool Enabled;
        public float Temp;
        public float Tfi;

        public EngineEntry(string label, PartModelDynamic model, float defaultTemp, float defaultTfi)
        {
            Label = label;
            Model = model;
            Enabled = false;
            Temp = defaultTemp;
            Tfi = defaultTfi;
        }
    }
}
