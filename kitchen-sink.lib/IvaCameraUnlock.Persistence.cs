using System;
using System.Linq;
using System.Text.Json.Serialization;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.KitchenSinkLib;

public static partial class IvaCameraUnlock
{
    public sealed class SavedCamera
    {
        [JsonRequired] public bool Enabled { get; set; }
        [JsonRequired] public double Speed { get; set; } = DefaultSpeed;
        [JsonRequired] public SavedPartReference? SeatPart { get; set; }
        [JsonRequired] public int SeatIndex { get; set; }
        [JsonRequired] public double3 PositionBody { get; set; }
        [JsonRequired] public doubleQuat RotationBody { get; set; } = doubleQuat.Identity;
    }

    public static ISaveParticipant SaveParticipant => new SaveParticipant<SavedCamera>(
        "kitchen-sink-iva-camera", Capture, Reset, Restore, order: 190, validate: Validate);

    private static SavedCamera Capture()
    {
        var saved = new SavedCamera { Speed = Speed, Enabled = Enabled };
        if (_active == null) return saved;
        if (!IsValid(_active)) throw new InvalidOperationException("Unlocked IVA target is missing or ambiguous.");
        saved.SeatPart = SavedPartReference.Capture(_active.Vehicle, _active.Seat.Parent);
        saved.SeatIndex = Array.IndexOf(_active.Seat.Parent.Modules.Get<IVASeat>().ToArray(), _active.Seat);
        saved.PositionBody = _active.Camera.LocalPosition;
        saved.RotationBody = doubleQuat.Inverse(_active.Vehicle.Body2Cce) * _active.Camera.LocalRotation;
        Validate(saved);
        return saved;
    }

    private static void Validate(SavedCamera saved)
    {
        double3 p = saved.PositionBody;
        doubleQuat q = saved.RotationBody;
        double norm = q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
        if (!double.IsFinite(saved.Speed) || saved.Speed < MinSpeed || saved.Speed > MaxSpeed
            || !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)
            || !double.IsFinite(norm) || Math.Abs(norm - 1) > 0.01)
            throw new InvalidOperationException("Invalid unlocked IVA camera pose or speed.");
        if (!saved.Enabled) return;
        var part = saved.SeatPart;
        if (part == null || string.IsNullOrWhiteSpace(part.VehicleId) || string.IsNullOrWhiteSpace(part.TemplateId)
            || string.IsNullOrWhiteSpace(part.VehicleTopology) || part.TreePath == null || part.SubPartPath == null
            || part.TreePath.Length > 256 || part.SubPartPath.Length > 64
            || part.TreePath.Any(i => i < 0) || part.SubPartPath.Any(i => i < 0) || saved.SeatIndex < 0)
            throw new InvalidOperationException("Invalid unlocked IVA seat reference.");
    }

    private static void Restore(SavedCamera saved, SaveRestoreContext context)
    {
        if (!saved.Enabled) { Speed = saved.Speed; return; }
        context.Require(IsAvailable, "IVA camera unlock patch is unavailable.");
        var vehicle = VehicleProvider.FindVehicle(saved.SeatPart!.VehicleId);
        var part = saved.SeatPart.Resolve();
        context.Require(vehicle != null && !vehicle.IsDisposed && part != null,
            "Missing or ambiguous unlocked IVA vehicle/seat: " + saved.SeatPart.VehicleId);
        var seats = part!.Modules.Get<IVASeat>();
        context.Require(saved.SeatIndex < seats.Length, "Saved IVA seat module is unavailable.");
        // Native reconstruction owns the followed target and mode. Never silently retarget
        // the camera to a controlled vehicle or enter IVA on an unrelated native save.
        context.Require(Program.Editor == null && Program.MainViewport is GameViewport
            && Program.MainViewport.Mode == CameraMode.IVA
            && ReferenceEquals(Program.MainViewport.GetCamera().Following, vehicle),
            "Native camera does not match the saved unlocked IVA target/mode.");
        var viewport = (GameViewport)Program.MainViewport;
        viewport.IvaController.Seat = seats[saved.SeatIndex];
        viewport.IvaController.LastFollowing = vehicle!;
        Speed = saved.Speed;
        context.Require(Enable(), LastError ?? "Could not restore unlocked IVA camera.");
        _active!.RestorePose(saved.PositionBody, saved.RotationBody);
    }
}
