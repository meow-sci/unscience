using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.CameraControllerOverrideLib.Animation.Animations;

/// <summary>
/// Moves the camera from its current position by a specified (X, Y, Z) offset
/// in camera-local coordinates, with easing. The camera continues to look at the
/// target throughout the pan.
///
/// X = camera right, Y = camera up, Z = toward target (positive = zoom in)
/// All offsets are relative to the camera's orientation at the start of the animation.
/// Position tracks the target so the spacecraft stays in view as it moves.
/// </summary>
public class PanAnimation : IKeyframeAnimation
{
    // Configuration properties
    public double OffsetX { get; }
    public double OffsetY { get; }
    public double OffsetZ { get; }
    public double DurationSeconds { get; }
    public EasingType Easing { get; }
    public double EasingPowerStart { get; }
    public double EasingPowerEnd { get; }

    // Runtime state
    private double3 _startOffset;    // camera pos relative to target at init
    private double3 _cameraRight;    // camera's local right axis at init
    private double3 _cameraUp;       // camera's local up axis at init
    private double3 _cameraForward;  // toward target at init

    // Interface properties
    public string Name => "Pan";
    public string Description => "Linear movement by offset from starting position";
    public Func<Controller, double3>? LookAtTargetProvider { get; set; }

    /// <summary>
    /// Creates a pan animation that moves the camera by the specified offset.
    /// </summary>
    /// <param name="offsetX">X displacement in meters (ecliptic coordinates).</param>
    /// <param name="offsetY">Y displacement in meters (ecliptic coordinates).</param>
    /// <param name="offsetZ">Z displacement in meters (ecliptic coordinates).</param>
    /// <param name="durationSeconds">Total animation duration in seconds.</param>
    /// <param name="easing">Easing function type.</param>
    /// <param name="easingPowerStart">Power for the acceleration phase.</param>
    /// <param name="easingPowerEnd">Power for the deceleration phase.</param>
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

    public void Initialize(Controller controller, Transform3D transform)
    {
        _startOffset = AnimationHelpers.GetTargetOffset(controller, transform);
        _cameraRight = double3.UnitX.Transform(transform.LocalRotation);
        _cameraUp = double3.UnitY.Transform(transform.LocalRotation);
        _cameraForward = double3.Normalize(-_startOffset);
        Console.WriteLine($"[PanAnimation] Initialize: startOffset={_startOffset}, offset=({OffsetX:F1}, {OffsetY:F1}, {OffsetZ:F1}), duration={DurationSeconds:F1}s, easing={Easing}");
    }

    public bool Update(Controller controller, Transform3D transform, double deltaTime, double elapsedTime)
    {
        double t = Math.Min(1.0, elapsedTime / DurationSeconds);
        double easedT = AnimationHelpers.ApplyEasing(t, Easing, EasingPowerStart, EasingPowerEnd);

        bool isComplete = elapsedTime >= DurationSeconds;
        if (isComplete)
        {
            easedT = 1.0;
        }

        var worldOffset = _cameraRight   * (OffsetX * easedT)
                        + _cameraUp      * (OffsetY * easedT)
                        + _cameraForward * (OffsetZ * easedT);

        double3 lookAtOffset = AnimationHelpers.GetLookAtOffset(LookAtTargetProvider, controller);
        double3 cameraOffset = lookAtOffset + _startOffset + worldOffset;
        AnimationHelpers.SetTargetOffset(controller, transform, cameraOffset);
        AnimationHelpers.LookAtOffset(transform, cameraOffset, lookAtOffset);

        if (elapsedTime < deltaTime * 1.5)
        {
            Console.WriteLine($"[PanAnimation] First frame: t={t:F4}, easedT={easedT:F4}, position={transform.PositionEcl}");
        }

        if (isComplete)
        {
            Console.WriteLine($"[PanAnimation] Complete: finalPosition={transform.PositionEcl}");
        }

        return isComplete;
    }

    public void Reset()
    {
        _startOffset = double3.Zero;
        _cameraRight = double3.Zero;
        _cameraUp = double3.Zero;
        _cameraForward = double3.Zero;
    }

    public Dictionary<string, string> GetDisplayProperties()
    {
        return new Dictionary<string, string>
        {
            { "Offset", $"({OffsetX:F1}, {OffsetY:F1}, {OffsetZ:F1})m" },
            { "Duration", $"{DurationSeconds:F1}s" },
            { "Easing", Easing.ToString() },
            { "Easing Power (Start)", $"{EasingPowerStart:F1}" },
            { "Easing Power (End)", $"{EasingPowerEnd:F1}" }
        };
    }
}
