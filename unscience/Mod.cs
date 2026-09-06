using System;
using System.Collections.Generic;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using StarMap.API;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.BlinkyLib;
using MeowSci.EternalFlameLib;
using MeowSci.GarrysTorchLib;
using MeowSci.GlassLib;
using MeowSci.IFeelSeenLib;
using MeowSci.ItsSoShinyLib;
using MeowSci.CameraControllerOverrideLib;
using MeowSci.KittenAnimationsLib;
using MeowSci.KiwisMarblesLib;
using MeowSci.SkittlesLib;
using MeowSci.ZippoLib;
using MeowSci.HumbleArteestLib;
using MeowSci.DohLib;
using MeowSci.KitchenSinkLib;
using MeowSci.PartsNowLib;
using MeowSci.ThugLifeLib;
using MeowSci.DontStifleMeLib;
using MeowSci.GraffitiLib;
using MeowSci.FreeFallinLib;
using MeowSci.HotPursuitLib;
using MeowSci.PyroLib;
using MeowSci.PebblesLib;
using MeowSci.RockyMcRockFaceLib;
using MeowSci.BloominOnionLib;

namespace MeowSci.Unscience;

[StarMapMod]
public class Mod
{
    public bool ImmediateUnload => false;

    private bool _isInitialized = false;
    private bool _isDisposed = false;
    private bool _windowVisible = false;

    private readonly List<ISubmod> _submods = new();
    private readonly Dictionary<string, bool> _submodVisibility = new();
    private readonly Dictionary<string, bool> _headerOpen = new();
    private bool _collapseAll;
    private bool _expandAll;
    private double _timeSinceLastSave;
    private bool _autoSaveEnabled = true;
    private bool _showModTooltips = true;

    [StarMapImmediateLoad]
    public void OnImmediateLoad() { }

    [StarMapAllModsLoaded]
    public void OnFullyLoaded()
    {
        try
        {
            // Create all submods in display order
            var iFeelSeen = new IFeelSeenSubmod();
            var skittles = new SkittlesSubmod();
            var cameraOverride = new CameraControllerOverrideSubmod();

            _submods.Add(new BlinkySubmod());
            _submods.Add(new BloominOnionSubmod());
            _submods.Add(cameraOverride);
            _submods.Add(new DohSubmod());
            _submods.Add(new DontStifleMeSubmod());
            _submods.Add(new EternalFlameSubmod());
            _submods.Add(new FreeFallinSubmod());
            _submods.Add(new GarrysTorchSubmod());
            _submods.Add(new GlassSubmod());
            _submods.Add(new GraffitiSubmod());
            _submods.Add(new HotPursuitSubmod());
            _submods.Add(new HumbleArteestSubmod());
            _submods.Add(iFeelSeen);
            _submods.Add(new ItsSoShinySubmod());
            _submods.Add(new KitchenSinkSubmod());
            _submods.Add(new KittenAnimationsSubmod());
            _submods.Add(new KiwisMarblesSubmod());
            _submods.Add(new PartsNowSubmod());
            var pebbles = new PebblesSubmod();
            _submods.Add(pebbles);
            _submods.Add(new PyroSubmod());
            _submods.Add(new RockyMcRockFaceSubmod());
            _submods.Add(skittles);
            _submods.Add(new ThugLifeSubmod());
            _submods.Add(new ZippoSubmod());

            // Initialize all submods so Tracker is populated before patching
            foreach (var submod in _submods)
            {
                submod.Initialize();
                _submodVisibility[submod.Name] = true;
            }

            // Restore persisted state
            UnscienceState.LoadImGuiWindowState();
            var (savedHeaders, savedVisibility) = UnscienceState.LoadSubmodState();
            foreach (var kvp in savedHeaders)
                _headerOpen[kvp.Key] = kvp.Value;
            foreach (var kvp in savedVisibility)
                if (_submodVisibility.ContainsKey(kvp.Key))
                    _submodVisibility[kvp.Key] = kvp.Value;
            _autoSaveEnabled = UnscienceState.AutoSaveEnabled;
            _showModTooltips = UnscienceState.ShowModTooltips;

            // Wire up Patcher dependencies and apply patches
            Patcher.PebblesController = pebbles.Controller;
            Patcher.IFeelSeenTracker = iFeelSeen.Tracker;
            Patcher.CameraSequencePlayer = cameraOverride.SequencePlayer;
            Patcher.MenuBarToggle = () => _windowVisible = !_windowVisible;

            // While the game HUD is hidden (F2) StarMap's [StarMapBeforeGui]/[StarMapAfterGui]
            // never fire (their game targets are skipped), so the non-UI per-frame work is
            // replayed from HiddenUiFrameHook at the same frame phase. ImGui rendering is
            // deliberately left out so mod windows honour the hidden HUD too.
            HiddenUiFrameHook.BeforeGui = UpdateSubmods;

            Patcher.Patch();

            _isInitialized = true;
            Console.WriteLine($"unscience: Initialized with {_submods.Count} submods");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience: Error during initialization: {ex.Message}");
        }
    }

    [StarMapBeforeGui]
    public void OnBeforeUi(double dt) => UpdateSubmods(dt);

    /// <summary>
    /// Per-frame pre-UI work. Called by <see cref="OnBeforeUi"/> when the HUD is visible and by
    /// <see cref="HiddenUiFrameHook"/> when it is hidden — never both in the same frame.
    /// </summary>
    private void UpdateSubmods(double dt)
    {
        if (!_isInitialized || _isDisposed) return;

        // Update ALL submods every frame, even hidden ones
        foreach (var submod in _submods)
        {
            try { submod.Update(dt); }
            catch (Exception ex) { Console.WriteLine($"unscience/{submod.Name}: Update error: {ex.Message}"); }
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
            {
                RenderWindow();

                if (_autoSaveEnabled)
                {
                    _timeSinceLastSave += dt;
                    if (_timeSinceLastSave >= UnscienceState.SaveIntervalSeconds)
                    {
                        _timeSinceLastSave = 0;
                        SaveAll();
                    }
                }
            }

            // Floating windows are always rendered so they remain visible when the
            // unscience window is hidden (e.g. while the Space Tape part editor is open).
            foreach (var submod in _submods)
            {
                try { submod.RenderFloatingWindows(); }
                catch (Exception ex) { Console.WriteLine($"unscience/{submod.Name}: RenderFloatingWindows error: {ex.Message}"); }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience: Error in OnAfterUi: {ex.Message}");
        }
    }

    [StarMapUnload]
    public void Unload()
    {
        try
        {
            if (_autoSaveEnabled)
                SaveAll();

            foreach (var submod in _submods)
            {
                try { submod.Dispose(); }
                catch (Exception ex) { Console.WriteLine($"unscience/{submod.Name}: Dispose error: {ex.Message}"); }
            }

            Patcher.Unload();
            _isDisposed = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"unscience: Error during unload: {ex.Message}");
        }
    }

    private void RenderWindow()
    {
        ImGui.SetNextWindowSize(new float2(600, 800), ImGuiCond.FirstUseEver);

        if (ImGui.Begin("Unscience Toolbox", ref _windowVisible, ImGuiWindowFlags.MenuBar))
        {
            // Menu bar
            if (ImGui.BeginMenuBar())
            {
                if (ImGui.BeginMenu("View"))
                {
                    ImGui.PushItemFlag(ImGuiItemFlags.AutoClosePopups, false);

                    if (ImGui.MenuItem("Show All"))
                        foreach (var s in _submods)
                            _submodVisibility[s.Name] = true;
                    if (ImGui.MenuItem("Hide All"))
                        foreach (var s in _submods)
                            _submodVisibility[s.Name] = false;
                    ImGui.Separator();

                    if (ImGui.MenuItem("Submod Tooltips", "", ref _showModTooltips))
                        UnscienceState.ShowModTooltips = _showModTooltips;
                    ImGui.Separator();

                    var sorted = new List<ISubmod>(_submods);
                    sorted.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                    foreach (var s in sorted)
                    {
                        bool visible = _submodVisibility[s.Name];
                        if (ImGui.MenuItem(s.Name, "", ref visible))
                            _submodVisibility[s.Name] = visible;
                    }

                    ImGui.PopItemFlag();
                    ImGui.EndMenu();
                }

                if (ImGui.MenuItem("Collapse"))
                    _collapseAll = true;
                if (ImGui.MenuItem("Expand"))
                    _expandAll = true;

                if (ImGui.BeginMenu("State"))
                {
                    if (ImGui.MenuItem("Auto save enabled", "", ref _autoSaveEnabled))
                        UnscienceState.AutoSaveEnabled = _autoSaveEnabled;

                    ImGui.PushItemWidth(120f);
                    int interval = UnscienceState.SaveIntervalSeconds;
                    if (ImGui.DragInt("Auto-save interval (s)", ref interval, 1.0f, 1, 30))
                        UnscienceState.SaveIntervalSeconds = interval;
                    ImGui.PopItemWidth();

                    if (ImGui.MenuItem("Save window state now"))
                    {
                        _timeSinceLastSave = 0;
                        SaveAll();
                    }
                    ImGui.EndMenu();
                }

                ImGui.EndMenuBar();
            }

            // Render visible submods
            foreach (var submod in _submods)
            {
                if (!_submodVisibility[submod.Name]) continue;

                if (_expandAll)
                    ImGui.SetNextItemOpen(true, ImGuiCond.Always);
                else if (_collapseAll)
                    ImGui.SetNextItemOpen(false, ImGuiCond.Always);
                else
                    ImGui.SetNextItemOpen(_headerOpen.GetValueOrDefault(submod.Name, false), ImGuiCond.Once);

                var headerLabel = _showModTooltips ? $"{submod.Name}  (?)" : submod.Name;
                bool isOpen = ImGui.CollapsingHeader(headerLabel, ImGuiTreeNodeFlags.None);
                _headerOpen[submod.Name] = isOpen;
                if (_showModTooltips && ImGui.IsItemHovered(ImGuiHoveredFlags.DelayNormal))
                    ImGui.SetTooltip(submod.Tooltip);

                if (isOpen)
                {
                    try { submod.RenderContent(); }
                    catch (Exception ex) { ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), $"Error: {ex.Message}"); }
                }
                ImGui.Separator();
            }
            _collapseAll = false;
            _expandAll = false;

            // Close button
            ImGui.Spacing();
            if (ImGui.Button("Close##unscience"))
                _windowVisible = false;
        }
        ImGui.End();
    }

    private void SaveAll()
    {
        UnscienceState.SaveImGuiWindowState();
        UnscienceState.SaveSubmodState(_headerOpen, _submodVisibility);
    }
}
