using System;
using System.Linq;
using System.Collections.Generic;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using MeowSci.KsaAbstractions;

namespace MeowSci.PebblesLib;

public sealed partial class PebblesSubmod
{
    private readonly ImInputString _assetFilter = new(256);
    private ObjectRecipe _replacement = new() { Collision = CollisionPolicy.None };
    private List<string> _targetTypes = [];
    private bool _allTypes;
    private string _recipeBody = "";

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##pebbles");
        try
        {
            ImGui.BeginDisabled(_workshop.IsOpen);
            try
            {
                ImGui.SeparatorText("Mesh"u8);
                ImGui.InputText("Find mesh", _assetFilter);
                string mesh = _replacement.Lods[0].MeshIds.FirstOrDefault() ?? "";
                string chosen = PebblesUi.Choice("Mesh", mesh, _assets.MeshIds, _assetFilter.ToString());
                if (chosen != mesh) Try(() => SelectMesh(chosen));
                ImportControls();
                mesh = _replacement.Lods[0].MeshIds.FirstOrDefault() ?? "";
                ImGui.Spacing();
                ImGui.SeparatorText("Scale and colliders"u8);
                float scale = _replacement.Transform.Scale.X;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputFloat("Scale"u8, ref scale)) Try(() => ClutterAuthoring.SetScale(_replacement, scale));
                ImGui.TextDisabled($"{(_replacement.Collision == CollisionPolicy.Custom ? _replacement.Colliders.Count(c => c.Enabled) : 0)} colliders · scale also resizes colliders");
                ImGui.BeginDisabled(mesh.Length == 0 || _workshop.IsOpen);
                try
                {
                    if (ImGui.Button(" Preview and set up colliders ", new float2(-1, 0)))
                        _workshop.Open(_replacement, CompleteWorkshop);
                }
                finally { ImGui.EndDisabled(); }
                }
            finally { ImGui.EndDisabled(); }
            string selectedMesh = _replacement.Lods[0].MeshIds.FirstOrDefault() ?? "";
            ImGui.Spacing();
            ImGui.SeparatorText("Planet clutter targets"u8);
            string body = PebblesUi.Choice("Planet", _bodyId, _controller.BodyIds);
            if (body != _bodyId)
            {
                _bodyId = body; _targetTypes.Clear(); _allTypes = false;
                Try(RefreshTargets);
            }
            if (ImGui.Button(" Refresh target types ", new float2(-1, 0))) Try(RefreshTargets);
            bool resolved = _recipeBody == _bodyId && _recipe.Ecotypes.Count > 0;
            if (resolved)
            {
                ImGui.Checkbox("All clutter types"u8, ref _allTypes);
                ImGui.BeginDisabled(_allTypes);
                try
                {
                    foreach (var type in _recipe.Ecotypes)
                    {
                        bool selected = _targetTypes.Contains(type.Name);
                        if (ImGui.Checkbox(type.Name, ref selected))
                        { if (selected) _targetTypes.Add(type.Name); else _targetTypes.Remove(type.Name); }
                    }
                }
                finally { ImGui.EndDisabled(); }
                foreach (var missing in _targetTypes.Where(n => _recipe.Ecotypes.All(e => e.Name != n)).ToArray())
                    if (ImGui.Button($"Remove unavailable target: {missing}")) _targetTypes.Remove(missing);
            }
            else ImGui.TextWrapped("Select a planet and refresh its clutter types to continue.");
            ImGui.TextWrapped("Replaces every variant of the checked types at the scale shown in the preview.");
            ImGui.BeginDisabled(!resolved || selectedMesh.Length == 0 || (!_allTypes && _targetTypes.Count == 0) || _workshop.IsOpen);
            try
            {
                if (ImGui.Button(" Apply to planet ", new float2(-1, 0))) Try(() =>
                {
                    var current = _controller.Capture(_bodyId);
                    IEnumerable<string> targets = _allTypes ? _recipe.Ecotypes.Select(e => e.Name) : _targetTypes;
                    foreach (string name in targets)
                        if (current.Ecotypes.Find(e => e.Name == name)?.Signature != _recipe.Ecotypes.Find(e => e.Name == name)?.Signature)
                            throw new InvalidOperationException("Clutter targets changed. Refresh target types before applying.");
                    _controller.QueueApply(_bodyId, ClutterAuthoring.Replace(current, _replacement, targets));
                    _message = "Replacement queued. Restore applied clutter below.";
                });
            }
            finally { ImGui.EndDisabled(); }
            RenderAppliedClutter();
            if (_message.Length > 0) ImGui.TextWrapped(_message);
            ImGui.TextWrapped(_controller.Status);
            foreach (var fault in _controller.Faults) ImGui.TextWrapped(fault);
        }
        finally { SubmodUI.EndContentArea(); }
    }

    private void RefreshTargets()
    {
        _controller.Refresh();
        var recipe = _controller.Capture(_bodyId);
        _recipe = recipe; _recipeBody = _bodyId;
    }

    private void SelectMesh(string mesh)
    {
        mesh = _assets.ResolveSelection(mesh);
        var materials = mesh.StartsWith(GlbIdentity.Prefix, StringComparison.Ordinal)
            ? _assets.GlbMaterials(mesh) : new List<MaterialRecipe> { new() { SourceColors = true } };
        ClutterAuthoring.AssignMesh(_replacement, mesh, materials);
        _glbStatus = mesh.StartsWith(GlbIdentity.Prefix, StringComparison.Ordinal)
            ? "Textures assigned automatically. " + string.Join(" ", _assets.GlbWarnings(mesh)) : "";
        if (mesh.StartsWith(GlbIdentity.Prefix, StringComparison.Ordinal))
        {
            _glbOptions = _assets.GlbOptions(mesh);
            _glbSelected = mesh;
            _glbPath.Value16 = GlbIdentity.Parse(mesh).Path;
        }
        _releaseImports = false;
    }
}
