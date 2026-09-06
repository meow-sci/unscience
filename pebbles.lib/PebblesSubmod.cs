using System;
using System.Linq;
using MeowSci.KsaAbstractions;

namespace MeowSci.PebblesLib;

public sealed partial class PebblesSubmod : ISubmod
{
    public string Name => "Pebbles — Ground Clutter";
    public string Tooltip => "Pick a mesh or import a GLB, set its scale and colliders, and replace selected planet clutter types.";
    public ClutterController Controller => _controller;
    private readonly ClutterAssets _assets = new();
    private readonly ClutterController _controller;
    private readonly WorkshopEditor _workshop = new();
    private PebblesRecipe _recipe = new();
    private string _bodyId = "", _message = "";
    private double _refreshTime, _libraryRefreshTime;
    public PebblesSubmod() { _controller = new ClutterController(_assets); }
    public void Initialize() => Console.WriteLine("pebbles: initialized");
    public void Update(double dt)
    {
        _controller.Update();
        _workshop.Update();
        if (_releaseImports && !_controller.NeedsHooks && _controller.Faults.Count == 0)
        {
            _releaseImports = false; // A failed native release is reported, never retried every frame.
            _workshop.Release(); _workshop.Update();
            _assets.ReleaseGlbImports(); _glbOptions = [];
        }
        _libraryRefreshTime -= dt;
        if (_libraryRefreshTime <= 0) { _libraryRefreshTime = 2; _assets.RefreshSharedLibrary(); }
        _refreshTime -= dt;
        if (_refreshTime > 0) return;
        _refreshTime = 5;
        Try(() => { _controller.Refresh(); if (!_assets.RegistryDiscovered) _assets.Refresh(); });
    }
    public void RenderFloatingWindows()
    {
        _workshop.SetCompletion(CompleteWorkshop);
        _workshop.Draw(_assets);
        _glbBrowser.Render(name => ImportAttempt(() => ImportGlb(GlbLibrary.Files.FullPath(name))));
    }
    private void CompleteWorkshop(ObjectRecipe value)
    {
        _replacement = RecipeCopy.Clone(value);
    }
    public void ReleaseAll() { _controller.Release(); _workshop.Release(); _releaseImports = true; }
    public void Dispose()
    {
        _workshop.Dispose(); _controller.Dispose();
        if (_assets.ImportedGlbCount > 0 && _controller.Faults.Count != 0)
            throw new InvalidOperationException("GLB imports retained because native clutter retirement failed; restart the game to reclaim them.");
        _assets.Dispose();
    }
    private void Try(Action action)
    {
        try { _message = ""; action(); }
        catch (Exception ex) { _message = ex.Message; Console.WriteLine($"pebbles: {ex}"); }
    }
}
