using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.CameraControllerOverrideLib.Animation.Animations;

/// <summary>
/// Circular orbit animation that rotates the camera around a target.
/// Uses absolute rotation of the original starting offset to avoid cumulative error.
/// Tracks the CURRENT target position so orbit follows moving targets.
/// </summary>
public class OrbitAnimation : IKeyframeAnimation
{
    // Configuration properties
    public double Degrees { get; }
    public double DurationSeconds { get; }
    public EasingType Easing { get; }
    public double EasingPowerStart { get; }
    public double EasingPowerEnd { get; }
    
    // Runtime state
    private double3 _orbitAxis;
    private double3 _startOffset;  // Store original offset to avoid cumulative error
    private bool _isInitialized;
    
    // Interface properties
    public string Name => "Orbit";
    public string Description => "Circular orbit around target";
    public Func<Controller, double3>? LookAtTargetProvider { get; set; }
    
    /// <summary>
    /// Create a new orbit animation.
    /// </summary>
    /// <param name="degrees">Total rotation angle in degrees.</param>
    /// <param name="durationSeconds">Total duration of the animation.</param>
    /// <param name="easing">Easing function to apply to the rotation.</param>
    /// <param name="easingPowerStart">Power parameter for easing at animation start (default 3.0).</param>
    /// <param name="easingPowerEnd">Power parameter for easing at animation end (default 3.0).</param>
    public OrbitAnimation(double degrees, double durationSeconds, EasingType easing, double easingPowerStart = 3.0, double easingPowerEnd = 3.0)
    {
        Degrees = degrees;
        DurationSeconds = durationSeconds;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
    }
    
    public void Initialize(Controller controller, Transform3D transform)
    {
        // Get current state to determine orbit axis
        double3 currentOffset = AnimationHelpers.GetTargetOffset(controller, transform);
        
        // Validate radius
        double radius = currentOffset.Length();
        if (radius < 0.01)
        {
            Console.WriteLine("camera-controller-override: Orbit radius too small, cancelling.");
            _isInitialized = false;
            return;
        }
        
        // Store starting offset to avoid cumulative rotation error
        _startOffset = currentOffset;
        
        // Calculate orbit axis (perpendicular to offset - determines rotation direction)
        _orbitAxis = AnimationHelpers.CalculateOrbitAxis(currentOffset, transform.LocalRotation);
        _isInitialized = true;
        
        Console.WriteLine($"[OrbitAnimation] Initialize: offset={currentOffset}, radius={radius:F2}");
    }
    
    public bool Update(Controller controller, Transform3D transform, double deltaTime, double elapsedTime)
    {
        // Skip animation if initialization failed
        if (!_isInitialized)
        {
            return true;
        }
        
        // Get current eased progress (0.0 to 1.0)
        double t = Math.Min(1.0, elapsedTime / DurationSeconds);
        double currentEasedProgress = AnimationHelpers.ApplyEasing(t, Easing, EasingPowerStart, EasingPowerEnd);
        
        // Snap to exactly 1.0 on completion to eliminate floating-point drift
        if (elapsedTime >= DurationSeconds)
        {
            currentEasedProgress = 1.0;
        }
        
        // FIXED: Calculate TOTAL rotation angle from start (not incremental)
        // This eliminates cumulative floating-point error
        double totalAngleDegrees = Degrees * currentEasedProgress;
        double totalAngleRadians = totalAngleDegrees * Math.PI / 180.0;
        
        // FIXED: Apply rotation to ORIGINAL starting offset
        double3 k = _orbitAxis;
        double cos = Math.Cos(totalAngleRadians);
        double sin = Math.Sin(totalAngleRadians);
        double3 rotatedOffset = _startOffset * cos 
            + double3.Cross(k, _startOffset) * sin 
            + k * double3.Dot(k, _startOffset) * (1.0 - cos);
        
        // Update position relative to the CURRENT target (not starting target) - target may be
        // moving! This allows orbit to follow a moving target while still using absolute rotation
        AnimationHelpers.SetTargetOffset(controller, transform, rotatedOffset);
        
        // Log on first frame
        if (elapsedTime < deltaTime * 1.5)
        {
            Console.WriteLine($"[OrbitAnimation] First frame: elapsed={elapsedTime:F4}, totalAngle={totalAngleDegrees:F4}°");
        }
        
        // Maintain look-at behavior
        double3 lookAtOffset = AnimationHelpers.GetLookAtOffset(LookAtTargetProvider, controller);
        AnimationHelpers.LookAtOffset(transform, rotatedOffset, lookAtOffset);
        
        // Log on completion
        bool isComplete = elapsedTime >= DurationSeconds;
        if (isComplete)
        {
            Console.WriteLine($"[OrbitAnimation] Complete: totalAngle={totalAngleDegrees:F2}°, finalPos={transform.PositionEcl}");
        }
        
        return isComplete;
    }
    
    public void Reset()
    {
        _orbitAxis = double3.Zero;
        _startOffset = double3.Zero;
        _isInitialized = false;
    }
    
    public Dictionary<string, string> GetDisplayProperties()
    {
        return new Dictionary<string, string>
        {
            { "Degrees", $"{Degrees:F1}°" },
            { "Duration", $"{DurationSeconds:F1}s" },
            { "Easing", Easing.ToString() },
            { "Easing Power (Start)", $"{EasingPowerStart:F1}" },
            { "Easing Power (End)", $"{EasingPowerEnd:F1}" }
        };
    }
}
