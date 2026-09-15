using System;
using Brutal.Numerics;

namespace MeowSci.DentWizardLib;

/// <summary>Inertial launch arithmetic, independent of native game state.</summary>
public static class LaunchMath
{
    public const double MinimumSpeed = 0.001;
    public static bool IsValidSpeed(double speed) => double.IsFinite(speed) && speed >= MinimumSpeed;
    public static bool IsFinite(double3 v) => double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

    /// <summary>Add a relative shot to the hit point's inertial velocity, including rigid-body spin.</summary>
    public static double3 Velocity(double3 direction, double speed, double3 targetVelocity,
        double3 angularVelocity, double3 hitOffset)
    {
        double length = direction.Length();
        if (!IsValidSpeed(speed) || !IsFinite(direction) || !double.IsFinite(length) || length <= 0
            || !IsFinite(targetVelocity) || !IsFinite(angularVelocity) || !IsFinite(hitOffset))
            throw new ArgumentException("Launch requires a finite direction, target state and speed of at least 0.001 m/s.");
        var velocity = targetVelocity + double3.Cross(angularVelocity, hitOffset) + direction / length * speed;
        if (!IsFinite(velocity)) throw new ArgumentException("The resulting launch velocity is not finite.");
        return velocity;
    }
}
