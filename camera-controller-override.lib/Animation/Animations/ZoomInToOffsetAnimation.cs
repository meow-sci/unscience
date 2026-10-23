using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.CameraControllerOverrideLib.Animation.Animations;

/// <summary>
/// Animation that moves the camera toward an offset point from the target center.
/// Each frame, calculates direction from CURRENT camera position to (target + offset).
/// This enables zooming into specific parts of game objects (e.g., an astronaut's helmet).
/// Includes minimum distance safeguard to prevent collision with offset destination.
/// </summary>
public class ZoomInToOffsetAnimation : IKeyframeAnimation
{
    // Configuration properties
    public double SpeedMetersPerSecond { get; }
    public double DurationSeconds { get; }
    public EasingType Easing { get; }
    public double EasingPowerStart { get; }
    public double EasingPowerEnd { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }
    public double OffsetZ { get; }
    
    // Runtime state - only track progress, not positions
    private double _distanceTraveled;
    private double _lastEasedProgress;
    
    // Interface properties
    public string Name => "Zoom In To Offset";
    public string Description => "Linear movement toward offset point from target";
    public Func<Controller, double3>? LookAtTargetProvider { get; set; }
    
    // Minimum distance safeguard (1 meter from offset destination)
    private const double MinimumDistance = 1.0;
    
    /// <summary>
    /// Create a new zoom in to offset animation.
    /// </summary>
    /// <param name="speedMetersPerSecond">Movement speed in meters per second.</param>
    /// <param name="durationSeconds">Total duration of the animation.</param>
    /// <param name="easing">Easing function to apply to the movement.</param>
    /// <param name="offsetX">X-axis offset from target center (meters).</param>
    /// <param name="offsetY">Y-axis offset from target center (meters).</param>
    /// <param name="offsetZ">Z-axis offset from target center (meters).</param>
    /// <param name="easingPowerStart">Power parameter for easing at animation start (default 3.0).</param>
    /// <param name="easingPowerEnd">Power parameter for easing at animation end (default 3.0).</param>
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
    
    public void Initialize(Controller controller, Transform3D transform)
    {
        // Only reset progress tracking - no position capture needed
        // Direction will be calculated each frame from current positions
        _distanceTraveled = 0.0;
        _lastEasedProgress = 0.0;
        
        Console.WriteLine($"[ZoomInToOffsetAnimation] Initialize: pos={transform.PositionEcl}, offset=({OffsetX:F2}, {OffsetY:F2}, {OffsetZ:F2})");
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
        
        // Calculate the offset destination relative to the CURRENT target
        double3 cameraOffset = AnimationHelpers.GetTargetOffset(controller, transform);
        double3 offset = new double3(OffsetX, OffsetY, OffsetZ);
        double3 towardDestination = offset - cameraOffset;
        double distanceToDestination = towardDestination.Length();
        
        // Handle edge case: camera is very close to destination
        if (distanceToDestination < MinimumDistance)
        {
            Console.WriteLine($"[ZoomInToOffsetAnimation] Stopping: Too close to destination (distance={distanceToDestination:F4}m)");
            return true; // Complete animation early
        }
        
        // Calculate displacement for this frame
        double3 direction = double3.Normalize(towardDestination);
        double frameDistance = totalDistance * frameProgress;
        
        // Clamp displacement to not overshoot minimum distance
        if (distanceToDestination - frameDistance < MinimumDistance)
        {
            frameDistance = distanceToDestination - MinimumDistance;
            Console.WriteLine($"[ZoomInToOffsetAnimation] Clamping displacement to maintain minimum distance");
        }
        
        double3 displacement = direction * frameDistance;
        cameraOffset += displacement;
        AnimationHelpers.SetTargetOffset(controller, transform, cameraOffset);
        
        // Log on first frame to verify correct initialization
        if (elapsedTime < deltaTime * 1.5)
        {
            Console.WriteLine($"[ZoomInToOffsetAnimation] First frame: elapsed={elapsedTime:F4}, frameProgress={frameProgress:F6}, displacement={displacement.Length():F4}");
        }
        
        // Smoothly transition look-at from target center to offset destination
        // At progress 0: look at the current target (no offset)
        // At progress 1: look at the current target + offset (full offset)
        // This correctly tracks moving targets throughout the animation
        double3 lookAtOffset = AnimationHelpers.GetLookAtOffset(LookAtTargetProvider, controller,
            offset * currentEasedProgress);
        AnimationHelpers.LookAtOffset(transform, cameraOffset, lookAtOffset);
        
        // Track distance traveled
        _distanceTraveled += displacement.Length();
        
        // Log on completion
        bool isComplete = elapsedTime >= DurationSeconds;
        if (isComplete)
        {
            Console.WriteLine($"[ZoomInToOffsetAnimation] Complete: totalDistanceTraveled={_distanceTraveled:F2}, finalPos={transform.PositionEcl}");
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
            { "Offset", $"({OffsetX:F1}, {OffsetY:F1}, {OffsetZ:F1})m" },
            { "Easing Power (Start)", $"{EasingPowerStart:F1}" },
            { "Easing Power (End)", $"{EasingPowerEnd:F1}" }
        };
    }
}
