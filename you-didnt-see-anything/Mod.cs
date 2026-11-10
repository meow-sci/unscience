using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using StarMap.API;
using MeowSci.YouDidntSeeAnythingLib;

namespace MeowSci.YouDidntSeeAnything;

/// <summary>Legacy standalone host; the shipped distribution uses the Unscience toolbox.</summary>
[StarMapMod]
public sealed class Mod
{
    private readonly YouDidntSeeAnythingSubmod _submod = new();
    private bool _visible;
    private bool _initialized;
    public bool ImmediateUnload => false;

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        try { _submod.Initialize(); Patcher.Patch(); _initialized = true; }
        catch (Exception ex) { Console.WriteLine($"you-didnt-see-anything: Initialization failed: {ex}"); }
    }

    [StarMapAfterGui]
    public void OnAfterUi(double dt)
    {
        try
        {
            if (!_initialized) return;
            if (ImGui.IsKeyPressed(ImGuiKey.F11)) _visible = !_visible;
            if (!_visible) return;
            ImGui.SetNextWindowSize(new float2(470, 220), ImGuiCond.FirstUseEver);
            bool begun = false;
            try
            {
                bool showContent = ImGui.Begin("You Didn't See Anything", ref _visible);
                begun = true;
                if (showContent) _submod.RenderContent();
            }
            finally { if (begun) ImGui.End(); }
        }
        catch (Exception ex) { Console.WriteLine($"you-didnt-see-anything: UI error: {ex}"); }
    }

    [StarMapUnload]
    public void Unload()
    {
        try { Patcher.Unload(); }
        catch (Exception ex) { Console.WriteLine($"you-didnt-see-anything: Unload failed: {ex}"); }
        finally { _submod.Dispose(); _initialized = false; }
    }

}
