// THREADING RULE (repeated in every parts-now file):
// Everything runs on the game thread except RuntimeModLoader's loader step, which runs on a
// Task.Run worker. The worker touches only ILoader.Load(). Completion is polled from Update(dt).
// Do not introduce background access to KSA state; parts-now must remain safe standalone.

using System;
using System.Collections.Generic;
using Brutal.ImGuiApi;
using MeowSci.KsaAbstractions;

namespace MeowSci.PartsNowLib;

/// <summary>
/// parts-now entry point: runtime Part / SubPart loading from pasted XML or an existing mod folder.
/// Hosted by the unscience supermod and by the standalone <c>parts-now</c> mod.
/// </summary>
public sealed partial class PartsNowSubmod : ISubmod
{
    private readonly List<string> _selfTestProblems = new List<string>();

    private readonly StatusPanel _statusPanel = new StatusPanel();
    private readonly PastePanel _pastePanel = new PastePanel();
    private readonly ModFolderPanel _modFolderPanel = new ModFolderPanel();
    private readonly ResultsPanel _resultsPanel = new ResultsPanel();

    private bool _firstFrameDone;

    /// <inheritdoc />
    public string Name => "Parts Now";

    /// <inheritdoc />
    public string Tooltip =>
        "Load Parts and SubParts into a running game — paste XML into a new mod folder, "
        + "or load / reload / unload an existing mod folder without restarting.";

    /// <summary>
    /// Problems found by <see cref="GameRegistry.SelfTest" /> during <see cref="Initialize" />.
    /// Non-empty means a KSA update moved something and loading must stay disabled.
    /// </summary>
    public IReadOnlyList<string> SelfTestProblems => _selfTestProblems;

    /// <summary>True when parts-now is able to load mods at all.</summary>
    public bool CanLoad => GameRegistry.IsHealthy && MeshBudget.IsUsable;

    /// <summary>
    /// Called from <c>[StarMapAllModsLoaded]</c>, which StarMap fires as a Harmony postfix on
    /// <c>ModLibrary.LoadAll()</c> — i.e. BEFORE <c>ModLibrary.Bind()</c> allocates the shared
    /// interleaved mesh buffers. That ordering is what makes <see cref="MeshBudget.Reserve" /> work,
    /// so this must never be moved to a later hook.
    /// </summary>
    public void Initialize()
    {
        _selfTestProblems.Clear();
        _selfTestProblems.AddRange(GameRegistry.SelfTest());

        MeshBudget.Reserve();
    }

    /// <inheritdoc />
    public void Update(double dt)
    {
        if (!_firstFrameDone)
        {
            // By the first real UI frame ModLibrary.Bind() has run and the enlarged shared buffers
            // are allocated, so the bump cursors can be rewound to the startup watermark.
            MeshBudget.OnFirstFrame();
            _firstFrameDone = true;
        }

        // Exactly once per frame, and only from here: the loader's Bind and Thumbnails states submit
        // command buffers and block on fences, which is only safe inside Program.OnDrawUiFrame.
        RuntimeModLoader.Step();

        // An unload confirmed in the UI is deferred to here for the same reason a purge is: it frees
        // ImGui textures, and by the time the panels draw, the vehicle editor's part browser has
        // already emitted ImageButton draw commands holding those descriptor sets.
        _modFolderPanel.ProcessPendingActions();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Never calls <c>ImGui.Begin</c>/<c>End</c> for the content area itself — the host window owns
    /// that. <see cref="StatusPanel" /> runs first because it publishes
    /// <see cref="StatusPanel.LoadingEnabled" />, which the two action panels take as their
    /// <c>canLoad</c> argument so the banner and the disabled buttons can never disagree.
    /// </remarks>
    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##pn_content");

        _statusPanel.Render(_selfTestProblems);

        ImGui.Spacing();
        _pastePanel.Render(_statusPanel.LoadingEnabled);

        ImGui.Spacing();
        _modFolderPanel.Render(_statusPanel.LoadingEnabled);

        ImGui.Spacing();
        _resultsPanel.Render();

        SubmodUI.EndContentArea();
    }

    /// <inheritdoc />
    public void RenderFloatingWindows()
    {
    }

    /// <inheritdoc />
    /// <remarks>
    /// A StarMap unload can land mid-job. <c>Mod.Unload</c> stops calling <see cref="Update" />, so
    /// the state machine would simply stop, abandoning the current job's <c>ThumbnailRenderer</c>,
    /// its private <c>VkCommandPool</c> and the readback buffer. Abandoning the job releases them.
    /// </remarks>
    public void Dispose()
    {
        RuntimeModLoader.AbandonForShutdown();
    }
}
