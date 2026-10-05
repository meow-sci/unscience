using System;
using System.Reflection;
using HarmonyLib;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.KitchenSinkLib;

/// <summary>Owns a temporary IVA controller on the main gameplay viewport.</summary>
public static partial class IvaCameraUnlock
{
    public const double DefaultSpeed = 0.5;
    public const double MinSpeed = 0.01;
    public const double MaxSpeed = 100;
    private static Action<GameViewport, IVAController>? _setController;
    private static MethodInfo? _headOriginal;
    private static MethodInfo? _headPostfix;
    private static UnlockedIvaController? _active;
    private static double _speed = DefaultSpeed;

    public static bool IsAvailable => _setController != null;
    public static bool Enabled => _active != null;
    public static string? LastError { get; private set; }
    public static double Speed
    {
        get => _speed;
        set
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            _speed = Math.Clamp(value, MinSpeed, MaxSpeed);
            _active?.SetSpeed(_speed);
        }
    }

    public static void Apply(Harmony harmony)
    {
        if (IsAvailable) return;
        try
        {
            var setter = AccessTools.PropertySetter(typeof(GameViewport), nameof(GameViewport.IvaController))
                ?? throw new MissingMethodException(nameof(GameViewport), "set_IvaController");
            var assign = setter.CreateDelegate<Action<GameViewport, IVAController>>();
            _headOriginal = AccessTools.Method(typeof(IVASeat), "IsCameraInThisSeat", new[] { typeof(IViewport) })
                ?? throw new MissingMethodException(nameof(IVASeat), "IsCameraInThisSeat");
            _headPostfix = AccessTools.Method(typeof(IvaCameraUnlock), nameof(ShowHeadWhenUnlocked));
            harmony.Patch(_headOriginal, postfix: new HarmonyMethod(_headPostfix));
            _setController = assign;
            LastError = null;
            Console.WriteLine("kitchen-sink: IVA camera unlock available");
        }
        catch (Exception ex)
        {
            Remove(harmony);
            LastError = "IVA camera unlock is unavailable: " + ex.Message;
            Console.WriteLine("kitchen-sink: " + LastError);
            throw;
        }
    }

    public static void Remove(Harmony harmony)
    {
        Disable(returnToSeat: true);
        if (_headOriginal != null && _headPostfix != null) harmony.Unpatch(_headOriginal, _headPostfix);
        _headOriginal = _headPostfix = null;
        _setController = null;
    }

    private static void ShowHeadWhenUnlocked(IViewport viewport, ref bool __result)
    {
        if (_active != null && ReferenceEquals(viewport, _active.Viewport)) __result = false;
    }

    public static bool CanEnable(out string reason)
    {
        reason = "";
        if (!IsAvailable) reason = LastError ?? "IVA camera unlock is unavailable.";
        else if (Program.Editor != null) reason = "Enter IVA in flight to unlock the camera.";
        else if (Program.MainViewport is not GameViewport viewport || viewport.Mode != CameraMode.IVA)
            reason = "Enter IVA camera mode first.";
        else if (viewport.GetCamera().Following is not Vehicle vehicle || vehicle.IsDisposed
            || !ReferenceEquals(vehicle, VehicleProvider.FindVehicle(vehicle.Id))
            || !HasSeat(vehicle, viewport.IvaController.Seat))
            reason = "The IVA seat or followed vehicle is unavailable.";
        return reason.Length == 0;
    }

    public static bool Enable()
    {
        if (Enabled) return true;
        if (!CanEnable(out string reason)) { LastError = reason; return false; }
        var viewport = (GameViewport)Program.MainViewport;
        var vehicle = (Vehicle)viewport.GetCamera().Following!;
        var controller = new UnlockedIvaController(viewport, vehicle, viewport.IvaController, Speed);
        _setController!(viewport, controller);
        _active = controller;
        vehicle.ClearHeldPlayerInput();
        LastError = null;
        return true;
    }

    public static void Disable(bool returnToSeat = true)
    {
        var active = _active;
        _active = null;
        if (active == null) return;
        active.ReleaseInput();
        bool valid = IsValid(active);
        if (ReferenceEquals(active.Viewport.IvaController, active))
        {
            _setController!(active.Viewport, active.Original);
            if (returnToSeat && valid) active.ReturnToSeat();
        }
    }

    public static void Reset()
    {
        // Save lifecycle calls this before the old native world is destroyed.
        Disable(returnToSeat: false);
        Speed = DefaultSpeed;
        LastError = null;
    }

    public static void Update()
    {
        if (_active == null || IsValid(_active)) return;
        var viewport = _active.Viewport;
        bool owned = ReferenceEquals(viewport.IvaController, _active);
        Disable(returnToSeat: false);
        // Stock IVA would dereference a stale seat or use the hovered viewport to leave IVA.
        if (owned && ReferenceEquals(viewport, Program.MainViewport) && viewport.Mode == CameraMode.IVA)
            viewport.SetCameraMode(CameraMode.Orbit);
    }

    internal static bool IsValid(UnlockedIvaController active) => Program.Editor == null
        && ReferenceEquals(Program.MainViewport, active.Viewport) && active.Viewport.Mode == CameraMode.IVA
        && ReferenceEquals(active.Viewport.IvaController, active)
        && ReferenceEquals(active.Camera.Following, active.Vehicle) && !active.Vehicle.IsDisposed
        && ReferenceEquals(active.Vehicle, VehicleProvider.FindVehicle(active.Vehicle.Id))
        && HasSeat(active.Vehicle, active.Seat);

    private static bool HasSeat(Vehicle vehicle, IVASeat? seat)
    {
        foreach (var candidate in vehicle.Parts.Modules.Get<IVASeat>())
            if (ReferenceEquals(candidate, seat)) return true;
        return false;
    }

    internal static void Fail(Exception error)
    {
        Disable();
        LastError = "IVA camera unlock stopped: " + error.Message;
        Console.WriteLine("kitchen-sink: " + LastError);
    }
}
