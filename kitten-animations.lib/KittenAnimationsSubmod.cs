using System;
using Brutal.ImGuiApi;
using KSA;
using MeowSci.KittenAnimationsLib.Ui;
using MeowSci.KsaAbstractions;

namespace MeowSci.KittenAnimationsLib;

/// <summary>
/// Animation panel for a selected EVA kitten: plays any clip the game loaded for it, triggers facial
/// expressions, and exposes the blend weights and locomotion tuning that decide how hard each
/// animation lands.
/// </summary>
public sealed partial class KittenAnimationsSubmod : ISubmod, MeowSci.KsaAbstractions.Persistence.ISaveParticipantSource
{
    public string Name => "Kitten Animations";
    public string Tooltip => "Play any kitten animation, trigger expressions, and tune animation strength.";

    private readonly Random _random = new();
    private readonly ImInputString _targetFilter = new(128);

    private AnimationUiContext? _context;
    private string? _selectedKittenId;

    public void Initialize()
    {
        KittenAnimationPatches.Drivers = _drivers;
        _originalTuning = SavedAnimationTuning.Capture();
    }

    public void Update(double dt)
    {
        try
        {
            UpdateTargets(dt);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"kitten-animations: Error in Update: {ex.Message}");
            _context = null;
        }
    }

    public void RenderContent()
    {
        SubmodUI.BeginContentArea("##ka_content");

        var kittens = KittenAvatarAccessor.GetAllKittens();
        var controlledKitten = KittenAvatarAccessor.GetControlledKitten();
        if (TargetSection.Render(kittens, controlledKitten, _selectedKittenId, _targetFilter,
                out var selectedKittenId))
        {
            SelectTarget(selectedKittenId);
        }

        ImGui.Spacing();
        var context = _context;
        if (context == null)
        {
            RenderNoTargetMessage(kittens, controlledKitten);
            SubmodUI.EndContentArea();
            return;
        }

        PlaybackSection.Render(context);
        ImGui.Spacing();
        AnimationLibrarySection.Render(context);
        ImGui.Spacing();
        ExpressionSection.Render(context);
        ImGui.Spacing();
        StrengthSection.Render(context);
        ImGui.Spacing();
        TuningSection.Render(context);

        SubmodUI.EndContentArea();
    }

    public void Dispose()
    {
        ResetAnimations();
        if (ReferenceEquals(KittenAnimationPatches.Drivers, _drivers))
            KittenAnimationPatches.Drivers = null;
    }

    private KittenEva? ResolveTargetKitten()
    {
        return _selectedKittenId == null
            ? KittenAvatarAccessor.GetControlledKitten()
            : KittenAvatarAccessor.FindKitten(_selectedKittenId);
    }

    private void SelectTarget(string? kittenId)
    {
        if (string.Equals(_selectedKittenId, kittenId, StringComparison.Ordinal)) return;

        _context = null;
        _selectedKittenId = kittenId;
        Update(0);
    }

    private void RenderNoTargetMessage(
        System.Collections.Generic.IReadOnlyCollection<KittenEva> kittens,
        KittenEva? controlledKitten)
    {
        if (_selectedKittenId != null)
        {
            bool available = false;
            foreach (var kitten in kittens)
            {
                if (!string.Equals(kitten.Id, _selectedKittenId, StringComparison.Ordinal)) continue;
                available = true;
                break;
            }

            if (available)
            {
                ImGui.TextDisabled($"Binding selected kitten '{_selectedKittenId}'...");
            }
            else
            {
                ImGui.TextDisabled($"Selected kitten '{_selectedKittenId}' is not currently on EVA.");
                ImGui.TextDisabled("Choose another target, or wait for this kitten to return to EVA.");
            }
        }
        else if (controlledKitten != null)
        {
            ImGui.TextDisabled($"Binding controlled kitten '{controlledKitten.Id}'...");
        }
        else if (kittens.Count > 0)
        {
            ImGui.TextDisabled("No EVA kitten is currently controlled.");
            ImGui.TextDisabled("Select a target kitten above, or take control of one.");
        }
        else
        {
            ImGui.TextDisabled("No EVA kittens are available in the current system.");
        }
    }
}
