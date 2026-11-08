using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using MeowSci.WarPaintLib;
using StarMap.API;

namespace MeowSci.WarPaint;

[StarMapMod]
public sealed class Mod
{
    public bool ImmediateUnload => false;

    private readonly WarPaintSubmod _submod = new();
    private bool _initialized;
    private bool _disposed;
    private bool _windowVisible;

    [StarMapImmediateLoad]
    public void OnImmediateLoad() { }

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        try
        {
            Patcher.Patch();
            _submod.Initialize();
            _initialized = true;
        }
        catch (Exception ex) { Console.WriteLine($"war-paint: Initialization failed: {ex}"); }
    }

    [StarMapBeforeGui]
    public void OnBeforeUi(double dt)
    {
        if (!_initialized || _disposed) return;
        try { _submod.Update(dt); }
        catch (Exception ex) { Console.WriteLine($"war-paint: Update failed: {ex}"); }
    }

    [StarMapAfterGui]
    public void OnAfterUi(double dt)
    {
        if (!_initialized || _disposed) return;
        try
        {
            if (ImGui.IsKeyPressed(ImGuiKey.F11)) _windowVisible = !_windowVisible;
            if (!_windowVisible) return;
            ImGui.SetNextWindowSize(new float2(620, 720), ImGuiCond.FirstUseEver);
            bool visible = ImGui.Begin("War Paint###war-paint"u8, ref _windowVisible);
            try { if (visible) _submod.RenderContent(); }
            finally { ImGui.End(); }
        }
        catch (Exception ex) { Console.WriteLine($"war-paint: UI failed: {ex}"); }
    }

    [StarMapUnload]
    public void Unload()
    {
        if (_disposed) return;
        _disposed = true;
        try { _submod.Dispose(); }
        finally { Patcher.Unload(); }
    }
}
