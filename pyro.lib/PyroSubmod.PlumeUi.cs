using System;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using KSA;

namespace MeowSci.PyroLib;

/// <summary>Per-plume panels: quick toggle, template, throttle, offsets, nozzle physics and look overrides.</summary>
public sealed partial class PyroSubmod
{
    private readonly ImInputString _plumeTemplateFilter = new(128);

    private void RenderBulkToggles()
    {
        if (ImGui.Button(" All On ##pyro_all_on")) SetAllEnabled(true);
        ImGui.SameLine(0, 8);
        if (ImGui.Button(" All Off ##pyro_all_off")) SetAllEnabled(false);
        ImGui.Spacing();
    }

    private void RenderPlumeSection(PlumeEntry plume, int index, ref PlumeEntry? toRemove)
    {
        string state = plume.EffectiveEnabled ? "ON" : "off";
        if (plume.Cycle.Running) state += " / cycling";
        string header = $"Plume #{plume.Id} [{state}]: {plume.Vehicle.Id} / {plume.Part.Id}  ({plume.TemplateId})##pyro_plume_{plume.Id}";
        if (!ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
            return;

        var wpadX = ImGui.GetStyle().WindowPadding.X;
        float childW = ImGui.GetContentRegionAvail().X + wpadX * 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - wpadX);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new float2(20f, 10f));
        ImGui.BeginChild($"pyro_child_{plume.Id}", new float2(childW, 0),
            ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysUseWindowPadding,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar();

        string id = $"##pyro_p{plume.Id}";

        // Quick toggle row
        bool enabled = plume.Enabled;
        if (ImGui.Checkbox($"Enabled{id}_enabled", ref enabled)) SetEnabled(plume, enabled);
        ImGui.SameLine(0, 12);
        if (ImGui.Button(plume.Enabled ? $" Off {id}_toggle" : $" On {id}_toggle"))
            SetEnabled(plume, !plume.Enabled);
        ImGui.SameLine(0, 12);
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled($"{plume.Vehicle.Id} / {PyroUi.PartLabel(plume.Part)}");

        ImGui.Spacing();
        RenderCycle(plume, id);
        RenderTemplateAndThrottle(plume, id);

        ImGui.Spacing();
        PyroUi.OffsetFields(id, ref plume.Position, ref plume.Rotation);

        ImGui.Spacing();
        if (ImGui.TreeNodeEx($"Nozzle physics{id}_nozzle", ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            RenderNozzleFields(plume.Nozzle, id);
            ImGui.TreePop();
        }
        if (ImGui.TreeNodeEx($"Look{id}_look", ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            RenderLookFields(plume, id);
            ImGui.TreePop();
        }

        if (!string.IsNullOrEmpty(plume.LastError))
        {
            ImGui.Spacing();
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), plume.LastError);
        }

        ImGui.Spacing();
        if (ImGui.Button($" Save settings as preset... {id}_savepreset"))
            OpenSavePresetModal(plume);
        ImGui.SameLine(0, 8);
        PyroUi.DangerButtonBegin();
        if (ImGui.Button($" Remove {id}_remove"))
            toRemove = plume;
        PyroUi.DangerButtonEnd();

        ImGui.Spacing();
        ImGui.EndChild();
    }

    private static void RenderCycle(PlumeEntry plume, string id)
    {
        bool running = plume.Cycle.Running;
        if (ImGui.Checkbox($"Repeat On / Off{id}_cycle", ref running))
        {
            if (running) { plume.Enabled = true; plume.Cycle.Restart(Universe.GetElapsedSeconds()); }
            else plume.Cycle.Stop();
        }
        if (PyroUi.BeginParamGrid($"{id}_cycle_times"))
        {
            bool changed = PyroUi.GridDrag("On (s)", $"{id}_cycle_on", ref plume.Cycle.OnSeconds, .05f, .05f, 3600, "%.2f");
            changed |= PyroUi.GridDrag("Off (s)", $"{id}_cycle_off", ref plume.Cycle.OffSeconds, .05f, .05f, 3600, "%.2f");
            PyroUi.EndParamGrid();
            if (changed && plume.Cycle.Running) plume.Cycle.Restart(Universe.GetElapsedSeconds());
        }
        if (plume.Cycle.Running)
        {
            if (ImGui.SmallButton($"Restart cycle{id}_restart")) plume.Cycle.Restart(Universe.GetElapsedSeconds());
            ImGui.SameLine();
            ImGui.TextDisabled($"{(plume.Cycle.IsOn ? "On" : "Off")} — {plume.Cycle.RemainingSeconds:0.00}s remaining");
        }
        ImGui.TextWrapped("Cycle starts On and uses simulation seconds (pauses with the game). Editing durations restarts it. Disabling the cycle returns to Enabled; manual On/Off cancels cycling. Runtime only, not saved in presets.");
        ImGui.Spacing();
    }

    private void RenderTemplateAndThrottle(PlumeEntry plume, string id)
    {
        var templateIds = PlumeTemplates.GetTemplateIds();
        int templateIndex = Array.IndexOf(templateIds, plume.TemplateId);

        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable($"{id}_tt", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("##lbl", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget", ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Template");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            int newIndex = templateIndex;
            PyroUi.FilteredCombo($"{id}_template", templateIds, ref newIndex, _plumeTemplateFilter);
            if (newIndex != templateIndex && newIndex >= 0)
                SetTemplate(plume, templateIds[newIndex]);

            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Throttle");
            ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
            ImGui.SliderFloat($"{id}_throttle", ref plume.Throttle, 0f, 1f, "%.2f");

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private static void RenderNozzleFields(NozzleSettings n, string id)
    {
        if (!PyroUi.BeginParamGrid($"{id}_nozzle_grid")) return;
        ImGui.TableNextRow();
        PyroUi.GridDrag("Exit radius (m)", $"{id}_exit", ref n.ExitRadius, 0.005f, 0.01f, 20f);
        PyroUi.GridDrag("Throat radius (m)", $"{id}_throat", ref n.ThroatRadius, 0.002f, 0.001f, 20f);
        ImGui.TableNextRow();
        PyroUi.GridDrag("Chamber (bar)", $"{id}_pc", ref n.ChamberPressureBar, 0.5f, 0.1f, 500f, "%.1f");
        PyroUi.GridDrag("Chamber (K)", $"{id}_tc", ref n.ChamberTemperatureK, 10f, 100f, 6000f, "%.0f");
        ImGui.TableNextRow();
        PyroUi.GridDrag("Gamma", $"{id}_gamma", ref n.Gamma, 0.005f, 1.05f, 1.67f);
        PyroUi.GridDrag("Gas const (J/kgK)", $"{id}_r", ref n.GasConstant, 1f, 50f, 4200f, "%.0f");
        PyroUi.EndParamGrid();
        ImGui.TextDisabled("Exit/throat radius set the area ratio; chamber pressure & temperature set the exhaust speed and plume length.");
    }

    private static void RenderLookFields(PlumeEntry plume, string id)
    {
        if (!PlumeEmitter.PerPlumeLookAvailable)
        {
            ImGui.TextDisabled("Per-plume look overrides unavailable on this game build.");
            return;
        }
        if (!PyroUi.BeginParamGrid($"{id}_look_grid")) return;
        ImGui.TableNextRow();
        PyroUi.GridDrag("Density x", $"{id}_dens", ref plume.AbsorptionDensityScale, 0.01f, 0f, 20f, "%.2f");
        PyroUi.GridDrag("Refraction", $"{id}_refr", ref plume.RefractionIntensity, 0.01f, 0f, 10f, "%.2f");
        PyroUi.EndParamGrid();
        ImGui.TextDisabled("Colours, brightness and noise are shared per template — edit them in the Template Editor below.");
    }
}
