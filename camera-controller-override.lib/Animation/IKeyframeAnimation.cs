using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.CameraControllerOverrideLib.Animation;

/// <summary>
/// Core interface for all keyframe animations.
/// Implementations must handle initialization, per-frame updates, and lifecycle management.
/// </summary>
public interface IKeyframeAnimation
{
    /// <summary>
    /// Display name for the animation (e.g., "Zoom Out", "Orbit").
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Brief description of what this animation does.
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Total duration of the animation in seconds.
    /// </summary>
    double DurationSeconds { get; }
    
    /// <summary>
    /// Easing function applied to animation progress.
    /// </summary>
    EasingType Easing { get; }
    
    /// <summary>
    /// Power parameter for acceleration phase of easing function.
    /// Used by EaseIn and the first half of EaseInOut.
    /// Controls the "strength" of the easing curve:
    /// - 1.0 = linear (no easing)
    /// - 2.0 = quadratic
    /// - 3.0 = cubic (default)
    /// - Higher values = more extreme easing effect
    /// </summary>
    double EasingPowerStart { get; }
    
    /// <summary>
    /// Power parameter for deceleration phase of easing function.
    /// Used by EaseOut and the second half of EaseInOut.
    /// Controls the "strength" of the easing curve:
    /// - 1.0 = linear (no easing)
    /// - 2.0 = quadratic
    /// - 3.0 = cubic (default)
    /// - Higher values = more extreme easing effect
    /// </summary>
    double EasingPowerEnd { get; }
    
    /// <summary>
    /// Optional override for look-at target position (absolute ECL).
    /// If null, uses the controller's Following target. Animations convert it into an offset from
    /// the follow target each frame, so only the provider's own position is absolute.
    /// </summary>
    Func<Controller, double3>? LookAtTargetProvider { get; set; }
    
    /// <summary>
    /// Initialize the animation state before first Update call.
    /// Captures starting position, rotation, and any other runtime state.
    /// </summary>
    /// <param name="controller">The current camera controller.</param>
    /// <param name="transform">The camera transform.</param>
    void Initialize(Controller controller, Transform3D transform);
    
    /// <summary>
    /// Update the animation for the current frame.
    /// </summary>
    /// <param name="controller">The current camera controller.</param>
    /// <param name="transform">The camera transform to modify.</param>
    /// <param name="deltaTime">Time elapsed since last frame in seconds.</param>
    /// <param name="elapsedTime">Total elapsed time since animation started in seconds.</param>
    /// <returns>True if animation is complete, false if still running.</returns>
    bool Update(Controller controller, Transform3D transform, double deltaTime, double elapsedTime);
    
    /// <summary>
    /// Reset all runtime state to allow replaying the animation.
    /// </summary>
    void Reset();
    
    /// <summary>
    /// Get a dictionary of display properties for UI rendering.
    /// Keys are property names, values are formatted strings.
    /// </summary>
    /// <returns>Dictionary of property name to display value.</returns>
    Dictionary<string, string> GetDisplayProperties();
}
