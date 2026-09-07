using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.PebblesLib;

namespace MeowSci.SphinxLib;

public sealed partial class SphinxSubmod
{
    private readonly LibraryFileBrowser _glbBrowser = new(GlbLibrary.Files, "sphinx", "Import GLB");
    private readonly PngFileBrowser _pngBrowser = new("sphinx", "Import texture override");
    private readonly ImInputString _glbFilter = new(128), _pngFilter = new(128);
    private string[] _glbs = [], _pngs = [];
    private string? _file, _png;
    private float3 _scale = new(1), _rotation, _offset;
    private TextureMapping _mapping = TextureMapping.Identity;
    private bool _align = true, _uniform = true, _armed;
    private float _range = 5000, _besideDistance = 50;

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##sphinx");
        if (ImGui.Button("Import GLB…##sphinx")) _glbBrowser.Open();
        ImGui.SameLine();
        if (ImGui.Button("Import PNG…##sphinx")) _pngBrowser.Open();
        ImGui.SameLine();
        if (ImGui.Button("Refresh libraries##sphinx")) RefreshLibrary();
        ImGui.TextDisabled(GlbLibrary.Files.DirectoryPath);
        FileCombo("GLB model##sphinx", ref _file, _glbs, _glbFilter, false);
        FileCombo("Texture##sphinx", ref _png, _pngs, _pngFilter, true);
        ImGui.TextWrapped("Embedded materials are used automatically. A PNG replaces color and transparency across the model; normal/PBR details stay from the GLB.");
        TextureFields("sphinx_new_texture", ref _mapping);
        TransformFields("sphinx_new", ref _scale, ref _rotation, ref _offset, ref _align, ref _uniform);
        ImGui.DragFloat("Pick range (m)##sphinx", ref _range, 10, 10, 100000);
        ImGui.DragFloat("Beside-vessel distance (m)##sphinx", ref _besideDistance, 1, 1, 100000);
        bool canPlace = !Program.EditorFlag && _file != null && _glbs.Contains(_file) && SphinxPatches.Ready;
        ImGui.BeginDisabled(!canPlace);
        if (ImGui.Button(_armed ? "Cancel placement##sphinx" : "Place on ground…##sphinx"))
        { _armed = !_armed; _status = _armed ? "Click the ground in the main view. Esc cancels." : "Placement cancelled."; }
        ImGui.SameLine();
        if (ImGui.Button("Place beside controlled vessel##sphinx")) Attempt(() =>
        {
            double clearance = float.IsFinite(_besideDistance) ? Math.Clamp(_besideDistance, 1, 100000) : 50;
            QueuePlacement(GroundPlacement.BesideControlled(clearance));
        });
        ImGui.EndDisabled();
        ImGui.TextWrapped("Models use glTF's Y-up axis. The rotated/scaled bounds are centered on the picked point with their base at ground level. Decorative placements last for this session and stay fixed to their planet. Large models may need extra height on uneven terrain.");
        if (Program.EditorFlag) ImGui.TextDisabled("Place statics in the flight scene.");
        if (_entries.Count > 0)
        {
            ImGui.SeparatorText("Placed statics"u8);
            foreach (var entry in _entries.ToArray())
            {
                ImGui.PushID(entry.Id);
                ImGui.Checkbox("##visible", ref entry.Visible);
                ImGui.SameLine();
                if (ImGui.Selectable($"#{entry.Id} {GlbIdentity.Label(entry.MeshId)} — {entry.Anchor.Body.Id}", entry.Id == _selectedId)) Select(entry);
                ImGui.PopID();
            }
            var selected = _entries.FirstOrDefault(e => e.Id == _selectedId);
            if (selected != null) RenderEditing(selected);
            if (ImGui.Button("Remove all statics##sphinx")) _pending.Enqueue(Clear);
        }
        ImGui.TextWrapped(_status);
        SubmodUI.EndContentArea();
    }

    private static bool FileCombo(string label, ref string? selected, string[] files, ImInputString filter, bool embeddedOption)
    {
        if (!ImGui.BeginCombo(label, selected ?? (embeddedOption ? "Embedded GLB materials" : "Choose a GLB"))) return false;
        string? previous = selected;
        ImGui.PushID(label);
        if (ImGui.IsWindowAppearing()) { filter.Clear(); ImGui.SetKeyboardFocusHere(); }
        ImGui.InputTextWithHint("##filter", "Filter files…"u8, filter);
        if (embeddedOption && ImGui.Selectable("Embedded GLB materials"u8, selected == null)) selected = null;
        foreach (string file in files)
            if (file.Contains(filter.ToString().Trim(), StringComparison.OrdinalIgnoreCase) && ImGui.Selectable(file, file == selected)) selected = file;
        if (files.Length == 0) ImGui.TextDisabled("Import a file to begin.");
        ImGui.PopID();
        ImGui.EndCombo();
        return selected != previous;
    }

    private static bool TransformFields(string id, ref float3 scale, ref float3 rotation, ref float3 offset, ref bool align, ref bool uniform)
    {
        ImGui.PushID(id);
        bool changed = ImGui.Checkbox("Align with terrain slope"u8, ref align);
        if (ImGui.Checkbox("Uniform scale"u8, ref uniform) && uniform)
        { scale = new float3(scale.X); changed = true; }
        if (uniform)
        {
            float value = scale.X;
            if (ImGui.DragFloat("Scale"u8, ref value, .01f, .01f, 1000))
            { scale = new float3(value); changed = true; }
        }
        else changed |= ImGui.DragFloat3("Scale XYZ"u8, ref scale, .01f, .01f, 1000);
        changed |= ImGui.DragFloat3("Rotation XYZ (degrees)"u8, ref rotation, .5f, -360, 360);
        changed |= ImGui.DragFloat3("Offset XYZ (m)"u8, ref offset, .1f, 0, 0);
        ImGui.TextDisabled("Y is up / heading; X and Z move along the ground.");
        ImGui.PopID();
        return changed;
    }

    private static bool TextureFields(string id, ref TextureMapping mapping)
    {
        ImGui.PushID(id);
        var scale = new float2(mapping.Scale.X, mapping.Scale.Y);
        var offset = new float2(mapping.Offset.X, mapping.Offset.Y);
        bool changed = ImGui.DragFloat2("Texture scale UV"u8, ref scale, .01f, .01f, 1000, "%.3f");
        ImGui.SetItemTooltip("U and V are the mesh's texture axes. Above 1 repeats the image more; below 1 makes it larger. Ctrl-click a value to type it.");
        changed |= ImGui.DragFloat2("Texture offset UV"u8, ref offset, .01f, 0, 0, "%.3f");
        ImGui.SetItemTooltip("Shift the texture along U and V. 1 is a full repeat; 0.5 is half a repeat.");
        if (changed) mapping = new(new(scale.X, scale.Y), new(offset.X, offset.Y));
        if (ImGui.Button(" Reset texture mapping ")) { mapping = TextureMapping.Identity; changed = true; }
        ImGui.TextDisabled("Mapping affects all materials and maps, including PNG overrides.");
        ImGui.PopID();
        return changed;
    }

    public void RenderFloatingWindows()
    {
        _glbBrowser.Render(name => { _file = name; RefreshLibrary(); _status = $"Imported {name}."; });
        _pngBrowser.Render(name => { _png = name; RefreshLibrary(); _status = $"Imported {name}."; });
        if (!_armed) return;
        if (Program.EditorFlag || Universe.CurrentSystem == null || ImGui.IsKeyPressed(ImGuiKey.Escape))
        { _armed = false; _status = "Placement cancelled."; return; }
        var drawList = ImGui.GetForegroundDrawList();
        var hintPosition = ImGui.GetMousePos() + new float2(18, 18);
        ImString hint = $"Place {_file ?? "GLB"} — click terrain (Esc cancels)";
        drawList.AddText(hintPosition + new float2(1, 1), ImColor8.Black, hint);
        drawList.AddText(hintPosition, ImColor8.White, hint);
        if (!ImGui.GetIO().WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) Attempt(() =>
        {
            double range = float.IsFinite(_range) ? Math.Clamp(_range,10,100000) : 5000;
            if (GroundPlacement.TryCursor(range, out var anchor)) { QueuePlacement(anchor); _armed = false; }
            else _status = "No ground within range. Click again or press Esc.";
        });
    }
}
