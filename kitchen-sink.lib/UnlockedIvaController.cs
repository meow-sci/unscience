using System;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using RenderCore.Input;

namespace MeowSci.KitchenSinkLib;

/// <summary>Retains IVA identity/audio while delegating input to a private native free camera.</summary>
internal sealed class UnlockedIvaController : IVAController
{
    private readonly FlyController _fly;
    public GameViewport Viewport { get; }
    public Vehicle Vehicle { get; }
    public IVAController Original { get; }
    public doubleQuat RotationBody { get; private set; }

    public UnlockedIvaController(GameViewport viewport, Vehicle vehicle, IVAController original, double speed)
        : base(original.Camera, "Unlocked IVA")
    {
        Viewport = viewport;
        Vehicle = vehicle;
        Original = original;
        Seat = original.Seat;
        LastFollowing = vehicle;
        RotationBody = doubleQuat.Inverse(vehicle.Body2Cce) * Camera.LocalRotation;
        _fly = new FlyController(Camera, "Unlocked IVA movement");
        SetSpeed(speed);
        _fly.CacheOffset();
    }

    public void SetSpeed(double speed) => _fly.SetSpeed(speed, DistanceUnit.Meters);

    private bool InputBlocked => !ReferenceEquals(Program.InputViewport, Viewport)
        || Program.IsWindowOpen || Program.ConsoleWindow.IsOpen
        || ImGui.GetIO().WantCaptureKeyboard || ImGui.GetIO().WantTextInput;

    public override void OnFrame(IViewport inViewport, double inDeltaTime)
    {
        try
        {
            IvaCameraUnlock.Update();
            if (!ReferenceEquals(Viewport.IvaController, this)) return;
            // Camera.LocalPosition already follows the vehicle's body frame. Make orientation
            // follow it too, then let stock free flight apply this frame's translation/look.
            Camera.LocalRotation = Vehicle.Body2Cce * RotationBody;
            _fly.CacheOffset();
            if (InputBlocked) ReleaseInput();
            else _fly.OnFrame(inViewport, inDeltaTime);
            RotationBody = doubleQuat.Normalize(doubleQuat.Inverse(Vehicle.Body2Cce) * Camera.LocalRotation);
        }
        catch (Exception ex) { IvaCameraUnlock.Fail(ex); }
    }

    public void RestorePose(double3 positionBody, doubleQuat rotationBody)
    {
        Camera.LocalPosition = positionBody;
        RotationBody = doubleQuat.Normalize(rotationBody);
        Camera.LocalRotation = Vehicle.Body2Cce * RotationBody;
        _fly.CacheOffset();
    }

    public void ReturnToSeat()
    {
        var seat = Original.Seat;
        double3 position = seat.Parent.PositionVehicleAsmbOffset(seat.PositionAsmb);
        Camera.PositionCce = double3.Transform(Vehicle.PosAsmbToBody(position), Vehicle.Body2Cce);
        Camera.LocalRotation = KSA.Camera.LookAtRotation(
            double3.Transform(seat.ForwardAxisAsmb, Vehicle.Asmb2Cce * seat.Parent.Asmb2VehicleAsmb),
            double3.Transform(seat.UpAxisAsmb, Vehicle.Asmb2Cce * seat.Parent.Asmb2VehicleAsmb));
        Original.LastFollowing = Vehicle;
        Original.LastLocalRotation = Camera.LocalRotation;
        Original.LastFollowingAsmb2Cce = Vehicle.Asmb2Cce;
        Array.Clear(Original.LastCursorDiffs);
        Original.SwitchThisFrame = true;
    }

    public void ReleaseInput() => _fly.OnSwitchOff(CameraMode.IVA);

    public override void OnSwitchOff(CameraMode nextMode) => IvaCameraUnlock.Disable(returnToSeat: false);

    public override bool OnKey(GlfwKeyEvent keyEvent)
    {
        if (InputBlocked) { ReleaseInput(); return false; }
        // Seat switching is intentionally suspended until Return to Seat.
        return _fly.OnKey(keyEvent);
    }

    public override bool OnMouseButton(GlfwWindow window, GlfwMouseButton button, GlfwButtonAction action, GlfwModifier mods)
        => !InputBlocked && _fly.OnMouseButton(window, button, action, mods);

    public override bool OnCursorPos(GlfwWindow window, double2 pos)
        => !InputBlocked && _fly.OnCursorPos(window, pos);

    public override bool OnScroll(GlfwWindow window, double2 offset)
    {
        if (InputBlocked) return false;
        // Keep base speed independent of the native sprint key so saves never compound it.
        IvaCameraUnlock.Speed *= Math.Pow(2, Math.Clamp(offset.Y / 50, -20, 20));
        return true;
    }

    public override GlfwCursorMode GetCursorMode() => InputBlocked ? GlfwCursorMode.Normal : _fly.GetCursorMode();
    public override bool IsMouseDrag() => _fly.IsMouseDrag();
    public override void CancelMouseDrag() => _fly.CancelMouseDrag();
}
