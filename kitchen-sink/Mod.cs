using System;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using StarMap.API;
using KSA;
using MeowSci.KitchenSinkLib;
using MeowSci.KsaAbstractions;

namespace MeowSci.KitchenSink;

[StarMapMod]
public class Mod
{
  public bool ImmediateUnload => false;

  private bool _isInitialized = false;
  private bool _isDisposed = false;
  private bool _windowVisible = false;
  private KitchenSinkSubmod _submod = null!;


  [StarMapImmediateLoad]
  public void OnImmediateLoad() { }

  [StarMapAllModsLoaded]
  public void OnFullyLoaded()
  {
    try
    {
      _submod = new KitchenSinkSubmod();
      Patcher.Patch();
      _submod.Initialize();
      _isInitialized = true;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"kitchen-sink: Error during initialization: {ex.Message}");
    }
  }

  [StarMapBeforeGui]
  public void OnBeforeUi(double dt)
  {
    if (!_isInitialized || _isDisposed) return;
    _submod.Update(dt);
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
      Console.WriteLine($"kitchen-sink: Error in OnAfterUi: {ex.Message}");
    }
  }

  [StarMapUnload]
  public void Unload()
  {
    try
    {
      IvaForceRender.Enabled = false;
      _submod?.Dispose();
      Patcher.Unload();
      _isDisposed = true;
    }
    catch (Exception ex)
    {
      Console.WriteLine($"kitchen-sink: Error during unload: {ex.Message}");
    }
  }

  private void RenderWindow()
  {
    ImGui.SetNextWindowSize(new float2(540, 440), ImGuiCond.FirstUseEver);
    if (ImGui.Begin("Kitchen Sink", ref _windowVisible))
      _submod.RenderContent();
    ImGui.End();
  }
}
