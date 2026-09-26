using System;
using Brutal.Numerics;
using KSA;

namespace MeowSci.DontStifleMeLib;

/// <summary>
/// Per-axis replacement for the stock uniform scale-gizmo drag. Keeps its own raw (un-snapped)
/// accumulator per drag session so snapping does not swallow small per-frame cursor deltas.
/// </summary>
internal static class PerAxisScaleDrag
{
    private static bool _active;
    private static Part? _part;
    private static int _axis = -1;
    private static double _raw;

    public static void End()
    {
        _active = false;
        _part = null;
        _axis = -1;
    }

    public static void Step(
        VehicleEditor editor,
        in double4x4 matrixVehicleAsmb2Ego,
        IViewport viewport,
        Func<Part, double, double> quantizeScale,
        Action<Part, Action<Part>> forEachPartWithSymmetry)
    {
        Part? selected = editor.Selected;
        int axis = editor.HighlightedGizmoSegmentIndex;
        if (selected == null || axis < 0 || axis > 2) return;

        Camera camera = viewport.GetCamera();
        double3 lastNear = camera.ScreenToEgoNearPlane(editor.CursorPositionScreenLastFrame);
        double nearDistance = lastNear.Length();
        double3 curNear = camera.ScreenToEgoNearPlane(editor.CursorPositionScreen);
        double3 cursorDelta = curNear - lastNear;
        if (cursorDelta.NormalizeOrZero().Length().Equals(0.0)) return;

        GenericGizmo.PerSegmentData[] segments = editor.ScaleGizmo.GetSegmentDataByViewport(viewport);
        double3 axisDir = double3.UnitX.Transform(segments[axis].Body2Cce).NormalizeOrZero();
        if (axisDir.Length() == 0.0) return;

        // Project the near-plane cursor motion onto the gizmo axis and scale it up to the part's depth.
        double3 alongAxis = double3.Dot(cursorDelta, axisDir) * axisDir;
        double partDistance = selected.PositionEgo(in matrixVehicleAsmb2Ego).Length();
        double depthRatio = partDistance / nearDistance;
        double magnitude = (alongAxis * depthRatio).Length();
        int sign = Math.Sign(double3.Dot(cursorDelta, axisDir));

        if (!_active || _part != selected || _axis != axis)
        {
            _active = true;
            _part = selected;
            _axis = axis;
            _raw = selected.Scale[axis];
        }

        _raw += magnitude * sign;
        double snapped = quantizeScale(selected, _raw);
        if (snapped.Equals(selected.Scale[axis])) return;

        double3 newScale = selected.Scale;
        SetAxis(ref newScale, axis, snapped);

        forEachPartWithSymmetry(selected, part =>
        {
            part.Scale = newScale;
            part.RefreshScaleAndReposition();
        });
        // Part.Tree is nullable since KSA 5482; editor selections always belong to a tree.
        selected.Tree?.RefreshStaticMass();
    }

    private static void SetAxis(ref double3 v, int axis, double value)
    {
        switch (axis)
        {
            case 0: v.X = value; break;
            case 1: v.Y = value; break;
            default: v.Z = value; break;
        }
    }
}
