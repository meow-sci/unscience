using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace MeowSci.DohLib;

public sealed partial class DohSubmod
{
    private void DrawPlacementPreview()
    {
        if (_placementPreview is not { } target || !Program.DrawUI) return;
        var camera = Program.MainViewport.GetCamera();
        target.PreviewPose(camera, out var centre, out var rotation);
        var shape = target.Capsule.Shape;
        var x = double3.UnitX.Transform(rotation) * shape.Radius;
        var y = double3.UnitY.Transform(rotation);
        var z = double3.UnitZ.Transform(rotation) * shape.Radius;
        var a = centre + y * shape.HalfLength;
        var b = centre - y * shape.HalfLength;
        var color = _placementValid ? new byte4(80, 240, 120, 230) : new byte4(255, 75, 65, 230);
        var draw = ImGui.GetForegroundDrawList();
        void Line(double3 start, double3 end)
        {
            // EgoToScreen rejects positions behind the camera with NaN; do not draw across them.
            var p = camera.EgoToScreen(start) + Program.MainViewport.Position;
            var q = camera.EgoToScreen(end) + Program.MainViewport.Position;
            if (float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(q.X) && float.IsFinite(q.Y))
                ImGuiHelper.DrawLine(draw, p, q, color, 1.5f);
        }
        void Ring(double3 origin)
        {
            for (int i = 0; i < 24; i++)
            {
                double t = i * Math.PI / 12, next = (i + 1) * Math.PI / 12;
                Line(origin + x * Math.Cos(t) + z * Math.Sin(t), origin + x * Math.Cos(next) + z * Math.Sin(next));
            }
        }
        void Cap(double3 origin, double3 radial, double3 outward)
        {
            for (int i = 0; i < 12; i++)
            {
                double t = i * Math.PI / 12, next = (i + 1) * Math.PI / 12;
                Line(origin + radial * Math.Cos(t) + outward * Math.Sin(t),
                    origin + radial * Math.Cos(next) + outward * Math.Sin(next));
            }
        }
        Ring(a); Ring(b);
        Line(a + x, b + x); Line(a - x, b - x); Line(a + z, b + z); Line(a - z, b - z);
        Cap(a, x, y * shape.Radius); Cap(a, z, y * shape.Radius);
        Cap(b, x, -y * shape.Radius); Cap(b, z, -y * shape.Radius);
    }
}
