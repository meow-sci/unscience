using System;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;
using EasingType = MeowSci.KsaAbstractions.EasingType;

namespace MeowSci.CameraControllerOverrideLib.Animation;

/// <summary>
/// Shared helper methods for camera animations.
/// Extracted from Patcher.cs for reusability across animation implementations.
/// </summary>
public static class AnimationHelpers
{
    /// <summary>
    /// Apply an easing function to a normalized time value [0, 1].
    /// </summary>
    /// <param name="t">Normalized time in range [0, 1].</param>
    /// <param name="easingType">The easing function to apply.</param>
    /// <param name="powerStart">Power parameter for acceleration (used by EaseIn and first half of EaseInOut, default 3.0).</param>
    /// <param name="powerEnd">Power parameter for deceleration (used by EaseOut and second half of EaseInOut, default 3.0).</param>
    /// <returns>Eased time value.</returns>
    public static double ApplyEasing(double t, EasingType easingType, double powerStart = 3.0, double powerEnd = 3.0)
        => EasingHelper.ApplyEasing(t, easingType, powerStart, powerEnd);
    
    /// <summary>
    /// Get the target position from the controller's Following object.
    /// Absolute ECL: only use it to convert a custom absolute look-at provider into an offset.
    /// </summary>
    /// <param name="controller">The camera controller.</param>
    /// <param name="fallback">Fallback position if no target available.</param>
    /// <returns>Target position in ecliptic coordinates.</returns>
    public static double3 GetTargetPosition(Controller controller, double3 fallback = default)
        => controller?.Camera?.Following?.GetPositionEcl() ?? fallback;

    /// <summary>
    /// Camera position relative to its follow target, in ECL axes. All animations work in this
    /// target-relative space. A following KSA camera stores exactly this offset
    /// (<c>Camera.PositionCce</c>), so it is read directly instead of subtracting two absolute
    /// ECL positions, whose double precision degrades to metres at interstellar distances.
    /// Without a follow target the offset is the absolute position.
    /// </summary>
    public static double3 GetTargetOffset(Controller controller, Transform3D transform)
    {
        if (transform is Camera { Following: not null } camera)
            return camera.PositionCce;
        return transform.PositionEcl - GetTargetPosition(controller);
    }

    /// <summary>Place the camera at <paramref name="offset"/> from its follow target (see <see cref="GetTargetOffset"/>).</summary>
    public static void SetTargetOffset(Controller controller, Transform3D transform, double3 offset)
    {
        if (transform is Camera { Following: not null } camera)
            camera.PositionCce = offset;
        else
            transform.PositionEcl = GetTargetPosition(controller) + offset;
    }

    /// <summary>
    /// Look-at point relative to the follow target: <paramref name="defaultOffset"/> unless a
    /// custom absolute <paramref name="provider"/> is set, which is converted once per frame.
    /// </summary>
    public static double3 GetLookAtOffset(Func<Controller, double3>? provider, Controller controller,
        double3 defaultOffset = default)
        => provider == null ? defaultOffset : provider(controller) - GetTargetPosition(controller);

    /// <summary>
    /// Orient the transform from <paramref name="cameraOffset"/> toward <paramref name="lookAtOffset"/>,
    /// both relative to the follow target, keeping the current up vector.
    /// </summary>
    public static void LookAtOffset(Transform3D transform, double3 cameraOffset, double3 lookAtOffset)
    {
        double3 lookDirection = lookAtOffset - cameraOffset;
        if (lookDirection.LengthSquared() > 0.0001)
        {
            double3 currentUp = double3.UnitY.Transform(transform.LocalRotation);
            transform.LocalRotation = Camera.LookAtRotation(lookDirection, currentUp);
        }
    }
    
    /// <summary>
    /// Calculate the eased progress for the current frame.
    /// Returns the delta between current eased progress and previous frame's eased progress.
    /// </summary>
    /// <param name="elapsed">Total elapsed time.</param>
    /// <param name="duration">Total animation duration.</param>
    /// <param name="deltaTime">Time since last frame.</param>
    /// <param name="easingType">Easing function to apply.</param>
    /// <param name="powerStart">Power parameter for acceleration (default 3.0).</param>
    /// <param name="powerEnd">Power parameter for deceleration (default 3.0).</param>
    /// <returns>Eased progress delta for this frame.</returns>
    public static double GetEasedFrameProgress(double elapsed, double duration, double deltaTime, EasingType easingType, double powerStart = 3.0, double powerEnd = 3.0)
    {
        double t = Math.Min(1.0, elapsed / duration);
        double lastT = Math.Max(0.0, (elapsed - deltaTime) / duration);
        return ApplyEasing(t, easingType, powerStart, powerEnd) - ApplyEasing(lastT, easingType, powerStart, powerEnd);
    }
    
    /// <summary>
    /// Calculate the orbit axis perpendicular to the offset from target.
    /// Uses camera's up vector and offset to determine rotation axis.
    /// </summary>
    /// <param name="startOffset">Offset vector from target to camera start position.</param>
    /// <param name="startRotation">Camera's starting rotation quaternion.</param>
    /// <returns>Normalized orbit axis vector.</returns>
    public static double3 CalculateOrbitAxis(double3 startOffset, doubleQuat startRotation)
    {
        double3 startUp = double3.UnitY.Transform(startRotation);
        if (startUp.LengthSquared() < 0.00000001) startUp = double3.UnitY;
        
        double3 right = double3.Cross(startUp, startOffset);
        if (right.LengthSquared() < 0.0001)
        {
            double3 offsetDir = double3.Normalize(startOffset);
            double3 fallback = Math.Abs(double3.Dot(offsetDir, double3.UnitY)) < 0.99 ? double3.UnitY : double3.UnitX;
            right = double3.Cross(fallback, startOffset);
        }
        return double3.Normalize(double3.Cross(startOffset, right));
    }
}
