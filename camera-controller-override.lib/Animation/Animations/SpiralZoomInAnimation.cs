using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.CameraControllerOverrideLib.Animation.Animations;

/// <summary>
/// Combines zoom-in movement toward the target with spiral rotation around the look-at axis.
/// Creates a "corkscrew" effect as the camera moves closer to the target while spinning.
/// </summary>
public class SpiralZoomInAnimation : IKeyframeAnimation
{
    // Configuration properties
    public double SpeedMetersPerSecond { get; }
    public double DurationSeconds { get; }
    public EasingType Easing { get; }
    public double EasingPowerStart { get; }
    public double EasingPowerEnd { get; }
    public double SpiralDegrees { get; }
    
    // Runtime state
    private double _distanceTraveled;
    private double _totalDegreesRotated;
    private double3 _startUpVector;
    
    // Interface properties
    public string Name => "Spiral Zoom In";
    public string Description => "Zoom toward target with spiral rotation";
    public Func<Controller, double3>? LookAtTargetProvider { get; set; }
    
    // Minimum distance safeguard (1 meter from target)
    private const double MinimumDistance = 1.0;
    
    /// <summary>
    /// Create a new spiral zoom in animation.
    /// </summary>
    /// <param name="speedMetersPerSecond">Movement speed in meters per second.</param>
    /// <param name="durationSeconds">Total duration of the animation.</param>
    /// <param name="easing">Easing function to apply to both movement and rotation.</param>
    /// <param name="spiralDegrees">Total rotation degrees during zoom (positive = clockwise when looking at target).</param>
    /// <param name="easingPowerStart">Power parameter for easing at animation start (default 3.0).</param>
    /// <param name="easingPowerEnd">Power parameter for easing at animation end (default 3.0).</param>
    public SpiralZoomInAnimation(double speedMetersPerSecond, double durationSeconds, EasingType easing, double spiralDegrees, double easingPowerStart = 3.0, double easingPowerEnd = 3.0)
    {
        SpeedMetersPerSecond = speedMetersPerSecond;
        DurationSeconds = durationSeconds;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
        SpiralDegrees = spiralDegrees;
    }
    
    public void Initialize(Controller controller, Transform3D transform)
    {
        // Reset all runtime state
        _distanceTraveled = 0.0;
        _totalDegreesRotated = 0.0;
        
        // Store original up vector at animation start
        _startUpVector = double3.UnitY.Transform(transform.LocalRotation);
        
        Console.WriteLine($"[SpiralZoomInAnimation] Initialize: pos={transform.PositionEcl}, spiralDegrees={SpiralDegrees}");
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
        
        // === ZOOM COMPONENT (from ZoomInAnimation) ===
        
        // Calculate frame progress for zoom movement
        double frameDistance = totalDistance * currentEasedProgress - _distanceTraveled;
        
        // Get CURRENT offset from the target and calculate direction toward it
        double3 cameraOffset = AnimationHelpers.GetTargetOffset(controller, transform);
        double3 towardTarget = -cameraOffset;
        double distanceToTarget = towardTarget.Length();
        
        // Handle edge case: camera is very close to target
        if (distanceToTarget < MinimumDistance)
        {
            Console.WriteLine($"[SpiralZoomInAnimation] Stopping: Too close to target (distance={distanceToTarget:F4}m)");
            return true; // Complete animation early
        }
        
        // Calculate displacement for this frame
        double3 direction = double3.Normalize(towardTarget);
        
        // Clamp displacement to not overshoot minimum distance
        if (distanceToTarget - frameDistance < MinimumDistance)
        {
            frameDistance = distanceToTarget - MinimumDistance;
        }
        
        double3 displacement = direction * frameDistance;
        cameraOffset += displacement;
        AnimationHelpers.SetTargetOffset(controller, transform, cameraOffset);
        _distanceTraveled += displacement.Length();
        
        // === SPIRAL COMPONENT (rotate camera orientation around look-at axis) ===
        
        // Calculate TOTAL angle based on absolute progress (not incremental)
        double totalAngleDegrees = SpiralDegrees * currentEasedProgress;
        double totalAngleRadians = totalAngleDegrees * Math.PI / 180.0;
        
        // Calculate look-at direction (from camera to target)
        double3 lookDirection = -cameraOffset;
        double lookDirectionLength = lookDirection.Length();
        
        if (lookDirectionLength > 0.0001)
        {
            // Normalize to get spiral axis (the look-at direction)
            double3 spiralAxis = double3.Normalize(lookDirection);
            
            // Rotate ORIGINAL up vector by TOTAL angle using Rodrigues' formula
            double3 k = spiralAxis;
            double cos = Math.Cos(totalAngleRadians);
            double sin = Math.Sin(totalAngleRadians);
            double3 rotatedUp = _startUpVector * cos 
                + double3.Cross(k, _startUpVector) * sin 
                + k * double3.Dot(k, _startUpVector) * (1.0 - cos);
            
            // Apply rotation with rotated up vector
            transform.LocalRotation = Camera.LookAtRotation(lookDirection, rotatedUp);
            
            _totalDegreesRotated = totalAngleDegrees;
        }
        
        // Log on first frame to verify correct initialization
        if (elapsedTime < deltaTime * 1.5)
        {
            Console.WriteLine($"[SpiralZoomInAnimation] First frame: elapsed={elapsedTime:F4}, easedProgress={currentEasedProgress:F6}, displacement={displacement.Length():F4}, rotation={totalAngleDegrees:F4}°");
        }
        
        // Log on completion
        bool isComplete = elapsedTime >= DurationSeconds;
        if (isComplete)
        {
            Console.WriteLine($"[SpiralZoomInAnimation] Complete: totalDistanceTraveled={_distanceTraveled:F2}, totalDegreesRotated={_totalDegreesRotated:F2}°, finalPos={transform.PositionEcl}");
        }
        
        return isComplete;
    }
    
    public void Reset()
    {
        _distanceTraveled = 0.0;
        _totalDegreesRotated = 0.0;
        _startUpVector = double3.Zero;
    }
    
    public Dictionary<string, string> GetDisplayProperties()
    {
        return new Dictionary<string, string>
        {
            { "Speed", $"{SpeedMetersPerSecond:F1} m/s" },
            { "Duration", $"{DurationSeconds:F1}s" },
            { "Spiral", $"{SpiralDegrees:F0}°" },
            { "Easing", Easing.ToString() },
            { "Easing Power (Start)", $"{EasingPowerStart:F1}" },
            { "Easing Power (End)", $"{EasingPowerEnd:F1}" }
        };
    }
}
