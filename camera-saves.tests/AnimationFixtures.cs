using System;
using MeowSci.KsaAbstractions;
namespace MeowSci.KsaAbstractions { public enum EasingType { Linear, EaseIn, EaseOut, EaseInOut } }
namespace MeowSci.CameraControllerOverrideLib.Animation
{
    // Constructor/property fixtures; the recipe mapper and JSON serializer are linked production code.
    public interface IKeyframeAnimation
    {
        double DurationSeconds { get; }
        EasingType Easing { get; }
        double EasingPowerStart { get; }
        double EasingPowerEnd { get; }
        Func<object, object>? LookAtTargetProvider { get; set; }
    }
    public sealed class AnimationGroup : IKeyframeAnimation
    {
        private readonly System.Collections.Generic.List<IKeyframeAnimation> _children = new();
        public int Count => _children.Count;
        public IKeyframeAnimation GetAnimation(int i) => _children[i];
        public void Add(IKeyframeAnimation value) => _children.Add(value);
        public double DurationSeconds => 10;
        public EasingType Easing => EasingType.Linear;
        public double EasingPowerStart => 1;
        public double EasingPowerEnd => 1;
        public Func<object, object>? LookAtTargetProvider { get; set; }
    }
}
namespace MeowSci.CameraControllerOverrideLib.Animation.Animations
{
    public sealed class SpiralZoomInAnimation : IKeyframeAnimation
    {
        public double SpeedMetersPerSecond { get; }
        public double DurationSeconds { get; }
        public EasingType Easing { get; }
        public double EasingPowerStart { get; }
        public double EasingPowerEnd { get; }
        public double SpiralDegrees { get; }
        public Func<object, object>? LookAtTargetProvider { get; set; }
        public SpiralZoomInAnimation(double speedMetersPerSecond, double durationSeconds, EasingType easing, double spiralDegrees, double easingPowerStart = 3.0, double easingPowerEnd = 3.0)
        {
        SpeedMetersPerSecond = speedMetersPerSecond;
        DurationSeconds = durationSeconds;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
        SpiralDegrees = spiralDegrees;
        }
    }
    public sealed class OrbitAnimation : IKeyframeAnimation
    {
        public double Degrees { get; }
        public double DurationSeconds { get; }
        public EasingType Easing { get; }
        public double EasingPowerStart { get; }
        public double EasingPowerEnd { get; }
        public Func<object, object>? LookAtTargetProvider { get; set; }
        public OrbitAnimation(double degrees, double durationSeconds, EasingType easing, double easingPowerStart = 3.0, double easingPowerEnd = 3.0)
        {
        Degrees = degrees;
        DurationSeconds = durationSeconds;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
        }
    }
    public sealed class ZoomOutAnimation : IKeyframeAnimation
    {
        public double SpeedMetersPerSecond { get; }
        public double DurationSeconds { get; }
        public EasingType Easing { get; }
        public double EasingPowerStart { get; }
        public double EasingPowerEnd { get; }
        public Func<object, object>? LookAtTargetProvider { get; set; }
        public ZoomOutAnimation(double speedMetersPerSecond, double durationSeconds, EasingType easing, double easingPowerStart = 3.0, double easingPowerEnd = 3.0)
        {
        SpeedMetersPerSecond = speedMetersPerSecond;
        DurationSeconds = durationSeconds;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
        }
    }
    public sealed class LoopyOrbitAnimation : IKeyframeAnimation
    {
        public double Degrees { get; }
        public double DurationSeconds { get; }
        public EasingType Easing { get; }
        public double EasingPowerStart { get; }
        public double EasingPowerEnd { get; }
        public double LoopIntervalDegrees { get; }
        public double AmplitudeMeters { get; }
        public Func<object, object>? LookAtTargetProvider { get; set; }
        public LoopyOrbitAnimation(
        double degrees,
        double loopIntervalDegrees,
        double amplitudeMeters,
        double durationSeconds,
        EasingType easing,
        double easingPowerStart = 3.0,
        double easingPowerEnd = 3.0)
        {
        Degrees = degrees;
        LoopIntervalDegrees = loopIntervalDegrees;
        AmplitudeMeters = amplitudeMeters;
        DurationSeconds = durationSeconds;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
        }
    }
    public sealed class PanAnimation : IKeyframeAnimation
    {
        public double OffsetX { get; }
        public double OffsetY { get; }
        public double OffsetZ { get; }
        public double DurationSeconds { get; }
        public EasingType Easing { get; }
        public double EasingPowerStart { get; }
        public double EasingPowerEnd { get; }
        public Func<object, object>? LookAtTargetProvider { get; set; }
        public PanAnimation(
        double offsetX,
        double offsetY,
        double offsetZ,
        double durationSeconds,
        EasingType easing,
        double easingPowerStart = 3.0,
        double easingPowerEnd = 3.0)
        {
        OffsetX = offsetX;
        OffsetY = offsetY;
        OffsetZ = offsetZ;
        DurationSeconds = durationSeconds;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
        }
    }
    public sealed class RotateAnimation : IKeyframeAnimation
    {
        public double YawDegrees { get; }
        public double PitchDegrees { get; }
        public double DurationSeconds { get; }
        public EasingType Easing { get; }
        public double EasingPowerStart { get; }
        public double EasingPowerEnd { get; }
        public Func<object, object>? LookAtTargetProvider { get; set; }
        public RotateAnimation(
        double yawDegrees,
        double pitchDegrees,
        double durationSeconds,
        EasingType easing,
        double easingPowerStart = 3.0,
        double easingPowerEnd = 3.0)
        {
        YawDegrees = yawDegrees;
        PitchDegrees = pitchDegrees;
        DurationSeconds = durationSeconds;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
        }
    }
    public sealed class ZoomInToOffsetAnimation : IKeyframeAnimation
    {
        public double SpeedMetersPerSecond { get; }
        public double DurationSeconds { get; }
        public EasingType Easing { get; }
        public double EasingPowerStart { get; }
        public double EasingPowerEnd { get; }
        public double OffsetX { get; }
        public double OffsetY { get; }
        public double OffsetZ { get; }
        public Func<object, object>? LookAtTargetProvider { get; set; }
        public ZoomInToOffsetAnimation(double speedMetersPerSecond, double durationSeconds, EasingType easing,
        double offsetX, double offsetY, double offsetZ, double easingPowerStart = 3.0, double easingPowerEnd = 3.0)
        {
        SpeedMetersPerSecond = speedMetersPerSecond;
        DurationSeconds = durationSeconds;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
        OffsetX = offsetX;
        OffsetY = offsetY;
        OffsetZ = offsetZ;
        }
    }
    public sealed class ShakeAnimation : IKeyframeAnimation
    {
        public double DurationSeconds { get; }
        public int ShakeCount { get; }
        public double AmplitudeDegrees { get; }
        public double ShakeSpeed { get; }
        public EasingType Easing { get; }
        public double EasingPowerStart { get; }
        public double EasingPowerEnd { get; }
        public Func<object, object>? LookAtTargetProvider { get; set; }
        public ShakeAnimation(
        double durationSeconds,
        int shakeCount,
        double amplitudeDegrees,
        double shakeSpeed,
        EasingType easing,
        double easingPowerStart = 3.0,
        double easingPowerEnd = 3.0)
        {
        DurationSeconds = durationSeconds;
        ShakeCount = shakeCount;
        AmplitudeDegrees = amplitudeDegrees;
        ShakeSpeed = shakeSpeed;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
        }
    }
    public sealed class ZoomInAnimation : IKeyframeAnimation
    {
        public double SpeedMetersPerSecond { get; }
        public double DurationSeconds { get; }
        public EasingType Easing { get; }
        public double EasingPowerStart { get; }
        public double EasingPowerEnd { get; }
        public Func<object, object>? LookAtTargetProvider { get; set; }
        public ZoomInAnimation(double speedMetersPerSecond, double durationSeconds, EasingType easing, double easingPowerStart = 3.0, double easingPowerEnd = 3.0)
        {
        SpeedMetersPerSecond = speedMetersPerSecond;
        DurationSeconds = durationSeconds;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
        }
    }
    public sealed class SpiralZoomOutAnimation : IKeyframeAnimation
    {
        public double SpeedMetersPerSecond { get; }
        public double DurationSeconds { get; }
        public EasingType Easing { get; }
        public double EasingPowerStart { get; }
        public double EasingPowerEnd { get; }
        public double SpiralDegrees { get; }
        public Func<object, object>? LookAtTargetProvider { get; set; }
        public SpiralZoomOutAnimation(double speedMetersPerSecond, double durationSeconds, EasingType easing, double spiralDegrees, double easingPowerStart = 3.0, double easingPowerEnd = 3.0)
        {
        SpeedMetersPerSecond = speedMetersPerSecond;
        DurationSeconds = durationSeconds;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
        SpiralDegrees = spiralDegrees;
        }
    }
}
