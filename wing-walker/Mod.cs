using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using MeowSci.WingWalkerLib;
using StarMap.API;

namespace MeowSci.WingWalker;

[StarMapMod]
public sealed class Mod
{
    private readonly WingWalkerSubmod _submod = new();
    private bool _initialized;
    private bool _visible;
    public bool ImmediateUnload => false;

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        try
        {
            _submod.Initialize();
            Patcher.Patch();
            _initialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"wing-walker: initialization: {ex}");
            try { _submod.Dispose(); }
            catch (Exception cleanupEx) { Console.WriteLine($"wing-walker: cleanup: {cleanupEx}"); }
        }
    }

    [StarMapBeforeGui]
    public void OnBeforeUi(double dt)
    {
        try { if (_initialized) _submod.Update(dt); }
        catch (Exception ex) { Console.WriteLine($"wing-walker: update: {ex}"); }
    }

    [StarMapAfterGui]
    public void OnAfterUi(double dt)
    {
        if (!_initialized) return;
        try
        {
            if (ImGui.IsKeyPressed(ImGuiKey.F11)) _visible = !_visible;
            if (!_visible) return;
            ImGui.SetNextWindowSize(new float2(500, 400), ImGuiCond.FirstUseEver);
            bool open = ImGui.Begin("Wing Walker###wing-walker"u8, ref _visible);
            try { if (open) _submod.RenderContent(); }
            finally { ImGui.End(); }
        }
        catch (Exception ex) { Console.WriteLine($"wing-walker: UI: {ex}"); }
    }

    [StarMapUnload]
    public void Unload()
    {
        _initialized = false;
        try { _submod.Dispose(); }
        catch (Exception ex) { Console.WriteLine($"wing-walker: cleanup: {ex}"); }
        finally { Patcher.Unload(); }
    }
}
