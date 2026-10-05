// Native boundaries only. Production ownership, controller, save adapter and identity resolvers
// are linked unchanged. Native FlyController motion is substituted and needs in-game acceptance.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Brutal.GlfwApi;
using Brutal.Numerics;
using RenderCore.Input;

namespace KSA
{
    public enum CameraMode { Orbit, Free, Map, IVA, Fixed }
    public enum DistanceUnit { Meters }
    public sealed partial class Vehicle
    {
        public PartTree Parts { get; } = new();
        public doubleQuat Body2Cce = doubleQuat.Identity;
        public doubleQuat Asmb2Cce => Body2Cce;
        public int InputClears;
        public void ClearHeldPlayerInput() => InputClears++;
        public double3 PosAsmbToBody(double3 position) => position;
    }
    public sealed class PartTree
    {
        public Part Root { get; } = new();
        public Modules Modules => Root.Modules;
    }
    public sealed class Part
    {
        public PartTemplate Template { get; } = new();
        public List<Part> TreeChildren { get; } = new();
        public Part[] SubParts = Array.Empty<Part>();
        public Modules Modules { get; } = new();
        public doubleQuat Asmb2VehicleAsmb => doubleQuat.Identity;
        public double3 PositionVehicleAsmbOffset(double3 position) => position;
    }
    public sealed class PartTemplate { public string Id { get; set; } = "cabin"; }
    public sealed class Modules
    {
        private readonly Dictionary<Type, object> _values = new();
        public Span<T> Get<T>() => _values.TryGetValue(typeof(T), out var list)
            ? CollectionsMarshal.AsSpan((List<T>)list) : Span<T>.Empty;
        public void Add<T>(T value)
        {
            if (!_values.TryGetValue(typeof(T), out var list)) _values[typeof(T)] = list = new List<T>();
            ((List<T>)list).Add(value);
        }
    }
    public class Camera
    {
        public Vehicle? Following;
        public double3 LocalPosition;
        public doubleQuat LocalRotation = doubleQuat.Identity;
        public double3 PositionCce
        {
            get => double3.Transform(LocalPosition, Following?.Body2Cce ?? doubleQuat.Identity);
            set => LocalPosition = double3.Transform(value, doubleQuat.Inverse(Following?.Body2Cce ?? doubleQuat.Identity));
        }
        public static doubleQuat LookAtRotation(double3 forward, double3 up) => doubleQuat.Identity;
    }
    public interface IViewport { CameraMode Mode { get; } Camera GetCamera(); }
    public interface IGameViewport : IViewport { IVAController IvaController { get; } }
    public class GameViewport : IGameViewport
    {
        private readonly Camera _camera;
        public IVAController IvaController { get; protected set; }
        public CameraMode Mode { get; private set; } = CameraMode.IVA;
        public GameViewport(Camera camera) { _camera = camera; IvaController = new IVAController(camera); }
        public Camera GetCamera() => _camera;
        public void ReplaceIva(IVAController controller) => IvaController = controller;
        public void SetCameraMode(CameraMode mode) { IvaController.OnSwitchOff(mode); Mode = mode; }
    }
    public class Controller(Camera camera)
    {
        public Camera Camera = camera;
        public virtual void OnFrame(IViewport viewport, double dt) { }
        public virtual void OnSwitchOff(CameraMode mode) { }
        public virtual bool OnKey(GlfwKeyEvent e) => false;
        public virtual bool OnMouseButton(GlfwWindow window, GlfwMouseButton button, GlfwButtonAction action, GlfwModifier mods) => false;
        public virtual bool OnCursorPos(GlfwWindow window, double2 pos) => false;
        public virtual bool OnScroll(GlfwWindow window, double2 offset) => false;
        public virtual GlfwCursorMode GetCursorMode() => GlfwCursorMode.Normal;
        public virtual bool IsMouseDrag() => false;
        public virtual void CancelMouseDrag() { }
    }
    public class IVAController(Camera camera, string name = "IVA") : Controller(camera)
    {
        public string Name = name;
        public IVASeat Seat = null!;
        public Vehicle LastFollowing = null!;
        public doubleQuat LastLocalRotation, LastFollowingAsmb2Cce;
        public double2[] LastCursorDiffs = new double2[3];
        public bool SwitchThisFrame;
        public override void OnFrame(IViewport viewport, double dt) => Camera.LocalPosition = Seat.PositionAsmb;
    }
    public class FlyController(Camera camera, string name = "Fly") : Controller(camera)
    {
        public string Name = name;
        private bool _moving;
        private double _speed;
        public static int Frames;
        public void SetSpeed(double speed, DistanceUnit unit) => _speed = speed;
        public void CacheOffset() { }
        public override void OnFrame(IViewport viewport, double dt)
        {
            Frames++;
            if (_moving) Camera.LocalPosition += new double3(_speed * dt, 0, 0);
        }
        public override bool OnKey(GlfwKeyEvent e) { _moving = e.Pressed; return true; }
        public override void OnSwitchOff(CameraMode mode) => _moving = false;
    }
    public sealed class IVASeat
    {
        public Part Parent = null!;
        public double3 PositionAsmb;
        public double3 ForwardAxisAsmb = double3.UnitX;
        public double3 UpAxisAsmb = double3.UnitZ;
        public bool HeadHidden(IViewport viewport) => IsCameraInThisSeat(viewport);
        [MethodImpl(MethodImplOptions.NoInlining)]
        private bool IsCameraInThisSeat(IViewport viewport) => viewport.Mode == CameraMode.IVA
            && viewport is IGameViewport game && ReferenceEquals(game.IvaController.Seat, this);
    }
}

namespace Brutal.GlfwApi
{
    public readonly struct GlfwWindow { }
    public enum GlfwMouseButton { Number1 }
    public enum GlfwButtonAction { Press, Release }
    public enum GlfwModifier { None }
    public enum GlfwCursorMode { Normal, Disabled }
}
namespace RenderCore.Input { public readonly record struct GlfwKeyEvent(bool Pressed); }
namespace Brutal.ImGuiApi
{
    public static class ImGui
    {
        public sealed class IO { public bool WantCaptureKeyboard, WantTextInput; }
        private static readonly IO _io = new();
        public static IO GetIO() => _io;
    }
}
