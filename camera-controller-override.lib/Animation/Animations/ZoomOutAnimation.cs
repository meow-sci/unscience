using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.CameraControllerOverrideLib.Animation.Animations;

/// <summary>
/// Linear animation that moves the camera away from the target at a constant speed.
/// Each frame, calculates direction from CURRENT camera position to CURRENT target position.
/// This ensures the animation always moves directly away from the target, even if the target moves.
/// </summary>
public class ZoomOutAnimation : IKeyframeAnimation
{
    // Configuration properties
    public double SpeedMetersPerSecond { get; }
    public double DurationSeconds { get; }
    public EasingType Easing { get; }
    public double EasingPowerStart { get; }
    public double EasingPowerEnd { get; }
    
    // Runtime state - only track progress, not positions
    private double _distanceTraveled;
    private double _lastEasedProgress;
    
    // Interface properties
    public string Name => "Zoom Out";
    public string Description => "Linear movement away from target";
    public Func<Controller, double3>? LookAtTargetProvider { get; set; }
    
    /// <summary>
    /// Create a new zoom out animation.
    /// </summary>
    /// <param name="speedMetersPerSecond">Movement speed in meters per second.</param>
    /// <param name="durationSeconds">Total duration of the animation.</param>
    /// <param name="easing">Easing function to apply to the movement.</param>
    /// <param name="easingPowerStart">Power parameter for easing at animation start (default 3.0).</param>
    /// <param name="easingPowerEnd">Power parameter for easing at animation end (default 3.0).</param>
    public ZoomOutAnimation(double speedMetersPerSecond, double durationSeconds, EasingType easing, double easingPowerStart = 3.0, double easingPowerEnd = 3.0)
    {
        SpeedMetersPerSecond = speedMetersPerSecond;
        DurationSeconds = durationSeconds;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
    }
    
    public void Initialize(Controller controller, Transform3D transform)
    {
        // Only reset progress tracking - no position capture needed
        // Direction will be calculated each frame from current positions
        _distanceTraveled = 0.0;
        _lastEasedProgress = 0.0;
        
        Console.WriteLine($"[ZoomOutAnimation] Initialize: pos={transform.PositionEcl}");
    }
    
    public bool Update(Controller controller, Transform3D transform, double deltaTime, double elapsedTime)
    {
        // Calculate total movement distance for the entire animation
        double totalDistance = SpeedMetersPerSecond * DurationSeconds;
        
        // Get current eased progress (0.0 to 1.0)
        double t = Math.Min(1.0, elapsedTime / DurationSeconds);
        double currentEasedProgress = AnimationHelpers.ApplyEasing(t, Easing, EasingPowerStart, EasingPowerEnd);
        
        // Snap to exactly 1.0 on completion to eliminate floating-point drift
        if (elapsedTime >= DurationSeconds)
        {
            currentEasedProgress = 1.0;
        }
        
        // Calculate how much progress we should make THIS frame
        double frameProgress = currentEasedProgress - _lastEasedProgress;
        _lastEasedProgress = currentEasedProgress;
        
        // Get CURRENT offset from the target and calculate direction away from it
        double3 cameraOffset = AnimationHelpers.GetTargetOffset(controller, transform);
        double3 towardTarget = -cameraOffset;
        
        // Handle edge case: camera is at target position
        if (towardTarget.LengthSquared() < 0.0001)
        {
            // Use a default direction (away from origin, or up if at origin)
            towardTarget = transform.PositionEcl.LengthSquared() > 0.0001 
                ? -transform.PositionEcl 
                : new double3(0, 1, 0);
        }
        
        double3 direction = double3.Normalize(-towardTarget);
        
        // Apply displacement for this frame
        double3 displacement = direction * (totalDistance * frameProgress);
        cameraOffset += displacement;
        AnimationHelpers.SetTargetOffset(controller, transform, cameraOffset);
        
        // Log on first frame to verify correct initialization
        if (elapsedTime < deltaTime * 1.5)
        {
            Console.WriteLine($"[ZoomOutAnimation] First frame: elapsed={elapsedTime:F4}, frameProgress={frameProgress:F6}, displacement={displacement.Length():F4}");
        }
        
        // Maintain look-at behavior - use current target
        double3 lookAtOffset = AnimationHelpers.GetLookAtOffset(LookAtTargetProvider, controller);
        AnimationHelpers.LookAtOffset(transform, cameraOffset, lookAtOffset);
        
        // Track distance traveled
        _distanceTraveled += displacement.Length();
        
        // Log on completion
        bool isComplete = elapsedTime >= DurationSeconds;
        if (isComplete)
        {
            Console.WriteLine($"[ZoomOutAnimation] Complete: totalDistanceTraveled={_distanceTraveled:F2}, finalPos={transform.PositionEcl}");
        }
        
        return isComplete;
    }
    
    public void Reset()
    {
        _distanceTraveled = 0.0;
        _lastEasedProgress = 0.0;
    }
    
    public Dictionary<string, string> GetDisplayProperties()
    {
        return new Dictionary<string, string>
        {
            { "Speed", $"{SpeedMetersPerSecond:F1} m/s" },
            { "Duration", $"{DurationSeconds:F1}s" },
            { "Easing", Easing.ToString() },
            { "Easing Power (Start)", $"{EasingPowerStart:F1}" },
            { "Easing Power (End)", $"{EasingPowerEnd:F1}" }
        };
    }
}
