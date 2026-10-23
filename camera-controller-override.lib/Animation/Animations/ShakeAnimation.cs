using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.CameraControllerOverrideLib.Animation.Animations;

/// <summary>
/// Shake animation that creates a "head shaking" effect using rotational yaw.
/// Uses smooth sinusoidal oscillation with configurable speed and amplitude.
/// </summary>
public class ShakeAnimation : IKeyframeAnimation
{
    // Configuration properties
    public double DurationSeconds { get; }
    public int ShakeCount { get; }
    public double AmplitudeDegrees { get; }
    public double ShakeSpeed { get; }
    public EasingType Easing { get; }
    public double EasingPowerStart { get; }
    public double EasingPowerEnd { get; }
    
    // Runtime state
    private doubleQuat _startRotation;
    private double _lastYawOffset;
    private bool _isInitialized;
    
    // Interface properties
    public string Name => "Shake";
    public string Description => "Head-shaking yaw rotation";
    public Func<Controller, double3>? LookAtTargetProvider { get; set; }
    
    /// <summary>
    /// Create a new shake animation.
    /// </summary>
    /// <param name="durationSeconds">Total duration of the animation.</param>
    /// <param name="shakeCount">Number of back/forth oscillations.</param>
    /// <param name="amplitudeDegrees">How far the yaw rotates from center (extent).</param>
    /// <param name="shakeSpeed">Acceleration/snap factor affecting how quickly it transitions.</param>
    /// <param name="easing">Easing function for overall animation progress.</param>
    /// <param name="easingPowerStart">Power parameter for easing at animation start (default 3.0).</param>
    /// <param name="easingPowerEnd">Power parameter for easing at animation end (default 3.0).</param>
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
    
    public void Initialize(Controller controller, Transform3D transform)
    {
        _startRotation = transform.LocalRotation;
        _lastYawOffset = 0.0;
        _isInitialized = true;
        
        Console.WriteLine($"[ShakeAnimation] Initialize: rotation={_startRotation}, amplitude={AmplitudeDegrees}°, count={ShakeCount}, speed={ShakeSpeed}x");
    }
    
    public bool Update(Controller controller, Transform3D transform, double deltaTime, double elapsedTime)
    {
        if (!_isInitialized)
        {
            return true;
        }
        
        // Calculate shake phase based on elapsed time
        // Multiply by 2π to convert full cycles to radians
        double phase = (elapsedTime / DurationSeconds) * ShakeCount * 2.0 * Math.PI * ShakeSpeed;
        
        // Calculate current yaw offset using sinusoidal oscillation
        double yawOffset = Math.Sin(phase) * AmplitudeDegrees;
        
        // Calculate INCREMENTAL rotation for this frame
        double deltaYaw = yawOffset - _lastYawOffset;
        _lastYawOffset = yawOffset;
        
        // Get camera's current up vector (local Y axis transformed by current rotation)
        double3 up = double3.UnitY.Transform(transform.LocalRotation);
        
        // Create yaw rotation quaternion around the up axis
        double deltaYawRadians = deltaYaw * Math.PI / 180.0;
        doubleQuat yawRotation = doubleQuat.CreateFromAxisAngle(up, deltaYawRadians);
        
        // Apply incremental rotation to current rotation
        transform.LocalRotation = yawRotation * transform.LocalRotation;
        
        // Maintain look-at behavior if provider is set
        if (LookAtTargetProvider != null)
        {
            double3 lookAtOffset = AnimationHelpers.GetLookAtOffset(LookAtTargetProvider, controller);
            AnimationHelpers.LookAtOffset(transform, AnimationHelpers.GetTargetOffset(controller, transform), lookAtOffset);
        }
        
        // Log on first frame
        if (elapsedTime < deltaTime * 1.5)
        {
            Console.WriteLine($"[ShakeAnimation] First frame: elapsed={elapsedTime:F4}, phase={phase:F4}, yawOffset={yawOffset:F4}°");
        }
        
        // Check completion
        bool isComplete = elapsedTime >= DurationSeconds;
        if (isComplete)
        {
            Console.WriteLine($"[ShakeAnimation] Complete: finalYawOffset={_lastYawOffset:F2}°");
        }
        
        return isComplete;
    }
    
    public void Reset()
    {
        _startRotation = doubleQuat.Identity;
        _lastYawOffset = 0.0;
        _isInitialized = false;
    }
    
    public Dictionary<string, string> GetDisplayProperties()
    {
        return new Dictionary<string, string>
        {
            { "Shake Count", $"{ShakeCount}" },
            { "Amplitude", $"{AmplitudeDegrees:F1}°" },
            { "Speed", $"{ShakeSpeed:F1}x" },
            { "Duration", $"{DurationSeconds:F1}s" },
            { "Easing", Easing.ToString() },
            { "Easing Power (Start)", $"{EasingPowerStart:F1}" },
            { "Easing Power (End)", $"{EasingPowerEnd:F1}" }
        };
    }
}
