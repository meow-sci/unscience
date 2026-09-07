using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.ZippoLib;

public sealed partial class ZippoSubmod
{
    private DiscoRecipe _disco = new();
    private bool _discoAllLights;
    private string? _discoError;
    private readonly Dictionary<Part, DiscoLight> _discoLights = new(ReferenceEqualityComparer.Instance);

    private void RenderDisco(Vehicle? selectedVehicle, Part? selectedPart)
    {
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Disco Party Lights##zp_disco", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Checkbox("All lights on selected vehicle##zp_disco_all", ref _discoAllLights);
            ImGui.TextWrapped(
                "Run independent repeating color, mechanism, and spotlight-beam cycles. " +
                "Light switches must be on; unsupported channels are skipped safely.");

            if (BeginDiscoTable("##zp_disco_phase_jitter"))
            {
                DiscoFloatRow("Phase jitter", "##zp_disco_phase_jitter_seconds", ref _disco.PhaseJitter,
                    0f, 3600f, "%.2f s");
                ImGui.EndTable();
            }
            ImGui.TextDisabled(
                "Each light and channel gets a stable random offset up to this value. Set to 0 for sync.");

            ImGui.Checkbox("Animate color##zp_disco_color", ref _disco.Color);
            if (_disco.Color)
            {
                ImGui.Checkbox("Rainbow / random colors##zp_disco_random", ref _disco.RandomColors);
                if (!_disco.RandomColors) RenderDiscoPalette();
                RenderDiscoTiming("Color", _disco.ColorTiming);
            }

            ImGui.Spacing();
            ImGui.Checkbox("Animate actuation##zp_disco_act", ref _disco.Actuation);
            if (_disco.Actuation)
            {
                if (BeginDiscoTable("##zp_disco_actuation"))
                {
                    DiscoFloatRow("Actuation minimum", "##zp_disco_act_min", ref _disco.ActuationMin, 0f, 1f);
                    DiscoFloatRow("Actuation maximum", "##zp_disco_act_max", ref _disco.ActuationMax, _disco.ActuationMin, 1f);
                    ImGui.EndTable();
                }
                RenderDiscoTiming("Actuation", _disco.ActuationTiming);
            }

            ImGui.Spacing();
            ImGui.Checkbox("Animate beam spread (spotlights)##zp_disco_spread", ref _disco.Spread);
            if (_disco.Spread)
            {
                if (BeginDiscoTable("##zp_disco_spread_angles"))
                {
                    DiscoFloatRow("Start inner half-angle", "##zp_disco_inner_min", ref _disco.InnerMin, 0.1f, 89f, "%.1f deg");
                    DiscoFloatRow("Start outer half-angle", "##zp_disco_outer_min", ref _disco.OuterMin, _disco.InnerMin, 89f, "%.1f deg");
                    DiscoFloatRow("End inner half-angle", "##zp_disco_inner_max", ref _disco.InnerMax, 0.1f, 89f, "%.1f deg");
                    DiscoFloatRow("End outer half-angle", "##zp_disco_outer_max", ref _disco.OuterMax, _disco.InnerMax, 89f, "%.1f deg");
                    ImGui.EndTable();
                }
                RenderDiscoTiming("Spread", _disco.SpreadTiming);
            }

            var targets = _discoAllLights && selectedVehicle != null
                ? LightController.GetLightParts(selectedVehicle)
                : selectedPart != null ? new List<Part> { selectedPart } : new List<Part>();
            bool canStart = targets.Count > 0 && (_disco.Color || _disco.Actuation || _disco.Spread);
            ImGui.BeginDisabled(!canStart);
            string label = _discoAllLights ? " Start Disco on Vehicle ##zp_disco_start" : " Start Disco on Light ##zp_disco_start";
            if (ImGui.Button(label, new float2(-1f, 0f)))
            {
                try
                {
                    _disco.Validate();
                    StartDisco(targets);
                    _discoError = null;
                }
                catch (Exception ex)
                {
                    _discoError = ex.Message;
                    Console.WriteLine("zippo: Disco: " + ex);
                }
            }
            ImGui.EndDisabled();

            if (_discoError != null)
                ImGui.TextColored(new float4(1f, 0.4f, 0.4f, 1f), _discoError);
        }

        RenderActiveDiscoLights();
    }

    private void RenderDiscoPalette()
    {
        int remove = -1;
        for (int i = 0; i < _disco.Palette.Count; i++)
        {
            ImGui.PushID(i + 20000);
            var color = _disco.Palette[i];
            if (ImGui.ColorEdit3($"Color {i + 1}##zp_disco_palette", ref color))
                _disco.Palette[i] = color;
            if (_disco.Palette.Count > 1 && ImGui.Button("Remove color##zp_disco_remove", new float2(-1f, 0f)))
                remove = i;
            ImGui.PopID();
        }

        if (remove >= 0) _disco.Palette.RemoveAt(remove);
        ImGui.BeginDisabled(_disco.Palette.Count >= 32);
        if (ImGui.Button("Add color##zp_disco_add", new float2(-1f, 0f)))
            _disco.Palette.Add(new float3(1f, 1f, 1f));
        ImGui.EndDisabled();
    }

    private static void RenderDiscoTiming(string channel, DiscoTiming timing)
    {
        if (!BeginDiscoTable($"##zp_disco_{channel}_timing")) return;

        DiscoFloatRow("Transition", $"##zp_disco_{channel}_transition", ref timing.Transition, 0.01f, 3600f, "%.2f s");
        DiscoFloatRow("Hold", $"##zp_disco_{channel}_hold", ref timing.Hold, 0f, 3600f, "%.2f s");

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Easing");
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1f);
        ImGui.Combo($"##zp_disco_{channel}_easing", ref timing.Easing,
            new[] { "Linear", "Ease in", "Ease out", "Smooth in-out" }, 4);
        ImGui.EndTable();
    }

    private static bool BeginDiscoTable(string id)
    {
        bool open = ImGui.BeginTable(id, 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX);
        if (open)
        {
            ImGui.TableSetupColumn("##zp_disco_label", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##zp_disco_value", ImGuiTableColumnFlags.WidthStretch, 2f);
        }
        return open;
    }

    private static void DiscoFloatRow(
        string label, string id, ref float value, float min, float max, string format = "%.2f")
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1f);
        ImGui.DragFloat(id, ref value, 0.05f, min, max, format);
    }

    private void RenderActiveDiscoLights()
    {
        if (_discoLights.Count == 0) return;

        ImGui.Spacing();
        ImGui.SeparatorText("Active Disco Lights");
        foreach (var (part, live) in _discoLights.ToArray())
        {
            string status = !live.OwnsTemplates ? "template replaced externally"
                : live.Paused ? "paused" : "animating";
            string name = part.DisplayName ?? part.Id;
            if (!ImGui.CollapsingHeader($"{name} #{part.InstanceId} ({status})##zp_disco_live_{part.InstanceId}"))
                continue;

            ImGui.TextWrapped(
                $"Elapsed {live.Elapsed:F1}s | Color: {live.Recipe.Color} | " +
                $"Spread: {live.Recipe.Spread} ({live.SpotCount} spots) | " +
                $"Actuation drivers: {live.Actuators.Count}");
            if (live.Recipe.Actuation && live.Actuators.Count == 0)
            {
                ImGui.TextDisabled(
                    "No actuator is owned by this light: it is unsupported or shared with another Disco light.");
            }

            ImGui.Checkbox($"Paused##zp_disco_pause_{part.InstanceId}", ref live.Paused);
            if (live.HasLightSwitch)
            {
                bool enabled = live.IsEnabled;
                if (ImGui.Checkbox($"Light switch##zp_disco_switch_{part.InstanceId}", ref enabled))
                    live.SetEnabled(enabled);
            }
            if (ImGui.Button($"Copy recipe to controls##zp_disco_copy_{part.InstanceId}", new float2(-1f, 0f)))
                _disco = live.Recipe.Clone();
            if (ImGui.Button($"Stop Disco and restore light##zp_disco_stop_{part.InstanceId}", new float2(-1f, 0f)))
                StopDisco(part);
        }

        if (_discoLights.Count > 1 && ImGui.Button("Stop all Disco lights##zp_disco_stop_all", new float2(-1f, 0f)))
            StopAllDisco();
    }

    private void StartDisco(IEnumerable<Part> targets)
    {
        var claimed = new HashSet<KeyframeAnimationModule>();
        foreach (var part in targets)
        {
            StopDisco(part);
            _animationManager.CancelAll(PartKey(part));

            var live = new DiscoLight(part, _disco);
            _discoLights[part] = live;
            if (_disco.Actuation)
            {
                // One animation module moves a complete assembly, so exactly one Disco light owns it.
                foreach (var module in part.FullPart.Modules.Get<KeyframeAnimationModule>())
                {
                    if (module.Shared.Duration <= 0f || !module.Shared.PartLookup.ContainsKey(part.Id)
                        || !claimed.Add(module))
                    {
                        continue;
                    }

                    foreach (var other in _discoLights.Values) other.ReleaseActuator(module);
                    live.AddActuator(module);
                }
            }
            live.Update(0d);
        }
    }

    private void StopDisco(Part part)
    {
        if (_discoLights.Remove(part, out var live)) live.Dispose();
    }

    private void StopAllDisco()
    {
        foreach (var part in _discoLights.Keys.ToArray()) StopDisco(part);
    }

    private void UpdateDisco(double dt)
    {
        if (_discoLights.Count == 0) return;

        var alive = new HashSet<Part>(
            VehicleProvider.GetAllVehicles(includeDebris: true).SelectMany(PartHelpers.GetAllParts),
            ReferenceEqualityComparer.Instance);
        foreach (var (part, live) in _discoLights.ToArray())
        {
            if (!alive.Contains(part))
            {
                StopDisco(part);
                continue;
            }
            live.Update(dt);
        }
    }
}
