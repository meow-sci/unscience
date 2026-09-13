using System;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using KSA;

namespace MeowSci.PyroLib;

/// <summary>
/// Shared exhaust-template editor — the same knobs as the game's hidden "Volumetric Exhausts" debug window
/// (View → Show Exhaust Debug). Edits apply to every plume AND every real engine using that template.
/// </summary>
public sealed partial class PyroSubmod
{
    private int _editorTemplateIndex = 0;
    private readonly ImInputString _editorTemplateFilter = new(128);

    private void RenderTemplateEditorSection()
    {
        bool open = ImGui.CollapsingHeader("Template Editor (?)", ImGuiTreeNodeFlags.None);
        ImGui.SetItemTooltip("Edit the game's shared exhaust templates (colours, brightness, noise, quality).\nChanges affect every plume and every real engine that uses the template,\nand last until the game restarts.");
        if (!open) return;

        var templateIds = PlumeTemplates.GetTemplateIds();
        if (templateIds.Length == 0)
        {
            ImGui.TextDisabled("No exhaust templates registered.");
            return;
        }
        if (_editorTemplateIndex >= templateIds.Length) _editorTemplateIndex = 0;

        ImGui.SetNextItemWidth(-1f);
        PyroUi.FilteredCombo("##pyro_editor_template", templateIds, ref _editorTemplateIndex, _editorTemplateFilter);
        var template = PlumeTemplates.Get(templateIds[_editorTemplateIndex]);
        if (template == null)
        {
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), "Template not found.");
            return;
        }

        bool changed = false;
        var original = SavedPlumeTemplate.Capture(template);
        string id = $"##pyro_te_{template.Id}";

        if (ImGui.TreeNodeEx($"Absorption{id}_abs", ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            changed |= RenderAbsorption(template.Absorption, id);
            ImGui.TreePop();
        }
        if (ImGui.TreeNodeEx($"Emission{id}_emi", ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            changed |= RenderEmission(template.Emission, id);
            ImGui.TreePop();
        }
        if (ImGui.TreeNodeEx($"Noise{id}_noise", ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            changed |= RenderNoise(template.Noise, id);
            ImGui.TreePop();
        }
        if (ImGui.TreeNodeEx($"Length weights{id}_lw", ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            changed |= RenderLengthWeights(template.LengthWeights, id);
            ImGui.TreePop();
        }
        if (ImGui.TreeNodeEx($"Quality{id}_q", ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            changed |= RenderQuality(template.Quality, id);
            ImGui.TreePop();
        }

        if (changed)
        {
            _originalTemplates.TryAdd(template.Id, original);
            TemplateRefresher.NotifyTemplateChanged(template, this);
        }
    }

    private static bool RenderAbsorption(Absorption a, string id)
    {
        bool changed = false;
        if (!PyroUi.BeginParamGrid($"{id}_abs_grid")) return false;
        ImGui.TableNextRow();
        changed |= PyroUi.GridDragDouble("Density", $"{id}_density", ref a.Density.Value, 0.001f, 0.0001f, 1000000f, "%.4f");
        changed |= PyroUi.GridDragDouble("Scatter bright.", $"{id}_scat", ref a.ScatteringBrightness.Value, 0.05f, 0f, 100f, "%.2f");
        ImGui.TableNextRow();
        changed |= PyroUi.GridDragDouble("Phase ecc.", $"{id}_phase", ref a.ScatteringPhaseEccentricity.Value, 0.005f, -1f, 1f);
        changed |= PyroUi.GridDragDouble("Refraction", $"{id}_refr", ref a.RefractionIntensity.Value, 0.01f, 0f, 10f, "%.2f");
        ImGui.TableNextRow();
        ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Clean burn in atm");
        ImGui.TableNextColumn();
        bool clean = a.FakeCleanBurnInAtmosphere.Value;
        if (ImGui.Checkbox($"{id}_clean", ref clean)) { a.FakeCleanBurnInAtmosphere.Value = clean; changed = true; }
        ImGui.TableNextColumn(); ImGui.TableNextColumn();
        PyroUi.EndParamGrid();
        return changed;
    }

    private static bool RenderEmission(Emission e, string id)
    {
        bool changed = false;
        if (!PyroUi.BeginParamGrid($"{id}_emi_grid")) return false;
        ImGui.TableNextRow();
        changed |= PyroUi.GridDragDouble("Brightness", $"{id}_bright", ref e.Brightness.Value, 0.1f, 0f, 200f, "%.1f");
        ImGui.TableNextColumn(); ImGui.TableNextColumn();
        ImGui.TableNextRow();
        changed |= EditColor("Color 0", $"{id}_c0", ref e.ColorGradient.Color0);
        changed |= EditColor("Color 1", $"{id}_c1", ref e.ColorGradient.Color1);
        ImGui.TableNextRow();
        changed |= EditColor("Color 2", $"{id}_c2", ref e.ColorGradient.Color2);
        changed |= EditColor("Color 3", $"{id}_c3", ref e.ColorGradient.Color3);
        PyroUi.EndParamGrid();

        ImGui.TextDisabled("Mach diamonds");
        if (!PyroUi.BeginParamGrid($"{id}_md_grid")) return changed;
        var md = e.Flow.MachDiamonds;
        ImGui.TableNextRow();
        changed |= PyroUi.GridDragDouble("Lead in", $"{id}_mdin", ref md.LeadIn.Value, 0.005f, 0f, 1f);
        changed |= PyroUi.GridDragDouble("Lead out", $"{id}_mdout", ref md.LeadOut.Value, 0.005f, 0f, 1f);
        ImGui.TableNextRow();
        changed |= PyroUi.GridDragDouble("Middle radius", $"{id}_mdr", ref md.MiddleRadius.Value, 0.005f, 0f, 1f);
        ImGui.TableNextColumn(); ImGui.TableNextColumn();
        PyroUi.EndParamGrid();
        return changed;
    }

    private static bool EditColor(string label, string id, ref ColorRgbReference reference)
    {
        float3 color = reference.Value.AsFloat3;
        if (!PyroUi.GridColor(label, id, ref color)) return false;
        var replacement = new ColorRgbReference(color);
        replacement.OnDataLoad(new Mod());
        reference = replacement;
        return true;
    }

    private static bool RenderNoise(Noise n, string id)
    {
        bool changed = false;
        if (!PyroUi.BeginParamGrid($"{id}_noise_grid")) return false;
        ImGui.TableNextRow();
        changed |= PyroUi.GridDragDouble("Density size", $"{id}_dns", ref n.DensityNoise.Size.Value, 0.1f, 0f, 100f, "%.2f");
        changed |= PyroUi.GridDragDouble("Density strength", $"{id}_dni", ref n.DensityNoise.Intensity.Value, 0.005f, 0f, 2f);
        ImGui.TableNextRow();
        changed |= PyroUi.GridDragDouble("Shape size", $"{id}_sns", ref n.ShapeNoise.Size.Value, 0.1f, 0f, 100f, "%.2f");
        changed |= PyroUi.GridDragDouble("Shape strength", $"{id}_sni", ref n.ShapeNoise.Intensity.Value, 0.005f, 0f, 2f);
        ImGui.TableNextRow();
        changed |= PyroUi.GridDragDouble("Radial size", $"{id}_rns", ref n.RadialShapeNoise.Size.Value, 0.01f, 0f, 100f, "%.2f");
        changed |= PyroUi.GridDragDouble("Radial strength", $"{id}_rni", ref n.RadialShapeNoise.Intensity.Value, 0.005f, 0f, 2f);
        ImGui.TableNextRow();
        changed |= PyroUi.GridDragDouble("Radial speed", $"{id}_rnsp", ref n.RadialShapeNoise.Speed.Value, 0.1f, 0f, 100f, "%.1f");
        changed |= PyroUi.GridDragDouble("Barrel shock", $"{id}_rnb", ref n.RadialShapeNoise.BarrelShockIntensity.Value, 0.01f, 0f, 4f, "%.2f");
        PyroUi.EndParamGrid();
        return changed;
    }

    private static bool RenderLengthWeights(LengthWeights w, string id)
    {
        bool changed = false;
        if (!PyroUi.BeginParamGrid($"{id}_lw_grid")) return false;
        ImGui.TableNextRow();
        changed |= PyroUi.GridDragDouble("Radius", $"{id}_lwr", ref w.RadiusWeight.Value, 0.005f, 0.0001f, 100f);
        changed |= PyroUi.GridDragDouble("Nozzle pressure", $"{id}_lwp", ref w.NozzlePressureWeight.Value, 0.1f, 0.0001f, 100f, "%.1f");
        ImGui.TableNextRow();
        changed |= PyroUi.GridDragDouble("Jet expansion", $"{id}_lwj", ref w.JetExpansionWeight.Value, 0.5f, 0.0001f, 1000f, "%.1f");
        changed |= PyroUi.GridDragDouble("Exit Mach", $"{id}_lwm", ref w.ExitMachNumberWeight.Value, 0.01f, 0.0001f, 100f, "%.2f");
        PyroUi.EndParamGrid();
        return changed;
    }

    private static bool RenderQuality(Quality q, string id)
    {
        bool changed = false;
        if (!PyroUi.BeginParamGrid($"{id}_q_grid")) return false;
        ImGui.TableNextRow();
        int samples = (int)q.SampleCount.Value;
        ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Samples");
        ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderInt($"{id}_samples", ref samples, 1, 64)) { q.SampleCount.Value = samples; changed = true; }
        int shadowSamples = (int)q.SelfShadowSampleCount.Value;
        ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Self-shadow");
        ImGui.TableNextColumn(); ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderInt($"{id}_shadow", ref shadowSamples, 0, 10)) { q.SelfShadowSampleCount.Value = shadowSamples; changed = true; }
        ImGui.TableNextRow();
        ImGui.TableNextColumn(); ImGui.AlignTextToFramePadding(); ImGui.Text("Vessel shadows");
        ImGui.TableNextColumn();
        if (ImGui.Checkbox($"{id}_vshadow", ref q.VolumetricVesselShadows)) changed = true;
        ImGui.TableNextColumn(); ImGui.TableNextColumn();
        PyroUi.EndParamGrid();
        return changed;
    }
}
