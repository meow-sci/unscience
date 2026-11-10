using System;
using System.Reflection;
using HarmonyLib;
using KSA;

namespace MeowSci.KsaAbstractions;

/// <summary>
/// Keeps a mod's per-frame work alive while the game HUD is hidden (F2 / <c>InputAction.ToggleUi</c>).
/// </summary>
/// <remarks>
/// StarMap dispatches <c>[StarMapBeforeGui]</c> as a prefix of <c>Program.OnDrawUiFrame</c> and
/// <c>[StarMapAfterGui]</c> as a postfix of <c>Program.OnDrawUiViewports</c>. Both game methods sit
/// inside <c>if (Program.DrawUI)</c> in <c>Program.OnFrame</c>, so when the HUD is hidden neither is
/// called and neither StarMap hook fires — every <c>Update(dt)</c>-driven feature silently freezes.
///
/// <c>Program.OnDrawUiConsole(double dt)</c> is called unconditionally in the same frame phase (after
/// <c>PrepareFrame</c>, inside the ImGui <c>NewFrame</c>…<c>Render</c> window, before <c>OnPreRender</c>),
/// immediately after the gated block. A prefix on it fires only while <c>DrawUI</c> is false and replays
/// the registered <see cref="BeforeGui"/> then <see cref="AfterGui"/> callbacks, so ordering guarantees the
/// submods rely on (solver-queue timing, ImGui frame validity, GPU submit phase) are unchanged.
///
/// The optional dispatch predicate consumes a marker captured at the start of the UI phase;
/// it remains correct even if a UI checkbox changes the feature setting during that phase.
/// </remarks>
public static class HiddenUiFrameHook
{
    private const string TargetMethodName = "OnDrawUiConsole";

    private static MethodInfo? _original;
    private static MethodInfo? _prefix;

    /// <summary>Work normally done in <c>[StarMapBeforeGui]</c>. Invoked only while the HUD is hidden.</summary>
    public static Action<double>? BeforeGui { get; set; }

    /// <summary>Work normally done in <c>[StarMapAfterGui]</c>. Invoked only while the HUD is hidden, after <see cref="BeforeGui"/>.</summary>
    public static Action<double>? AfterGui { get; set; }

    /// <summary>True when another patch dispatched the normal UI callbacks while F2 is active.</summary>
    public static Func<bool>? UiCallbacksDispatched { get; set; }

    /// <summary>True while the game HUD is hidden (F2).</summary>
    public static bool IsUiHidden => !Program.DrawUI;

    public static void Patch(Harmony harmony)
    {
        _original = AccessTools.Method(typeof(Program), TargetMethodName)
            ?? throw new MissingMethodException(typeof(Program).FullName, TargetMethodName);
        _prefix = typeof(HiddenUiFrameHook).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)!;
        harmony.Patch(_original, prefix: new HarmonyMethod(_prefix));
        Console.WriteLine("ksa-abstractions: HiddenUiFrameHook patch applied");
    }

    public static void Unpatch(Harmony harmony)
    {
        if (_original != null && _prefix != null)
            harmony.Unpatch(_original, _prefix);
        _original = null;
        _prefix = null;
        UiCallbacksDispatched = null;
        BeforeGui = null;
        AfterGui = null;
        Console.WriteLine("ksa-abstractions: HiddenUiFrameHook patch removed");
    }

    private static void Prefix(double dt)
    {
        if (Program.DrawUI) return; // HUD visible: StarMap already fired the real hooks this frame

        bool suppressUpdate = UiCallbacksDispatched?.Invoke() == true;
        try { if (!suppressUpdate) BeforeGui?.Invoke(dt); }
        catch (Exception ex) { Console.WriteLine($"ksa-abstractions: HiddenUiFrameHook BeforeGui error: {ex.Message}"); }

        try { if (!suppressUpdate) AfterGui?.Invoke(dt); }
        catch (Exception ex) { Console.WriteLine($"ksa-abstractions: HiddenUiFrameHook AfterGui error: {ex.Message}"); }
    }
}
