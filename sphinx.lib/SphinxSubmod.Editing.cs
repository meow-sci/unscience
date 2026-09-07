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
    private CollisionMode _editCollision;
    private float3 _editScale, _editRotation, _editOffset;
    private bool _editAlign, _editUniform = true;
    private string? _editPng;
    private TextureMapping _editMapping = TextureMapping.Identity;
    private readonly ImInputString _editPngFilter = new(128);

    private void Select(SphinxEntry entry)
    {
        _selectedId = entry.Id; _editScale = entry.Scale; _editRotation = entry.Rotation;
        _editOffset = entry.Offset; _editAlign = entry.Align; _editPng = entry.Png;
        _editMapping = entry.Mapping; _editCollision = entry.Collision;
        _editUniform = entry.Scale.X == entry.Scale.Y && entry.Scale.Y == entry.Scale.Z;
    }
    private void RenderEditing(SphinxEntry entry)
    {
        ImGui.SeparatorText($"Edit #{entry.Id}");
        ImGui.TextDisabled("Edits apply live. Rebuilding textures or colliders may briefly pause for large models.");
        bool transformChanged = TransformFields("sphinx_edit", ref _editScale, ref _editRotation, ref _editOffset, ref _editAlign, ref _editUniform);
        bool textureChanged = FileCombo("Texture##sphinx_edit", ref _editPng, _pngs, _editPngFilter, true);
        textureChanged |= TextureFields("sphinx_edit_texture", ref _editMapping);
        transformChanged |= CollisionFields("sphinx_edit_collision", ref _editCollision);
        ImGui.TextWrapped(entry.Collider?.Description ?? "Collision off");
        if (transformChanged) QueueTransformEdit(entry);
        if (_editCollision != entry.Collision || _editScale != entry.Scale || _editRotation != entry.Rotation || _editOffset != entry.Offset || _editAlign != entry.Align)
        {
            ImGui.TextDisabled("Transform/collider edit pending or failed; the previous settings remain active.");
            if (ImGui.Button("Retry transform/collider edit##sphinx_edit")) QueueTransformEdit(entry);
            ImGui.SameLine();
            if (ImGui.Button("Use current transform/collider settings##sphinx_edit"))
            {
                _editScale = entry.Scale; _editRotation = entry.Rotation; _editOffset = entry.Offset;
                _editAlign = entry.Align; _editCollision = entry.Collision;
                _editUniform = entry.Scale.X == entry.Scale.Y && entry.Scale.Y == entry.Scale.Z;
            }
        }
        if (textureChanged) QueueTextureEdit(entry);
        if (_editPng != entry.Png || _editMapping != entry.Mapping)
        {
            ImGui.TextDisabled("Texture edit pending or failed; the last successful texture remains visible.");
            if (ImGui.Button(" Retry texture edit ##sphinx_edit")) QueueTextureEdit(entry);
            ImGui.SameLine();
            if (ImGui.Button(" Use current texture settings ##sphinx_edit"))
            { _editPng = entry.Png; _editMapping = entry.Mapping; }
        }
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
            var anchor = GroundPlacement.At(entry.Anchor.Body, point);
            var replacement = BuildCollider(entry, entry.Collision, entry.Scale, entry.Rotation, new float3(0));
            ReplaceCollider(entry, replacement);
            entry.Anchor = anchor; entry.Offset = new float3(0); Select(entry);
            _status = $"Snapped #{entry.Id} to the terrain.";
        });
        ImGui.SameLine();
        if (ImGui.Button("Duplicate##sphinx_edit")) _pending.Enqueue(() =>
        {
            if (!_entries.Contains(entry)) return;
            if (_entries.Count >= 32) throw new InvalidOperationException("Remove a static before duplicating another.");
            var resource = new StaticModelResources(_assets, entry.MeshId, entry.Png, 8_000_000 - _entries.Sum(e => e.Model.VertexCount), entry.Mapping);
            var copy = new SphinxEntry { Id = _nextId++, MeshId = entry.MeshId, Png = entry.Png, Anchor = entry.Anchor,
                Align = entry.Align, Scale = entry.Scale, Rotation = entry.Rotation, Offset = entry.Offset, Model = resource, Mapping = entry.Mapping, Collision = entry.Collision, Visible = entry.Visible };
            copy.Offset.X += Math.Max(1, (resource.Max.X - resource.Min.X) * entry.Scale.X);
            try { copy.Collider = BuildCollider(copy, copy.Collision, copy.Scale, copy.Rotation, copy.Offset); }
            catch { resource.Dispose(); throw; }
            _entries.Add(copy); Select(copy); _status = $"Duplicated as #{copy.Id}; adjust offsets or snap to ground.";
        });
        ImGui.SameLine();
        if (ImGui.Button("Remove##sphinx_edit")) _pending.Enqueue(() => { if (_entries.Contains(entry)) Remove(entry); });
    }

    private void QueueTransformEdit(SphinxEntry entry)
    {
        var collision = _editCollision;
        var scale = _editScale; var rotation = _editRotation; var offset = _editOffset; bool align = _editAlign;
        _pending.Enqueue(() =>
        {
            if (!_entries.Contains(entry)) return;
            _ = PlacementMath.GroundedLocal(entry.Model.Min, entry.Model.Max, SphinxEntry.Vector(scale), SphinxEntry.Vector(rotation), SphinxEntry.Vector(offset));
            var replacement = BuildCollider(entry, collision, scale, rotation, offset);
            ReplaceCollider(entry, replacement);
            entry.Collision = collision;
            entry.Scale = scale; entry.Rotation = rotation; entry.Offset = offset; entry.Align = align;
            _status = $"Updated #{entry.Id}.";
        });
    }

    private static void ReplaceCollider(SphinxEntry entry, StaticCollider? replacement)
    {
        try { SphinxPhysics.Detach(entry); entry.Collider?.Dispose(); }
        catch { replacement?.Dispose(); throw; }
        entry.Collider = replacement;
    }

    private void QueueTextureEdit(SphinxEntry entry)
    {
        string? png = _editPng;
        var mapping = _editMapping;
        _pending.Enqueue(() =>
        {
            if (!_entries.Contains(entry)) return;
            if (png != entry.Png)
            {
                // Build first: invalid images or UVs retain the complete previous material/mesh.
                var replacement = new StaticModelResources(_assets, entry.MeshId, png,
                    8_000_000 - _entries.Where(e => e != entry).Sum(e => e.Model.VertexCount), mapping);
                try { entry.Model.Dispose(); }
                catch { replacement.Dispose(); throw; }
                entry.Model = replacement;
            }
            else if (mapping != entry.Mapping) entry.Model.UpdateTextureMapping(mapping);
            entry.Png = png; entry.Mapping = mapping;
            _status = $"Updated texture for #{entry.Id}.";
        });
    }
}
