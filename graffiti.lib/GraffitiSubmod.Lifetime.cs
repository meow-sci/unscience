using System;
using KSA;

namespace MeowSci.GraffitiLib;

public sealed partial class GraffitiSubmod
{
    public void Dispose()
    {
        _renderActive = false;
        _decals.Clear();
        _published = Array.Empty<DecalEntry>();
        FreeGpu(releaseTextures: true);
        if (ReferenceEquals(Instance, this)) Instance = null;
    }

    /// <summary>Drain and release only resources this feature actually acquired.</summary>
    private void FreeGpu(bool releaseTextures = false)
    {
        if (_renderer == null && (!releaseTextures || !_textures.HasResources)) return;
        _renderActive = false;
        WaitIdle();
        var renderer = _renderer;
        _renderer = null;
        try { renderer?.Dispose(); }
        catch (Exception ex) { Console.WriteLine($"graffiti: renderer dispose failed: {ex}"); }
        if (releaseTextures) _textures.DisposeAll();
    }

    // Called on the game thread while its device is live (before native Program.Dispose on shutdown).
    private static void WaitIdle()
    {
        try { KSA.Program.GetRenderer()?.GraphicsAndCompute?.WaitIdle(); }
        catch (Exception ex) { Console.WriteLine($"graffiti: teardown wait failed: {ex}"); }
    }
}
