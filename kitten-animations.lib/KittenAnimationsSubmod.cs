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

    private readonly KittenAnimationDriver _driver = new();
    private readonly KittenExpressionController _expressions = new();
    private readonly Random _random = new();
    private readonly ImInputString _targetFilter = new(128);

    private AnimationUiContext? _context;
    private KittenEva? _boundKitten;
    private CharacterAvatar? _boundAvatar;
    private string? _selectedKittenId;

    public void Initialize()
    {
        KittenAnimationPatches.Driver = _driver;
        _originalTuning = SavedAnimationTuning.Capture();
    }

    public void Update(double dt)
    {
        try
        {
            var kitten = ResolveTargetKitten();
            var renderable = kitten?.Renderable;
            var avatar = KittenAvatarAccessor.GetAvatar(renderable);

            if (kitten == null || renderable == null || avatar == null)
            {
                Unbind();
                return;
            }

            if (!ReferenceEquals(kitten, _boundKitten) || !ReferenceEquals(avatar, _boundAvatar))
                Bind(kitten, renderable, avatar);

            _expressions.Update(dt);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"kitten-animations: Error in Update: {ex.Message}");
            Unbind();
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
        Unbind();
        _driver.Reset();
        KittenAnimationPatches.Driver = null;
    }

    private void Bind(KittenEva kitten, KittenRenderable renderable, CharacterAvatar avatar)
    {
        Unbind();

        var catalog = KittenAnimationCatalog.Build(avatar, renderable);
        var processors = KittenAnimProcessors.Read(renderable);

        _expressions.Attach(avatar);

        _driver.ClearClip();
        _driver.BindTarget(avatar.Core.CharacterModel, processors);

        _context = new AnimationUiContext
        {
            Kitten = kitten,
            Avatar = avatar,
            Catalog = catalog,
            Driver = _driver,
            Expressions = _expressions,
            Processors = processors,
            Random = _random,
        };

        _boundKitten = kitten;
        _boundAvatar = avatar;

        if (catalog.UnresolvedFields.Count > 0)
            Console.WriteLine($"kitten-animations: unresolved game fields: {string.Join(", ", catalog.UnresolvedFields)}");
    }

    private void Unbind()
    {
        _expressions.Detach();
        _driver.ClearClip();
        _driver.UnbindTarget();
        _context = null;
        _boundKitten = null;
        _boundAvatar = null;
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

        Unbind();
        _selectedKittenId = kittenId;
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
