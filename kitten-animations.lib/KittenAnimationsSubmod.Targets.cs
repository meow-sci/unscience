using System;
using System.Collections.Generic;
using KSA;
using MeowSci.KittenAnimationsLib.Ui;
using MeowSci.KsaAbstractions.Persistence;

namespace MeowSci.KittenAnimationsLib;

public sealed partial class KittenAnimationsSubmod
{
    private sealed class Target
    {
        public AnimationUiContext? Context;
        public SavedKittenAnimation? Pending;
    }

    private readonly Dictionary<string, Target> _targets = new(StringComparer.Ordinal);
    private readonly Dictionary<AnimatedRenderable, KittenAnimationDriver> _drivers = new(ReferenceEqualityComparer.Instance);

    private void UpdateTargets(double dt)
    {
        var selected = ResolveTargetKitten();
        if (selected != null && !selected.IsDisposed && !_targets.ContainsKey(selected.Id))
            _targets.Add(selected.Id, new Target());

        _context = null;
        foreach (var pair in _targets)
        {
            try
            {
                var target = pair.Value;
                // Resolve exact, unambiguous identity even for targets outside the active camera.
                var kitten = KittenAvatarAccessor.FindKitten(pair.Key);
                var avatar = KittenAvatarAccessor.GetAvatar(kitten?.Renderable);
                if (target.Context != null &&
                    (!ReferenceEquals(kitten, target.Context.Kitten) || !ReferenceEquals(avatar, target.Context.Avatar)))
                {
                    target.Pending = CaptureTarget(target.Context);
                    ReleaseTarget(target);
                }

                if (target.Context == null && kitten != null && avatar != null)
                    BindTarget(target, kitten, avatar);

                target.Context?.Expressions.Update(dt);
                if (ReferenceEquals(kitten, selected)) _context = target.Context;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"kitten-animations: Error updating '{pair.Key}': {ex.Message}");
            }
        }
    }

    private void BindTarget(Target target, KittenEva kitten, CharacterAvatar avatar)
    {
        var catalog = KittenAnimationCatalog.Build(avatar, kitten.Renderable);
        var processors = KittenAnimProcessors.Read(kitten.Renderable);
        var driver = new KittenAnimationDriver();
        var expressions = new KittenExpressionController();
        driver.BindTarget(avatar.Core.CharacterModel, processors);
        expressions.Attach(avatar);
        var ui = new AnimationUiContext
        {
            Kitten = kitten, Avatar = avatar, Catalog = catalog, Processors = processors,
            Driver = driver, Expressions = expressions, Random = _random,
        };
        target.Context = ui;
        _drivers.Add(avatar.Core.CharacterModel, driver);
        if (target.Pending != null)
        {
            RestoreTarget(target.Pending, ui, new SaveRestoreContext(
                warning => Console.WriteLine($"kitten-animations: {warning}")));
            target.Pending = null;
        }
        if (catalog.UnresolvedFields.Count > 0)
            Console.WriteLine($"kitten-animations: unresolved game fields: {string.Join(", ", catalog.UnresolvedFields)}");
    }

    private void ReleaseTarget(Target target)
    {
        var ui = target.Context;
        if (ui == null) return;
        _drivers.Remove(ui.Avatar.Core.CharacterModel);
        ui.Expressions.Detach();
        ui.Driver.Reset();
        target.Context = null;
    }
}
