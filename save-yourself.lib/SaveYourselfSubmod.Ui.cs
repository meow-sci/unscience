using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace MeowSci.SaveYourselfLib;

public sealed partial class SaveYourselfSubmod
{
    private readonly ImInputString _prefixInput = new(AutoSaveSettings.MaxPrefixLength + 1);
    private string _syncedPrefix = string.Empty;

    private void RenderAutoSavePanel()
    {
        ImGui.SeparatorText("Auto Save"u8);
        ImGui.TextWrapped("Writes a new KSA save every N seconds through the game's own save path, exactly like the GAME SAVES window. Names are <prefix>_YYYYMMDDTHHMMSS in local time."u8);
        ImGui.TextDisabled("Settings are saved with the Unscience scene. Saving waits while no world is loaded or the vehicle editor is open."u8);
        ImGui.Spacing();

        var settings = Controller.Settings;
        SyncPrefixInput(settings.Prefix);

        bool enabled = settings.Enabled;
        if (ImGui.Checkbox("Enable auto save##save_yourself_enabled"u8, ref enabled))
            Controller.Configure(settings with { Enabled = enabled });

        RenderSettingsTable(settings);
        RenderActions();
        RenderStatus();
    }

    private void RenderSettingsTable(AutoSaveSettings settings)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new float2(6f, 6f));
        if (ImGui.BeginTable("##save_yourself_settings"u8, 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoPadOuterX))
        {
            ImGui.TableSetupColumn("##label"u8, ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##widget"u8, ImGuiTableColumnFlags.WidthStretch, 3f);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Prefix"u8);
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputTextWithHint("##save_yourself_prefix"u8, "autosave_"u8, _prefixInput))
            {
                string sanitized = AutoSaveSettings.SanitizePrefix(_prefixInput.ToString());
                _prefixInput.SetValue(sanitized.AsSpan());
                _syncedPrefix = sanitized;
                Controller.Configure(settings with { Prefix = sanitized });
            }
            ImGui.SetItemTooltip("Letters, digits, underscore and hyphen only. Leave empty for 'autosave_'."u8);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Interval (s)"u8);
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1f);
            int interval = settings.IntervalSeconds;
            if (ImGui.DragInt("##save_yourself_interval"u8, ref interval, 1f, AutoSaveSettings.MinIntervalSeconds, AutoSaveSettings.MaxIntervalSeconds))
                Controller.Configure(settings with { IntervalSeconds = interval });
            ImGui.SetItemTooltip("Real seconds between auto saves (5 to 300). Drag, or Ctrl+click to type."u8);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.Text("Next name"u8);
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled(Controller.PreviewName());

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();
    }

    private void RenderActions()
    {
        bool canSave = MeowSci.KsaAbstractions.GameSaveProvider.CanSaveNow;
        ImGui.BeginDisabled(!canSave);
        if (ImGui.Button(" Save Now "u8))
            Controller.SaveNow();
        ImGui.EndDisabled();
        ImGui.SetItemTooltip("Write one save immediately with the same naming. The countdown is unaffected."u8);
    }

    private void RenderStatus()
    {
        ImGui.Spacing();
        var settings = Controller.Settings;
        if (!settings.Enabled)
            ImGui.TextDisabled("Auto save is off."u8);
        else if (Controller.IsWaitingForGame)
            ImGui.TextColored(new float4(1f, 0.7f, 0.2f, 1f), "Auto save due; waiting for a loaded world without the vehicle editor."u8);
        else
            ImGui.Text($"Next auto save in {Math.Ceiling(Controller.SecondsUntilNextSave):0} s.");

        if (Controller.LastSaveName != null)
            ImGui.TextColored(new float4(0.4f, 1f, 0.4f, 1f), $"Last save: {Controller.LastSaveName} ({Controller.SaveCount} this scene)");
        if (!string.IsNullOrEmpty(Controller.LastError))
            ImGui.TextColored(new float4(1f, 0.3f, 0.3f, 1f), Controller.LastError);
    }

    /// <summary>Mirrors restored/reset settings into the text box without fighting live typing.</summary>
    private void SyncPrefixInput(string prefix)
    {
        if (string.Equals(prefix, _syncedPrefix, StringComparison.Ordinal)) return;
        _prefixInput.SetValue(prefix.AsSpan());
        _syncedPrefix = prefix;
    }
}
