using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using MeowSci.IronManLib;
using StarMap.API;

namespace MeowSci.IronMan;

[StarMapMod]
public sealed class Mod
{
    private readonly IronManSubmod _submod = new();
    private bool _initialized;
    private bool _visible;
    public bool ImmediateUnload => false;

    [StarMapImmediateLoad]
    public void OnImmediateLoad() { }

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        try { _submod.Initialize(); Patcher.Patch(); _initialized = true; }
        catch (Exception ex) { Console.WriteLine($"iron-man: initialization: {ex}"); }
    }

    [StarMapBeforeGui]
    public void OnBeforeUi(double dt)
    {
        try { if (_initialized) _submod.Update(dt); }
        catch (Exception ex) { Console.WriteLine($"iron-man: update: {ex}"); }
    }

    [StarMapAfterGui]
    public void OnAfterUi(double dt)
    {
        if (!_initialized) return;
        try
        {
            if (ImGui.IsKeyPressed(ImGuiKey.F11)) _visible = !_visible;
            if (!_visible) return;
            ImGui.SetNextWindowSize(new float2(500, 620), ImGuiCond.FirstUseEver);
            bool open = ImGui.Begin("Iron Man###iron-man"u8, ref _visible);
            try { if (open) _submod.RenderContent(); }
            finally { ImGui.End(); }
        }
        catch (Exception ex) { Console.WriteLine($"iron-man: UI: {ex}"); }
    }

    [StarMapUnload]
    public void Unload()
    {
        _initialized = false;
        try { _submod.Dispose(); }
        catch (Exception ex) { Console.WriteLine($"iron-man: cleanup: {ex}"); }
        finally { Patcher.Unload(); }
    }
}
