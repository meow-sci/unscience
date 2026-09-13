using System;
using System.Collections.Generic;
using System.Reflection;
using KSA;

namespace MeowSci.KittenAnimationsLib;

/// <summary>
/// Plays facial expressions through a CatExpressionAnim processor the mod owns and appends to the
/// kitten model itself.
///
/// The game's own expression processor (KittenAnimProcessors.Reactive) has its ExpressionWeight
/// rewritten every frame by KittenRenderable.UpdateRenderData from vehicle acceleration, so writing
/// to it never holds. Appending our own processor puts the mod last in the AnimProcessors list — it
/// mixes over the game's poses and nothing overwrites it.
/// </summary>
public sealed class KittenExpressionController
{
    public enum ExpressionType { None, Angry, Awe, Happy, Sad, Scared }

    public static readonly ExpressionType[] AllExpressions =
    {
        ExpressionType.Angry, ExpressionType.Awe, ExpressionType.Happy,
        ExpressionType.Sad, ExpressionType.Scared,
    };

    private static readonly FieldInfo? ExpressionPoseField = typeof(CatExpressionAnim).GetField(
        "_expressionPose",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private CatExpressionAnim? _processor;
    private AnimatedRenderable? _model;
    private float _elapsed;

    /// <summary>Seconds spent ramping weight up from 0.</summary>
    public float EaseInDuration { get; set; } = 0.25f;

    /// <summary>Seconds held at <see cref="PeakWeight"/> before easing out.</summary>
    public float HoldDuration { get; set; } = 1.5f;

    /// <summary>Seconds spent ramping weight back to 0.</summary>
    public float EaseOutDuration { get; set; } = 0.25f;

    /// <summary>How strongly the expression pose is mixed in. 1 = the full authored pose.</summary>
    public float PeakWeight { get; set; } = 1f;

    /// <summary>When true the expression holds at peak weight until it is cleared.</summary>
    public bool Latch { get; set; }

    public ExpressionType Current { get; private set; } = ExpressionType.None;

    public string CurrentVariant { get; private set; } = string.Empty;
    public int CurrentVariantIndex { get; private set; } = -1;

    public float CurrentWeight => _processor?.ExpressionWeight ?? 0f;

    public bool IsAttached => _processor != null;

    /// <summary>Seconds remaining before the expression ends, or -1 while latched.</summary>
    public float Remaining => Latch ? -1f : Math.Max(0f, TotalDuration - _elapsed);

    private float TotalDuration => EaseInDuration + HoldDuration + EaseOutDuration;

    /// <summary>Installs the mod's expression processor on the avatar, replacing any earlier one.</summary>
    public void Attach(CharacterAvatar avatar)
    {
        var model = avatar.Core.CharacterModel;
        if (ReferenceEquals(model, _model) && _processor != null) return;

        Detach();

        _processor = new CatExpressionAnim
        {
            CharacterAvatar = avatar,
            ExpressionAnim = null,
            ExpressionWeight = 0f,
            Priority = 1f,
        };

        model.AnimProcessors.Add(_processor);
        _model = model;
    }

    /// <summary>Removes the mod's processor from the model it was installed on.</summary>
    public void Detach()
    {
        if (_model != null && _processor != null)
            _model.AnimProcessors.Remove(_processor);

        _processor = null;
        _model = null;
        Current = ExpressionType.None;
        CurrentVariant = string.Empty;
        _elapsed = 0f;
    }

    /// <summary>Starts an expression. <paramref name="variantIndex"/> below zero picks a random variant.</summary>
    public void Trigger(CharacterAvatar avatar, ExpressionType type, int variantIndex, Random random)
    {
        Attach(avatar);
        if (_processor == null) return;

        var variants = GetVariants(avatar, type);
        if (variants == null || variants.Count == 0)
        {
            Console.WriteLine($"kitten-animations: No {type} expression clips on this character");
            return;
        }

        int index = variantIndex < 0 ? random.Next(variants.Count) : Math.Clamp(variantIndex, 0, variants.Count - 1);
        var animation = variants[index];
        if (animation == null) return;

        _processor.ExpressionAnim = animation;
        ClearPoseCache(_processor);
        _processor.ExpressionWeight = 0f;

        Current = type;
        CurrentVariantIndex = index;
        CurrentVariant = $"{type} {index + 1}/{variants.Count} ({animation.Id})";
        _elapsed = 0f;
    }

    /// <summary>Ends the current expression immediately.</summary>
    public void Clear()
    {
        Current = ExpressionType.None;
        CurrentVariantIndex = -1;
        CurrentVariant = string.Empty;
        _elapsed = 0f;

        if (_processor != null)
            _processor.ExpressionWeight = 0f;
    }

    /// <summary>Advances the expression envelope. Call once per frame.</summary>
    public void Update(double dt)
    {
        if (_processor == null || Current == ExpressionType.None) return;

        _elapsed += (float)dt;

        if (!Latch && _elapsed >= TotalDuration)
        {
            Clear();
            return;
        }

        _processor.ExpressionWeight = ComputeWeight();
    }

    private float ComputeWeight()
    {
        if (EaseInDuration > 0f && _elapsed < EaseInDuration)
        {
            float progress = _elapsed / EaseInDuration;
            return PeakWeight * progress * progress; // quadratic ease-in
        }

        if (Latch) return PeakWeight;

        float easeOutStart = EaseInDuration + HoldDuration;
        if (_elapsed <= easeOutStart || EaseOutDuration <= 0f)
            return PeakWeight;

        float fade = (_elapsed - easeOutStart) / EaseOutDuration;
        return PeakWeight * Math.Max(0f, 1f - fade);
    }

    /// <summary>Returns the authored clips for an expression, or null if the character has none.</summary>
    public static List<AnimationAssetRef>? GetVariants(CharacterAvatar avatar, ExpressionType type) => type switch
    {
        ExpressionType.Angry => avatar.Expressions.Angry,
        ExpressionType.Awe => avatar.Expressions.Awe,
        ExpressionType.Happy => avatar.Expressions.Happy,
        ExpressionType.Sad => avatar.Expressions.Sad,
        ExpressionType.Scared => avatar.Expressions.Scared,
        _ => null,
    };

    /// <summary>
    /// CatExpressionAnim samples its pose once and caches it in a private field. Swapping ExpressionAnim
    /// without busting that cache replays whichever expression was sampled first.
    /// </summary>
    private static void ClearPoseCache(CatExpressionAnim processor)
    {
        if (ExpressionPoseField == null)
        {
            Console.WriteLine("kitten-animations: CatExpressionAnim._expressionPose field not found");
            return;
        }

        ExpressionPoseField.SetValue(processor, null);
    }
}
