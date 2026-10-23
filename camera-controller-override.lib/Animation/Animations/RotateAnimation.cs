using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.CameraControllerOverrideLib.Animation.Animations;

/// <summary>
/// Rotates the camera's look-direction by specified yaw and pitch angles while
/// keeping the camera at its current position. This creates the appearance of the
/// camera "looking around" from a fixed point.
///
/// Yaw positive = look right, negative = look left.
/// Pitch positive = look up, negative = look down.
///
/// Uses absolute rotation from the starting orientation each frame to avoid
/// floating-point drift.
/// </summary>
public class RotateAnimation : IKeyframeAnimation
{
    // Configuration
    public double YawDegrees { get; }
    public double PitchDegrees { get; }
    public double DurationSeconds { get; }
    public EasingType Easing { get; }
    public double EasingPowerStart { get; }
    public double EasingPowerEnd { get; }

    // Interface properties
    public string Name => "Rotate";
    public string Description => "Rotate camera look-direction (yaw/pitch) from fixed position";
    public Func<Controller, double3>? LookAtTargetProvider { get; set; }

    // Runtime state
    private doubleQuat _startRotation;
    private double3 _startOffset;  // camera pos relative to target at init
    private double3 _upAxis;
    private double3 _rightAxis;
    private bool _isInitialized;

    /// <summary>
    /// Creates a new rotation animation that rotates the camera look-direction
    /// by the specified yaw and pitch angles over the given duration.
    /// </summary>
    /// <param name="yawDegrees">Horizontal rotation in degrees (positive = right, negative = left).</param>
    /// <param name="pitchDegrees">Vertical rotation in degrees (positive = up, negative = down).</param>
    /// <param name="durationSeconds">Total animation duration in seconds.</param>
    /// <param name="easing">Easing function type.</param>
    /// <param name="easingPowerStart">Power for acceleration phase.</param>
    /// <param name="easingPowerEnd">Power for deceleration phase.</param>
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

    public void Initialize(Controller controller, Transform3D transform)
    {
        _startRotation = transform.LocalRotation;
        _startOffset = AnimationHelpers.GetTargetOffset(controller, transform);
        _upAxis = double3.UnitY.Transform(_startRotation);
        _rightAxis = double3.UnitX.Transform(_startRotation);
        _isInitialized = true;

        Console.WriteLine($"[RotateAnimation] Initialize: yaw={YawDegrees:F1}° pitch={PitchDegrees:F1}° duration={DurationSeconds:F1}s easing={Easing}");
    }

    public bool Update(Controller controller, Transform3D transform, double deltaTime, double elapsedTime)
    {
        if (!_isInitialized)
            return true;

        double t = Math.Min(1.0, elapsedTime / DurationSeconds);
        double easedT = AnimationHelpers.ApplyEasing(t, Easing, EasingPowerStart, EasingPowerEnd);
        bool isComplete = elapsedTime >= DurationSeconds;
        if (isComplete)
            easedT = 1.0;

        double currentYawRad = YawDegrees * easedT * Math.PI / 180.0;
        double currentPitchRad = PitchDegrees * easedT * Math.PI / 180.0;

        var yawQuat = doubleQuat.CreateFromAxisAngle(_upAxis, currentYawRad);
        var pitchQuat = doubleQuat.CreateFromAxisAngle(_rightAxis, currentPitchRad);
        var totalRotation = yawQuat * pitchQuat;

        transform.LocalRotation = totalRotation * _startRotation;
        AnimationHelpers.SetTargetOffset(controller, transform, _startOffset);

        if (elapsedTime < deltaTime * 1.5)
            Console.WriteLine($"[RotateAnimation] First frame: t={t:F4} easedT={easedT:F4} yawRad={currentYawRad:F4} pitchRad={currentPitchRad:F4}");
        if (isComplete)
            Console.WriteLine($"[RotateAnimation] Complete: yaw={YawDegrees:F1}° pitch={PitchDegrees:F1}°");

        return isComplete;
    }

    public void Reset()
    {
        _startRotation = doubleQuat.Identity;
        _startOffset = double3.Zero;
        _upAxis = double3.Zero;
        _rightAxis = double3.Zero;
        _isInitialized = false;
    }

    public Dictionary<string, string> GetDisplayProperties()
    {
        return new Dictionary<string, string>
        {
            { "Yaw", $"{YawDegrees:F1}°" },
            { "Pitch", $"{PitchDegrees:F1}°" },
            { "Duration", $"{DurationSeconds:F1}s" },
            { "Easing", Easing.ToString() },
            { "Easing Power (Start)", $"{EasingPowerStart:F1}" },
            { "Easing Power (End)", $"{EasingPowerEnd:F1}" }
        };
    }
}
