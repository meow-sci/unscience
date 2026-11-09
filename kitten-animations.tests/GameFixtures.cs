using System;
using System.Collections.Generic;

namespace KSA
{
    public interface IAnimation { }
    public sealed class AnimationAssetRef(string id) : IAnimation { public string Id = id; }
    public sealed class BoneAnimRuntime
    {
        internal float TimeSinceTransition;
        internal IAnimation? CurrentAnimation;
        public void SampleCurrentAnimation() { }
    }
    public sealed class AnimatedRenderable
    {
        private readonly BoneAnimRuntime RuntimeAnim = new();
        public BoneAnimRuntime Clock => RuntimeAnim;
        public bool FreezeAnimation;
        public List<object> AnimProcessors = new();
        public void PlayAnimation(IAnimation animation, float blend)
        {
            RuntimeAnim.CurrentAnimation = animation;
            RuntimeAnim.TimeSinceTransition = 0;
        }
        public void SetAnimation(IAnimation animation, float blend)
        {
            if (!ReferenceEquals(RuntimeAnim.CurrentAnimation, animation)) PlayAnimation(animation, blend);
        }
        public void UpdateAnimation(double dt) { }
    }
    public sealed class CatExpressionAnim
    {
        public CharacterAvatar? CharacterAvatar;
        public AnimationAssetRef? ExpressionAnim;
        private object? _expressionPose = null;
        public object? Pose => _expressionPose;
        public float ExpressionWeight;
        public float Priority;
    }
    public sealed class CatEyeAnim { public float MaxLookAtAngle = 30; public float LookPitchOffsetDeg; }
    public sealed class CatEarAnim { public float ExpressionWeight = 0.8f; }
    public sealed class CharacterAvatar
    {
        public sealed class CharacterCore { public AnimatedRenderable CharacterModel = new(); }
        public sealed class ExpressionClips
        {
            public List<AnimationAssetRef>? Angry = new() { new("angry") };
            public List<AnimationAssetRef>? Awe = new() { new("awe") };
            public List<AnimationAssetRef>? Happy = new() { new("happy") };
            public List<AnimationAssetRef>? Sad = new() { new("sad") };
            public List<AnimationAssetRef>? Scared = new() { new("scared") };
        }
        public CharacterCore Core = new();
        public ExpressionClips Expressions = new();
    }
    public class Vehicle(string id)
    {
        public string Id = id;
        public bool IsDisposed;
        public bool IsDebris;
    }
    public sealed class KittenEva(string id) : Vehicle(id) { public KittenRenderable Renderable = new(); }
    public sealed class KittenRenderable
    {
        public CharacterAvatar _characterAvatar = new();
        public CatExpressionAnim _catPersonalityExpressionAnim = new() { ExpressionWeight = 0.6f };
        public CatExpressionAnim _catExpressionAnim = new();
        public CatEyeAnim _catEyeAnim = new();
        public CatEarAnim _catEarAnim = new();
        public AnimationAssetRef Clip = new("walk");
    }
    public sealed class ObjectCollection : List<object> { public List<object> UnsafeAsList() => this; }
    public sealed class CelestialSystem { public ObjectCollection All = new(); }
    public static class Universe { public static CelestialSystem? CurrentSystem = new(); }
    public static class Program { public static Vehicle? ControlledVehicle; }
}

namespace KSA
{
    public struct KittenLocomotionTuning
    {
        public static KittenLocomotionTuning Current;
        public float AnimBlendTime;
        public float IdleSpeedThreshold;
        public float PlaybackRateMin;
        public float PlaybackRateMax;
        public float WalkClipNominalSpeed;
        public float RunClipNominalSpeed;
        public float LadderNominalSpeed;
        public float TumbleNominalSpeed;
        public float MoonwalkWalkNominalSpeed;
        public float MoonwalkRunNominalSpeed;
        public float MoonwalkStartGravity;
        public float MoonwalkFullGravity;
        public float MoonwalkPlaybackScale;
        public float NominalSwimAnimSpeed;
        public float SwimBlendFullSpeed;
        public float SwimBlendHalfLife;
        public float SwimEyePitchFactor;
        public float JumpLandDuration;
        public float JumpLandBounceIgnoreTime;
        public float LadderEyePitchDeg;
    }
}
