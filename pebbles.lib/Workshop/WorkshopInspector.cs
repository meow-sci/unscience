using System;
using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace MeowSci.PebblesLib;

public sealed partial class WorkshopEditor
{
    private static readonly string[] PrimitiveNames = { "Box", "Sphere", "Capsule", "Cylinder" };
    private static readonly string[] AxisNames = { "X", "Y", "Z" };

    private void Inspector()
    {
        bool shown = ImGui.BeginChild("##workshop-inspector"u8, new float2(0, Math.Max(150, ImGui.GetContentRegionAvail().Y - 120)), ImGuiChildFlags.Borders);
        try
        {
            if (!shown) return;
            if (_restoreInspectorScroll) { ImGui.SetScrollY(_state.InspectorScroll); _restoreInspectorScroll = false; }
            ImGui.TextWrapped(_state.Object.Name);
            bool mesh = _state.ShowMesh, colliders = _state.ShowColliders;
            if (ImGui.Checkbox("Show mesh"u8, ref mesh)) _state.ShowMesh = mesh;
            ImGui.SameLine(0, 8); if (ImGui.Checkbox("Show colliders"u8, ref colliders)) _state.ShowColliders = colliders;
            ImGui.SeparatorText("Mesh scale"u8);
            {
                var before = RecipeCopy.Clone(_state.Object);
                var transform = _state.Object.Transform;
                float scale = transform.Scale.X;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat("##mesh-scale"u8, ref scale, .01f, .001f, 10000))
                {
                    try { ClutterAuthoring.SetScale(_state.Object, scale); NumericChanged(before, true); }
                    catch (Exception ex) { _message = ex.Message; }
                }
                ImGui.TextWrapped("Scale resizes the mesh and all colliders together.");
                ImGui.BeginDisabled(_stale || !_preview.IsReady);
                if (ImGui.Button(" Ground mesh and colliders "u8, new float2(-1, 0)))
                {
                    float shift = -_preview.BoundsMin.Y;
                    Edit(() =>
                    {
                        transform.Position = Vec3.From(transform.Position.Vector + Vector3.UnitY * shift);
                        foreach (var c in _state.Object.Colliders) c.Position = Vec3.From(c.Position.Vector + Vector3.UnitY * shift);
                    }, true);
                }
                ImGui.EndDisabled();
            }
            ImGui.Spacing(); ImGui.SeparatorText("Collision geometry"u8);
            bool useColliders = _state.Object.Collision == CollisionPolicy.Custom;
            if (ImGui.Checkbox("Use colliders"u8, ref useColliders))
                Edit(() => _state.Object.Collision = useColliders ? CollisionPolicy.Custom : CollisionPolicy.None);
            int newKind = _state.NewColliderKind;
            ImGui.SetNextItemWidth(-1); if (ImGui.Combo("##new-shape"u8, ref newKind, PrimitiveNames)) _state.NewColliderKind = newKind;
            ImGui.BeginDisabled(_stale || !_preview.IsReady);
            if (ImGui.Button(" Add fitted shape "u8, new float2(-1, 0)))
            {
                Edit(() =>
                {
                    var collider = WorkshopColliders.Fit((ColliderKind)_state.NewColliderKind, _preview.BoundsMin, _preview.BoundsMax);
                    _state.Object.Colliders.Add(collider); _state.SelectedColliderId = collider.Id;
                    _state.Object.Collision = CollisionPolicy.Custom;
                });
            }
            ImGui.EndDisabled();
            foreach (var collider in _state.Object.Colliders)
            {
                if (ImGui.Selectable($"{collider.Name} ({collider.Kind})##{collider.Id}", collider.Id == _state.SelectedColliderId))
                { CancelGesture(); _state.SelectedColliderId = collider.Id; }
            }
            if (Selected is { } selected) ColliderInspector(selected);
            else ImGui.TextDisabled("Add a shape, or click a collider outline."u8);
            if (_state.Snap)
            {
                ImGui.Spacing(); ImGui.SeparatorText("Snap steps"u8);
                float move = _state.MoveSnap, angle = _state.AngleSnap, size = _state.SizeSnap;
                ImGui.SetNextItemWidth(-1); if (ImGui.DragFloat("Move (m)"u8, ref move, .01f, .001f, 100)) _state.MoveSnap = move;
                ImGui.SetNextItemWidth(-1); if (ImGui.DragFloat("Angle (deg)"u8, ref angle, 1, .1f, 180)) _state.AngleSnap = angle;
                ImGui.SetNextItemWidth(-1); if (ImGui.DragFloat("Size (m)"u8, ref size, .01f, .001f, 100)) _state.SizeSnap = size;
            }
            FinishNumeric();
        }
        finally { _state.InspectorScroll = ImGui.GetScrollY(); ImGui.EndChild(); }
    }

    private void ColliderInspector(ColliderRecipe collider)
    {
        ImGui.Spacing(); ImGui.Separator();
        if (_nameId != collider.Id) { _name.Value16 = collider.Name; _nameId = collider.Id; }
        var before = RecipeCopy.Clone(_state.Object);
        bool changed = false;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##collider-name"u8, _name)) { collider.Name = _name.ToString(); changed = true; }
        bool enabled = collider.Enabled, visible = collider.Visible;
        if (ImGui.Checkbox("Collision enabled"u8, ref enabled)) { collider.Enabled = enabled; changed = true; }
        ImGui.SameLine(0, 8);
        if (ImGui.Checkbox("Visible"u8, ref visible)) { collider.Visible = visible; changed = true; }
        changed |= VectorInput("Center (m)", collider.Position, v => collider.Position = v, .01f);
        changed |= VectorInput("Rotation (degrees)", collider.RotationDegrees, v => collider.RotationDegrees = v, .5f);
        if (collider.Kind == ColliderKind.Box)
            changed |= VectorInput("Full dimensions (m)", collider.Dimensions, v => collider.Dimensions = v, .01f, .001f, 100000);
        else if (collider.Kind != ColliderKind.ConvexHull)
        {
            float diameter = collider.Dimensions.X, height = collider.Dimensions.Y;
            ImGui.Text("Diameter (m)"u8); ImGui.SetNextItemWidth(-1);
            if (ImGui.DragFloat("##diameter"u8, ref diameter, .01f, .001f, 100000)) changed = true;
            if (collider.Kind != ColliderKind.Sphere)
            {
                ImGui.Text("Total height (m)"u8); ImGui.SetNextItemWidth(-1);
                if (ImGui.DragFloat("##height"u8, ref height, .01f, .001f, 100000)) changed = true;
                if (collider.Kind == ColliderKind.Capsule) height = Math.Max(height, diameter);
            }
            collider.Dimensions = new Vec3(diameter, height, diameter);
        }
        else
        {
            if (_assets != null) AssetCombo("Hull source mesh", collider.HullMeshId, _assets.MeshIds,
                value => Edit(() => collider.HullMeshId = _assets.ResolveSelection(value), true));
            changed |= VectorInput("Hull scale", collider.HullScale, value => collider.HullScale = value, .01f, .001f, 10000);
            ImGui.TextWrapped("Hull source wireframe is shown; physics uses its convex envelope. Resize handles edit hull scale."u8);
        }
        if (changed) NumericChanged(before, false);
        ImGui.BeginDisabled(collider.Kind == ColliderKind.ConvexHull || _stale || !_preview.IsReady);
        if (ImGui.Button(" Fit to mesh bounds "u8, new float2(-1, 0)))
        {
            Edit(() =>
            {
                var fit = WorkshopColliders.Fit(collider.Kind, _preview.BoundsMin, _preview.BoundsMax);
                collider.Position = fit.Position; collider.Dimensions = fit.Dimensions; collider.RotationDegrees = Vec3.Zero;
            });
        }
        ImGui.EndDisabled();
        if (ImGui.Button(" Duplicate "u8))
        {
            Edit(() => { var copy = RecipeCopy.Clone(collider); copy.Id = Guid.NewGuid().ToString("N"); copy.Name += " copy";
                _state.Object.Colliders.Add(copy); _state.SelectedColliderId = copy.Id; });
        }
        ImGui.SameLine(0, 8);
        if (ImGui.Button(" Delete "u8)) Edit(() => { _state.Object.Colliders.Remove(collider); _state.SelectedColliderId = ""; });
        int mirrorAxis = _state.MirrorAxis;
        ImGui.SetNextItemWidth(80); if (ImGui.Combo("##mirror-axis"u8, ref mirrorAxis, AxisNames)) _state.MirrorAxis = mirrorAxis;
        ImGui.SameLine(0, 8);
        if (ImGui.Button(" Mirror copy "u8))
            Edit(() => { var copy = WorkshopColliders.Mirror(collider, _state.MirrorAxis); _state.Object.Colliders.Add(copy); _state.SelectedColliderId = copy.Id; });
    }

    private static bool VectorInput(string label, Vec3 current, Action<Vec3> assign, float speed, float min = 0, float max = 0)
    {
        ImGui.Text(label); ImGui.SetNextItemWidth(-1);
        var value = new float3(current.X, current.Y, current.Z);
        if (!ImGui.DragFloat3($"##{label}", ref value, speed, min, max)) return false;
        assign(new(value.X, value.Y, value.Z)); return true;
    }
}
