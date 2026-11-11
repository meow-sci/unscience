using System;
using System.Runtime.CompilerServices;
using KSA;

namespace MeowSci.WingWalkerLib;

public static partial class WingWalkerPatches
{
    private sealed record StandingBody(AnimatedRenderable Model, AnimationAssetRef Clip);
    private static readonly ConditionalWeakTable<KittenRenderable, StandingBody> StandingBodies = new();

    private readonly record struct AppearanceState(
        CharacterAvatar? Avatar, AnimatedRenderable? Model, CatEarAnim? Ear, CatConditionAnim? Condition,
        StaticMeshRenderable? HelmetMesh, StaticMeshRenderable? VisorMesh,
        bool HelmetEnabled, bool HelmetVisible, bool VisorVisible, float EarWeight, bool PoseEars,
        bool Standing, AnimationAssetRef? SeatedClip, bool Frozen);

    // The native seat has created its renderable by this public call. Holding the override
    // across the entire render also covers selectors that the JIT has inlined into this body.
    private static void BeginExternalAppearance(KittenRenderable __instance,
        CharacterAvatar ____characterAvatar, CatEarAnim ____catEarAnim, CatConditionAnim ____catConditionAnim,
        ref AnimationAssetRef? ____seatedIdleAnim, AnimationAssetRef? ____groundIdleAnim,
        out AppearanceState __state)
    {
        __state = default;
        IVASeat? seat = _externalSeat;
        if (seat == null || !ReferenceEquals(seat.Renderable, __instance)) return;

        CharacterAvatar avatar = ____characterAvatar;
        AnimatedRenderable model = avatar.Core.CharacterModel;
        StaticMeshRenderable? helmet = avatar.Attachments.Helmet.HelmetMesh;
        StaticMeshRenderable? visor = avatar.Attachments.Helmet.VisorMesh;
        bool standing = ExternalCrewParts.IsStanding(seat) && ____groundIdleAnim != null;
        __state = new AppearanceState(avatar, model, ____catEarAnim, ____catConditionAnim, helmet, visor,
            avatar.Attachments.Helmet.Enabled, helmet?.Visible ?? false, visor?.Visible ?? false,
            ____catEarAnim.ExpressionWeight, ____catConditionAnim.PoseEars,
            standing, ____seatedIdleAnim, model.FreezeAnimation);

        // Keep helmet-compatible ears in every viewport: native body poses are cached per frame.
        // Mesh visibility hides the own-seat helmet in first person without changing that pose.
        avatar.Attachments.Helmet.Enabled = true;
        ____catEarAnim.ExpressionWeight = 1f;
        if (helmet != null) helmet.Visible = helmet.Visible && !__instance.HideHead;
        if (visor != null) visor.Visible = visor.Visible && !__instance.HideHead;
        // The native seat continues to own the avatar and its disabled MMU/backpack.

        if (standing)
        {
            AnimationAssetRef clip = ____groundIdleAnim!;
            ____seatedIdleAnim = null;
            model.FreezeAnimation = false;
            if (!StandingBodies.TryGetValue(__instance, out StandingBody? initialized)
                || !ReferenceEquals(initialized.Model, model) || !ReferenceEquals(initialized.Clip, clip))
            {
                // A timed crossfade from the constructor's seated pose never finishes at dt=0.
                // Restart once with zero blend; later views/frames retain the native idle clock.
                model.PlayAnimation(clip, 0f);
                StandingBodies.Remove(__instance);
                StandingBodies.Add(__instance, new StandingBody(model, clip));
            }
            else model.SetAnimation(clip, 0f);
        }
        else if (ExternalCrewParts.IsStanding(seat) && !_missingIdleLogged)
        {
            _missingIdleLogged = true;
            Console.WriteLine("wing-walker: character has no standing idle; retaining its native seated animation");
        }
    }

    private static void FinishExternalAppearance(ref AnimationAssetRef? ____seatedIdleAnim, AppearanceState __state)
    {
        if (__state.Avatar is not CharacterAvatar avatar) return;
        if (__state.Standing)
        {
            ____seatedIdleAnim = __state.SeatedClip;
            if (__state.Model != null) __state.Model.FreezeAnimation = __state.Frozen;
        }
        avatar.Attachments.Helmet.Enabled = __state.HelmetEnabled;
        if (__state.HelmetMesh != null) __state.HelmetMesh.Visible = __state.HelmetVisible;
        if (__state.VisorMesh != null) __state.VisorMesh.Visible = __state.VisorVisible;
        if (__state.Ear != null) __state.Ear.ExpressionWeight = __state.EarWeight;
        if (__state.Condition != null) __state.Condition.PoseEars = __state.PoseEars;
    }
}
