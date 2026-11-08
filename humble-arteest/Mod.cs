using System;
using System.Collections.Generic;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using StarMap.API;
using MeowSci.KsaAbstractions;
using MeowSci.HumbleArteestLib;

namespace MeowSci.HumbleArteest;

[StarMapMod]
public class Mod
{
    public bool ImmediateUnload => false;

    private bool _isInitialized;
    private bool _isDisposed;
    private bool _windowVisible;

    private readonly List<ISubmod> _submods = new();

    [StarMapImmediateLoad]
    public void OnImmediateLoad() { }

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        try
        {
            _submods.Add(new HumbleArteestSubmod());

            foreach (var submod in _submods)
                submod.Initialize();

            Patcher.Patch();
            _isInitialized = true;
            Console.WriteLine($"humble-arteest: Initialized with {_submods.Count} features");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"humble-arteest: Error during initialization: {ex.Message}");
        }
    }

    [StarMapBeforeGui]
    public void OnBeforeUi(double dt)
    {
        if (!_isInitialized || _isDisposed) return;

        foreach (var submod in _submods)
        {
            try { submod.Update(dt); }
            catch (Exception ex) { Console.WriteLine($"humble-arteest/{submod.Name}: Update error: {ex.Message}"); }
        }
    }

    [StarMapAfterGui]
    public void OnAfterUi(double dt)
    {
        try
        {
            if (!_isInitialized || _isDisposed) return;

            if (ImGui.IsKeyPressed(ImGuiKey.F11))
                _windowVisible = !_windowVisible;

            if (_windowVisible)
                RenderWindow();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"humble-arteest: Error in OnAfterUi: {ex.Message}");
        }
    }

    [StarMapUnload]
    public void Unload()
    {
        try
        {
            foreach (var submod in _submods)
            {
                try { submod.Dispose(); }
                catch (Exception ex) { Console.WriteLine($"humble-arteest/{submod.Name}: Dispose error: {ex.Message}"); }
            }

            Patcher.Unload();
            _isDisposed = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"humble-arteest: Error during unload: {ex.Message}");
        }
    }

    private void RenderWindow()
    {
        ImGui.SetNextWindowSize(new float2(550, 600), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Humble Arteest", ref _windowVisible))
        {
            foreach (var submod in _submods)
            {
                try { submod.RenderContent(); }
                catch (Exception ex)
                {
                    ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), $"{submod.Name} error: {ex.Message}");
                }
                ImGui.Separator();
            }
        }
        ImGui.End();
    }
}
