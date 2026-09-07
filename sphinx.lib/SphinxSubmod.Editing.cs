using System;
using System.Linq;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.SphinxLib;

public sealed partial class SphinxSubmod
{
    private int _selectedId;
    private float3 _editScale, _editRotation, _editOffset;
    private bool _editAlign, _editUniform = true;
    private string? _editPng;
    private readonly ImInputString _editPngFilter = new(128);

    private void Select(SphinxEntry entry)
    {
        _selectedId = entry.Id; _editScale = entry.Scale; _editRotation = entry.Rotation;
        _editOffset = entry.Offset; _editAlign = entry.Align; _editPng = entry.Png;
        _editUniform = entry.Scale.X == entry.Scale.Y && entry.Scale.Y == entry.Scale.Z;
    }
    private void RenderEditing(SphinxEntry entry)
    {
        ImGui.SeparatorText($"Edit #{entry.Id}");
        TransformFields("sphinx_edit", ref _editScale, ref _editRotation, ref _editOffset, ref _editAlign, ref _editUniform);
        FileCombo("Texture##sphinx_edit", ref _editPng, _pngs, _editPngFilter, true);
        if (ImGui.Button("Apply changes##sphinx_edit"))
        {
            var scale = _editScale; var rotation = _editRotation; var offset = _editOffset; bool align = _editAlign; string? png = _editPng;
            _pending.Enqueue(() =>
            {
                if (!_entries.Contains(entry)) return;
                _ = PlacementMath.GroundedLocal(entry.Model.Min, entry.Model.Max, SphinxEntry.Vector(scale), SphinxEntry.Vector(rotation), SphinxEntry.Vector(offset));
                if (png != entry.Png)
                {
                    // Build the replacement first; a bad PNG leaves the visible model intact.
                    var replacement = new StaticModelResources(_assets, entry.MeshId, png, 8_000_000 - _entries.Where(e => e != entry).Sum(e => e.Model.VertexCount));
                    try { entry.Model.Dispose(); }
                    catch { replacement.Dispose(); throw; }
                    entry.Model = replacement; entry.Png = png;
                }
                entry.Scale = scale; entry.Rotation = rotation; entry.Offset = offset; entry.Align = align;
                _status = $"Updated #{entry.Id}.";
            });
        }
        ImGui.SameLine();
        if (ImGui.Button("Reset edit fields##sphinx_edit")) Select(entry);
        if (ImGui.Button("Snap current position to ground##sphinx_edit")) _pending.Enqueue(() =>
        {
            if (!_entries.Contains(entry)) return;
            // Translate the offset from the local surface basis into body-fixed coordinates.
            var radial = entry.Anchor.PositionCcf.NormalizeOrZero();
            var x = double3.Cross(double3.UnitZ, radial).NormalizeOrZero();
            if (x.LengthSquared() < .5) x = double3.Cross(double3.UnitY, radial).NormalizeOrZero();
            var up = entry.Align ? entry.Anchor.NormalCcf : radial;
            x = (x - up * double3.Dot(x,up)).NormalizeOrZero();
            var z = double3.Cross(x, up).NormalizeOrZero();
            var point = entry.Anchor.PositionCcf + x * entry.Offset.X + up * entry.Offset.Y + z * entry.Offset.Z;
            entry.Anchor = GroundPlacement.At(entry.Anchor.Body, point); entry.Offset = new float3(0); Select(entry);
            _status = $"Snapped #{entry.Id} to the terrain.";
        });
        ImGui.SameLine();
        if (ImGui.Button("Duplicate##sphinx_edit")) _pending.Enqueue(() =>
        {
            if (!_entries.Contains(entry)) return;
            if (_entries.Count >= 32) throw new InvalidOperationException("Remove a static before duplicating another.");
            var resource = new StaticModelResources(_assets, entry.MeshId, entry.Png, 8_000_000 - _entries.Sum(e => e.Model.VertexCount));
            var copy = new SphinxEntry { Id = _nextId++, MeshId = entry.MeshId, Png = entry.Png, Anchor = entry.Anchor,
                Align = entry.Align, Scale = entry.Scale, Rotation = entry.Rotation, Offset = entry.Offset, Model = resource };
            copy.Offset.X += Math.Max(1, (resource.Max.X - resource.Min.X) * entry.Scale.X);
            _entries.Add(copy); Select(copy); _status = $"Duplicated as #{copy.Id}; adjust offsets or snap to ground.";
        });
        ImGui.SameLine();
        if (ImGui.Button("Remove##sphinx_edit")) _pending.Enqueue(() => { if (_entries.Contains(entry)) Remove(entry); });
    }
}
