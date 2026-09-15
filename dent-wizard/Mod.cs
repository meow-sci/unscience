using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using MeowSci.DentWizardLib;
using StarMap.API;

namespace MeowSci.DentWizard;

[StarMapMod]
public sealed class Mod
{
    public bool ImmediateUnload => false;

    private DentWizardSubmod _submod = null!;
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
            _submod = new DentWizardSubmod();
            _submod.Initialize();
            Patcher.Patch();
            _initialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"dent-wizard: Error during initialization: {ex.Message}");
        }
    }

    [StarMapBeforeGui]
    public void OnBeforeUi(double dt)
    {
        if (!_initialized || _disposed)
            return;
        try { _submod.Update(dt); }
        catch (Exception ex) { Console.WriteLine($"dent-wizard: Update error: {ex.Message}"); }
    }

    [StarMapAfterGui]
    public void OnAfterUi(double dt)
    {
        try
        {
            if (!_initialized || _disposed)
                return;
            if (ImGui.IsKeyPressed(ImGuiKey.F11))
                _windowVisible = !_windowVisible;
            if (_windowVisible)
            {
                ImGui.SetNextWindowSize(new float2(560f, 650f), ImGuiCond.FirstUseEver);
                if (ImGui.Begin("Dent Wizard###dent-wizard", ref _windowVisible))
                    _submod.RenderContent();
                ImGui.End();
            }
            _submod.RenderFloatingWindows();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"dent-wizard: Error in OnAfterUi: {ex.Message}");
        }
    }

    [StarMapUnload]
    public void Unload()
    {
        try
        {
            Patcher.Unload();
            _submod?.Dispose();
            _disposed = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"dent-wizard: Error during unload: {ex.Message}");
        }
    }
}
