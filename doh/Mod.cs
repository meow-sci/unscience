using System;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using StarMap.API;
using MeowSci.DohLib;

namespace MeowSci.Doh;

[StarMapMod]
public class Mod
{
    public bool ImmediateUnload => false;

    private bool _isInitialized;
    private bool _isDisposed;
    private bool _windowVisible;

    private readonly DohSubmod _submod = new();

    [StarMapImmediateLoad]
    public void OnImmediateLoad() { }

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        try
        {
            Patcher.Patch();
            _submod.Initialize();
            _isInitialized = true;
            Console.WriteLine("doh: Initialized");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: Error during initialization: {ex.Message}");
        }
    }

    [StarMapBeforeGui]
    public void OnBeforeUi(double dt)
    {
        try
        {
            if (_isInitialized && !_isDisposed)
                _submod.Update(dt);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: Error in OnBeforeUi: {ex.Message}");
        }
    }

    [StarMapAfterGui]
    public void OnAfterUi(double dt)
    {
        try
        {
            if (!_isInitialized || _isDisposed) return;

            if (ImGui.IsKeyPressed(ImGuiKey.F8))
                _windowVisible = !_windowVisible;

            if (_windowVisible)
                RenderWindow();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: Error in OnAfterUi: {ex.Message}");
        }
    }

    [StarMapUnload]
    public void Unload()
    {
        try
        {
            _submod.Dispose();
            Patcher.Unload();
            _isDisposed = true;
            Console.WriteLine("doh: Unloaded");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"doh: Error during unload: {ex.Message}");
        }
    }

    private void RenderWindow()
    {
        ImGui.SetNextWindowSize(new float2(480, 560), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("DOH — Kitten Spawner###doh-window", ref _windowVisible))
        {
            ImGui.End();
            return;
        }

        _submod.RenderContent();
        ImGui.End();
    }
}
