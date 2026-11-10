using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Internal;

namespace MeowSci.YouDidntSeeAnythingLib;

/// <summary>Filters the native frame without interrupting any Begin/End or draw callbacks.</summary>
internal static unsafe class NativeWindowFilter
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void HookCallback(ImGuiContext* context, ImGuiContextHook* hook);

    private static readonly HookCallback Callback = OnHook;
    private static readonly nint CallbackPointer = Marshal.GetFunctionPointerForDelegate(Callback);
    private static readonly Dictionary<nint, WindowChange> Changes = new();
    private static readonly Dictionary<nint, uint> HiddenChanges = new();
    private static readonly HashSet<nint> AllowedLists = new();
    private static readonly HashSet<nint> StockGaugeWindows = new();
    private static ImGuiContext* _context;
    private static ImGuiID _newFrameHook, _endFrameHook, _renderHook, _shutdownHook;
    private static bool _reportedError;
    private static bool _failed;
    private static bool _clearAtRender;

    internal static bool IsReady => _context != null && !_failed;

    private readonly record struct WindowChange(uint Id, ImGuiWindowFlags AddedFlags, bool AddedDisableInputs);

    // Called from a recurring game frame hook: the native context may not exist at patch time.
    internal static void EnsureInstalled()
    {
        try
        {
            ImGuiContext* context = ImGui.GetCurrentContext();
            if (context == null || context == _context) return;
            // KSA uses one context. Do not dereference an old context after a switch: it
            // may already be destroyed. Its shutdown hook normally clears our state.
            if (_context != null)
                throw new InvalidOperationException("ImGui context changed without a shutdown hook");
            _context = context;
            _failed = false;
            try
            {
                _newFrameHook = AddHook(context, ImGuiContextHookType.NewFramePre);
                _endFrameHook = AddHook(context, ImGuiContextHookType.EndFramePre);
                _renderHook = AddHook(context, ImGuiContextHookType.RenderPost);
                _shutdownHook = AddHook(context, ImGuiContextHookType.Shutdown);
            }
            catch
            {
                if ((uint)_newFrameHook != 0) ImGui.Internal.RemoveContextHook(context, _newFrameHook);
                if ((uint)_endFrameHook != 0) ImGui.Internal.RemoveContextHook(context, _endFrameHook);
                if ((uint)_renderHook != 0) ImGui.Internal.RemoveContextHook(context, _renderHook);
                if ((uint)_shutdownHook != 0) ImGui.Internal.RemoveContextHook(context, _shutdownHook);
                ForgetContext();
                throw;
            }
        }
        catch (Exception ex) { _failed = true; Report(ex); }
    }

    internal static void Remove()
    {
        try
        {
            ImGuiContext* context = ImGui.GetCurrentContext();
            if (context != null && context == _context)
            {
                RestoreChanges(context);
                ImGui.Internal.RemoveContextHook(context, _newFrameHook);
                ImGui.Internal.RemoveContextHook(context, _endFrameHook);
                ImGui.Internal.RemoveContextHook(context, _renderHook);
                ImGui.Internal.RemoveContextHook(context, _shutdownHook);
            }
        }
        catch (Exception ex) { Report(ex); }
        finally { ForgetContext(); }
    }

    private static ImGuiID AddHook(ImGuiContext* context, ImGuiContextHookType type)
    {
        ImGuiContextHook hook = new() { Type = type, Callback = CallbackPointer };
        return ImGui.Internal.AddContextHook(context, &hook);
    }

    private static bool Filtering => IsReady && VisibilitySettings.Enabled && !KSA.Program.DrawUI && HiddenUiVisibilityPatches.IsApplied;

    private static void OnHook(ImGuiContext* context, ImGuiContextHook* hook)
    {
        try
        {
            if (context != _context) return;
            if (hook->Type == ImGuiContextHookType.RenderPost && _clearAtRender)
            {
                ClearDrawData(context);
                _clearAtRender = false;
                return;
            }
            switch (hook->Type)
            {
                case ImGuiContextHookType.NewFramePre: PrepareInput(context); break;
                case ImGuiContextHookType.EndFramePre:
                    if (Filtering || HiddenUiVisibilityPatches.IsHiddenPass) FinishFrame(context);
                    break;
                case ImGuiContextHookType.RenderPost: if (Filtering || HiddenUiVisibilityPatches.IsHiddenPass) FilterDrawData(context); break;
                case ImGuiContextHookType.Shutdown:
                    try { RestoreChanges(context); }
                    finally { ForgetContext(); }
                    break;
            }
        }
        catch (Exception ex)
        {
            bool mustClear = hook->Type != ImGuiContextHookType.NewFramePre && HiddenUiVisibilityPatches.IsHiddenPass;
            _failed = true;
            _clearAtRender = mustClear && hook->Type != ImGuiContextHookType.RenderPost;
            try
            {
                RestoreChanges(context);
                if (mustClear && hook->Type == ImGuiContextHookType.RenderPost) ClearDrawData(context);
            }
            catch { /* No exception may cross the native callback. */ }
            Report(ex);
        }
    }

    private static void PrepareInput(ImGuiContext* context)
    {
        RestoreChanges(context);
        if (!Filtering) return;
        RefreshStockGaugeWindows();
        uint mainViewport = MainViewportId(context);
        CloseExcludedPopups(context, mainViewport);
        // Let ImGui release its viewport drag state before we set NoInputs.
        if (IsExcluded(context->MovingWindow, mainViewport)) ImGui.Internal.StopMouseMovingWindow();
        for (int i = 0; i < context->Windows.Count; i++)
        {
            ImGuiWindow* window = context->Windows[i];
            if (window == null || IsAllowed(window, mainViewport)) continue;
            ImGuiWindowFlags added = ImGuiWindowFlags.NoInputs & ~window->Flags;
            bool addedDisable = window->DisableInputsFrames <= 0;
            window->Flags |= ImGuiWindowFlags.NoInputs;
            if (addedDisable) window->DisableInputsFrames = 1;
            Changes[(nint)window] = new WindowChange(window->ID, added, addedDisable);
        }

        if (IsExcluded(context->ActiveIdWindow, mainViewport)) ImGui.Internal.ClearActiveID();
        if (IsExcluded(context->NavWindow, mainViewport)) ImGui.Internal.FocusWindow(default);
        if (IsExcluded(context->NavWindowingTarget, mainViewport)) context->NavWindowingTarget = default;
        if (IsExcluded(context->NavWindowingTargetAnim, mainViewport)) context->NavWindowingTargetAnim = default;
        if (IsExcluded(context->NavWindowingListWindow, mainViewport)) context->NavWindowingListWindow = default;
    }

    private static void RestoreChanges(ImGuiContext* context)
    {
        // Only touch windows still owned by this context; addresses can be recycled after GC.
        for (int i = 0; i < context->Windows.Count && (Changes.Count > 0 || HiddenChanges.Count > 0); i++)
        {
            ImGuiWindow* window = context->Windows[i];
            if (window == null) continue;
            if (Changes.TryGetValue((nint)window, out WindowChange change) && (uint)window->ID == change.Id)
            {
                window->Flags &= ~change.AddedFlags;
                if (change.AddedDisableInputs && window->DisableInputsFrames == 1) window->DisableInputsFrames = 0;
            }
            if (HiddenChanges.TryGetValue((nint)window, out uint id) && (uint)window->ID == id)
                window->Hidden = false;
        }
        Changes.Clear();
        HiddenChanges.Clear();
    }

    private static void CloseExcludedPopups(ImGuiContext* context, uint mainViewport)
    {
        for (int i = 0; i < context->OpenPopupStack.Count; i++)
        {
            ImGuiPopupData popup = context->OpenPopupStack[i];
            ImGuiWindow* window = popup.Window;
            // A popup without a submitted window cannot yet block input. Wait until
            // Begin supplies its actual owner rather than guessing from focus history.
            if (!IsExcluded(window, mainViewport)) continue;
            ImGui.Internal.ClosePopupToLevel(i, false);
            break; // ImGui popup stacks are nested; all entries above this one belong to that chain.
        }
    }

    private static void FinishFrame(ImGuiContext* context)
    {
        RefreshStockGaugeWindows();
        uint mainViewport = MainViewportId(context);
        CloseExcludedPopups(context, mainViewport);
        // EndFrame builds PlatformIO.Viewports after this hook. Mark only an excluded
        // platform owner with no allowed window in its viewport, so UpdatePlatformWindows
        // destroys its OS window without destroying a host used by allowed content.
        for (int i = 1; i < context->Viewports.Count; i++)
        {
            ImGuiViewportP* viewport = context->Viewports[i];
            if (viewport == null) continue;
            ImGuiWindow* owner = viewport->Window;
            if (owner == null || !IsExcluded(owner, mainViewport) || owner->Hidden) continue;
            uint viewportId = ((ImGuiViewport*)viewport)->ID;
            bool allowedPresent = false;
            for (int j = 0; j < context->Windows.Count; j++)
            {
                ImGuiWindow* window = context->Windows[j];
                if (window != null && window->Active && (uint)window->ViewportId == viewportId && IsAllowed(window, mainViewport))
                {
                    allowedPresent = true;
                    break;
                }
            }
            if (!NativeWindowFilterPolicy.HidePlatformOwner(true, allowedPresent)) continue;
            HiddenChanges[(nint)owner] = owner->ID;
            owner->Hidden = true;
        }
    }

    private static bool IsExcluded(ImGuiWindow* window, uint mainViewport) => window != null && !IsAllowed(window, mainViewport);

    private static uint MainViewportId(ImGuiContext* context) => context->Viewports.Count == 0 ? 0 : ((ImGuiViewport*)context->Viewports[0].Ptr)->ID;

    private static bool IsAllowed(ImGuiWindow* window, uint mainViewport)
    {
        // Children and popups follow their Begin owner, never the dock tree (which can contain unrelated windows).
        ImGuiWindow* owner = window;
        bool stockGauge = false;
        for (int depth = 0; depth < 64 && owner != null; depth++)
        {
            stockGauge |= IsStockGauge(owner);
            ImGuiWindow* parent = owner->ParentWindowInBeginStack;
            if (parent == null || parent == owner || (owner->Flags & (ImGuiWindowFlags.ChildWindow | ImGuiWindowFlags.Popup | ImGuiWindowFlags.Tooltip)) == 0) break;
            owner = parent;
        }
        return NativeWindowFilterPolicy.Allows(VisibilitySettings.DetachedOnly, VisibilitySettings.HideGauges,
            (uint)owner->ViewportId == mainViewport, stockGauge);
    }

    private static void RefreshStockGaugeWindows()
    {
        StockGaugeWindows.Clear();
        if (!VisibilitySettings.HideGauges) return;
        foreach (KSA.GaugeCanvas canvas in KSA.GaugeCanvas.AllCanvases)
        {
            if (UiGatePolicy.DrawGauge(true, true, canvas.Id)) continue;
            ImGuiWindowPtr window = ImGui.Internal.FindWindowByName($"###{canvas.DebugTitle}");
            if (window.Ptr != null) StockGaugeWindows.Add((nint)window.Ptr);
        }
    }

    private static bool IsStockGauge(ImGuiWindow* window) => StockGaugeWindows.Contains((nint)window);

    private static void FilterDrawData(ImGuiContext* context)
    {
        uint mainViewport = MainViewportId(context);
        RefreshStockGaugeWindows();
        AllowedLists.Clear();
        for (int i = 0; i < context->Windows.Count; i++)
        {
            ImGuiWindow* window = context->Windows[i];
            if (window != null && IsAllowed(window, mainViewport) && window->DrawList.Ptr != null)
                AllowedLists.Add((nint)window->DrawList.Ptr);
        }
        // Lists not owned by a window include background/foreground HUD overlays, which stay hidden.
        for (int i = 0; i < context->Viewports.Count; i++)
        {
            ImGuiViewport* viewport = (ImGuiViewport*)context->Viewports[i].Ptr;
            if (viewport == null || viewport->DrawData.Ptr == null) continue;
            ImDrawData* data = viewport->DrawData;
            int count = data->CmdLists.Count, kept = 0, indices = 0, vertices = 0;
            for (int j = 0; j < count; j++)
            {
                ImDrawListPtr list = data->CmdLists[j];
                if (list.Ptr == null || !AllowedLists.Contains((nint)list.Ptr)) continue;
                data->CmdLists[kept++] = list;
                indices += list.Ptr->IdxBuffer.Count;
                vertices += list.Ptr->VtxBuffer.Count;
            }
            data->CmdLists.Count = kept;
            data->CmdListsCount = kept;
            data->TotalIdxCount = indices;
            data->TotalVtxCount = vertices;
        }
        AllowedLists.Clear();
    }

    private static void ClearDrawData(ImGuiContext* context)
    {
        for (int i = 0; i < context->Viewports.Count; i++)
        {
            ImGuiViewport* viewport = (ImGuiViewport*)context->Viewports[i].Ptr;
            if (viewport == null || viewport->DrawData.Ptr == null) continue;
            ImDrawData* data = viewport->DrawData;
            data->CmdLists.Count = 0;
            data->CmdListsCount = data->TotalIdxCount = data->TotalVtxCount = 0;
        }
    }

    private static void ForgetContext()
    {
        _context = null;
        _newFrameHook = _endFrameHook = _renderHook = _shutdownHook = default;
        Changes.Clear();
        HiddenChanges.Clear();
        AllowedLists.Clear();
        StockGaugeWindows.Clear();
        _clearAtRender = false;
    }

    private static void Report(Exception ex)
    {
        if (_reportedError) return;
        _reportedError = true;
        Console.WriteLine($"you-didnt-see-anything: native ImGui filter failed: {ex}");
    }
}
