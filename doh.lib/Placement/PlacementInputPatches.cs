using System;
using System.Reflection;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace MeowSci.DohLib.Placement;

/// <summary>Consumes DOH placement clicks before native vehicle/controller/orbit actions.</summary>
public static class PlacementInputPatches
{
    private static readonly PlacementMouseCapture Mouse = new();
    private static MethodInfo? _mouse, _key, _mousePrefix, _keyPrefix;
    private static object? _owner;
    private static Action<float2>? _click;
    private static Action? _cancel;
    public static bool IsApplied { get; private set; }

    public static void Apply(Harmony harmony)
    {
        if (IsApplied) return;
        try
        {
            _mouse = AccessTools.Method(typeof(Program), nameof(Program.OnMouseButton));
            _key = AccessTools.Method(typeof(Program), nameof(Program.OnKey));
            _mousePrefix = AccessTools.Method(typeof(PlacementInputPatches), nameof(MousePrefix));
            _keyPrefix = AccessTools.Method(typeof(PlacementInputPatches), nameof(KeyPrefix));
            harmony.Patch(_mouse ?? throw new MissingMethodException("Program.OnMouseButton"), prefix: new HarmonyMethod(_mousePrefix));
            harmony.Patch(_key ?? throw new MissingMethodException("Program.OnKey"), prefix: new HarmonyMethod(_keyPrefix));
            IsApplied = true;
        }
        catch { Remove(harmony); throw; }
    }

    public static void Remove(Harmony harmony)
    {
        _cancel?.Invoke();
        _owner = null; _click = null; _cancel = null;
        Mouse.Clear();
        if (_mouse != null && _mousePrefix != null) harmony.Unpatch(_mouse, _mousePrefix);
        if (_key != null && _keyPrefix != null) harmony.Unpatch(_key, _keyPrefix);
        _mouse = _key = _mousePrefix = _keyPrefix = null;
        IsApplied = false;
    }

    internal static void Claim(object owner, Action<float2> click, Action cancel)
    {
        if (!ReferenceEquals(_owner, owner)) _cancel?.Invoke();
        _owner = owner; _click = click; _cancel = cancel;
    }

    internal static void Release(object owner)
    {
        if (!ReferenceEquals(_owner, owner)) return;
        _owner = null; _click = null; _cancel = null;
    }

    internal static bool WorldViewport => Universe.CurrentSystem != null && Program.DrawUI && !Program.EditorFlag
        && ReferenceEquals(Program.HoveredViewport, Program.MainViewport) && Program.MainViewport.Mode != CameraMode.Map;

    private static bool MousePrefix(GlfwMouseButton button, GlfwButtonAction action)
    {
        try
        {
            if (button != GlfwMouseButton.Number1 && button != GlfwMouseButton.Number2) return true;
            bool pressed = action == GlfwButtonAction.Press;
            bool capture = pressed && _owner != null && WorldViewport && !Program.HasMouseInputLatch
                && !ImGui.GetIO().WantCaptureMouse && !Program.ConsoleWindow.IsOpen;
            if (!Mouse.Consume((int)button, pressed, capture)) return true;
            if (pressed)
            {
                if (button == GlfwMouseButton.Number2) _cancel?.Invoke();
                else _click?.Invoke(Cursor.GetPosition(Program.MainViewport));
            }
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: placement mouse input failed: {ex}");
            _cancel?.Invoke();
            return false; // A failed owned action must not fall through into native world actions.
        }
    }

    private static bool KeyPrefix(GlfwKey key, GlfwKeyAction action)
    {
        if (_owner == null || key != GlfwKey.Escape || action != GlfwKeyAction.Press) return true;
        try { _cancel?.Invoke(); }
        catch (Exception ex) { Console.WriteLine($"doh: placement cancellation failed: {ex}"); }
        return false;
    }
}
