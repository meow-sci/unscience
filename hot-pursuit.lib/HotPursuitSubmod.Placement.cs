using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace MeowSci.HotPursuitLib;

public sealed partial class HotPursuitSubmod
{
    private bool _armed;
    private bool _armingThisFrame;
    private string? _placeStatus;
    private bool _placeStatusIsError;

    private void Arm()
    {
        _armed = true;
        _armingThisFrame = true;
        _placeStatus = null;
    }

    private void Disarm(string? status)
    {
        _armed = false;
        _armingThisFrame = false;
        _placeStatus = status;
        _placeStatusIsError = false;
    }

    public void RenderFloatingWindows()
    {
        if (!_armed)
            return;

        DrawCursorHint();
        if (_armingThisFrame)
        {
            // The arm button itself is an ImGui click. Consume this frame so that the button
            // cannot also become the world placement click.
            _armingThisFrame = false;
            return;
        }
        if (Program.EditorFlag)
        {
            Disarm("Placement cancelled — cameras are a flight-scene feature.");
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Escape) || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            Disarm("Placement cancelled.");
            return;
        }
        if (!ImGui.GetIO().WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (HotPursuitPicker.TryPick(_placementRange, out var pick))
            {
                var tangent = PickTangent(pick.Normal);
                if (tangent.LengthSquared() > 0)
                {
                    var entry = new HotPursuitCamera
                    {
                        VehicleId = pick.Vehicle.Id,
                        PartInstanceId = pick.Part.InstanceId,
                        Vehicle = pick.Vehicle,
                        Part = pick.Part,
                        SaveTarget = MeowSci.KsaAbstractions.Persistence.SavedPartReference.Capture(pick.Vehicle, pick.Part),
                        MountPoint = pick.Position + pick.Normal * 0.15,
                        SurfaceNormal = pick.Normal,
                        MountTangent = tangent,
                        Visible = true,
                    };
                    _cameras.Add(entry);
                    TryOpenViewport(entry);
                    _armed = false;
                    _placeStatus = $"Placed camera #{entry.Id} on {entry.TargetDescription}.";
                    _placeStatusIsError = false;
                    Console.WriteLine($"hot-pursuit: placed camera #{entry.Id} on {entry.TargetDescription} "
                                      + $"(hit {pick.Distance:0.0} m)");
                }
                else
                {
                    SetPlacementError("The hit surface has no usable tangent basis.");
                }
            }
            else
            {
                SetPlacementError($"Nothing hit within {_placementRange:0} m. Click again or press Esc.");
            }
        }
    }

    private void SetPlacementError(string message)
    {
        _placeStatus = message;
        _placeStatusIsError = true;
    }

    private static double3 PickTangent(double3 normal)
    {
        var tangent = ProjectTangent(double3.UnitY, normal);
        if (tangent.LengthSquared() > 1e-20)
            return tangent;
        tangent = ProjectTangent(double3.UnitX, normal);
        return tangent.LengthSquared() > 1e-20 ? tangent : double3.Zero;
    }

    private static double3 ProjectTangent(double3 candidate, double3 normal)
    {
        var result = candidate - normal * double3.Dot(candidate, normal);
        var length = result.Length();
        return double.IsFinite(length) && length > 1e-10 ? result / length : double3.Zero;
    }

    private static void DrawCursorHint()
    {
        var drawList = ImGui.GetForegroundDrawList();
        var position = ImGui.GetMousePos() + new float2(18f, 18f);
        ImString hint = "Hot Pursuit: click a vehicle part to mount a camera (Esc/right-click cancels)";
        drawList.AddText(position + new float2(1f, 1f), ImColor8.Black, hint);
        drawList.AddText(position, ImColor8.White, hint);
    }
}
