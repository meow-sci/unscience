using System;
using System.Collections.Generic;
using System.Numerics;
using Brutal;
using Brutal.ImGuiApi;
using RenderCore;

namespace MeowSci.PebblesLib;

public sealed partial class WorkshopEditor
{
    private sealed record HullWire(Vector3 Minimum, Vector3 Maximum, List<(Vector3 A, Vector3 B)> Edges);
    private readonly Dictionary<string, HullWire> _hulls = new(StringComparer.Ordinal);

    private void RefreshHullSources()
    {
        if (_assets == null) return;
        foreach (var collider in _state.Object.Colliders)
        {
            if (collider.Kind != ColliderKind.ConvexHull || _hulls.ContainsKey(collider.HullMeshId)) continue;
            try
            {
                var source = _assets.ResolveMesh(collider.HullMeshId);
                var edges = new List<(Vector3, Vector3)>();
                var minimum = new Vector3(float.PositiveInfinity); var maximum = new Vector3(float.NegativeInfinity);
                foreach (var primitive in source.HostPrimitives)
                {
                    var positions = primitive.GetVertexSpan<Vector3>(MeshAttribute.Position);
                    foreach (var position in positions) { minimum = Vector3.Min(minimum, position); maximum = Vector3.Max(maximum, position); }
                    var buffer = primitive.IndexBuffer ?? throw new InvalidOperationException("Hull source has no triangle indices.");
                    var indices = new uint[primitive.IndexCount];
                    if (buffer.Stride == ByteSize.Of<ushort>())
                    { var span = buffer.AsSpan<ushort>(); for (int i = 0; i < indices.Length; i++) indices[i] = span[i]; }
                    else buffer.AsSpan<uint>().CopyTo(indices);
                    int stride = Math.Max(1, indices.Length / 6000) * 3;
                    for (int i = 0; i + 2 < indices.Length && edges.Count < 6000; i += stride)
                    {
                        var a = positions[(int)indices[i]]; var b = positions[(int)indices[i + 1]]; var c = positions[(int)indices[i + 2]];
                        edges.Add((a, b)); edges.Add((b, c)); edges.Add((c, a));
                    }
                }
                _hulls[collider.HullMeshId] = new(minimum, maximum, edges);
            }
            catch (Exception ex) { _message = "Hull preview: " + ex.Message; }
        }
    }

    private bool DrawHull(ImDrawListPtr draw, ColliderRecipe collider, ImColor8 color, float width)
    {
        if (!_hulls.TryGetValue(collider.HullMeshId, out var wire)) return false;
        var rotation = WorkshopMath.Rotation(collider.RotationDegrees.Vector);
        Vector3 Transform(Vector3 value) => Vector3.Transform(value * collider.HullScale.Vector, rotation) + collider.Position.Vector;
        foreach (var edge in wire.Edges) Line(draw, Transform(edge.A), Transform(edge.B), color, width);
        return true;
    }

    private (Vector3 Center, Vector3 Half) PickBounds(ColliderRecipe collider)
    {
        if (collider.Kind != ColliderKind.ConvexHull || !_hulls.TryGetValue(collider.HullMeshId, out var wire))
            return (collider.Position.Vector, WorkshopColliders.HalfExtents(collider));
        var center = (wire.Minimum + wire.Maximum) * .5f * collider.HullScale.Vector;
        return (collider.Position.Vector + Vector3.Transform(center, WorkshopMath.Rotation(collider.RotationDegrees.Vector)),
            (wire.Maximum - wire.Minimum) * .5f * collider.HullScale.Vector);
    }
}
