using System;
using Brutal.Numerics;
using MeowSci.KsaAbstractions;

namespace MeowSci.ZippoLib;

/// <summary>
/// A single animation step that interpolates a light's color and intensity from start
/// values to end values over a specified duration using configurable easing.
/// </summary>
public partial class LightAnimation
{
    public float3 StartColor { get; }
    public float3 EndColor { get; }
    public float StartIntensity { get; }
    public float EndIntensity { get; }
    public double DurationSeconds { get; }
    public EasingType Easing { get; }
    public double EasingPowerStart { get; }
    public double EasingPowerEnd { get; }
    public double ElapsedSeconds { get; private set; }

    public bool IsComplete => ElapsedSeconds >= DurationSeconds;

    public LightAnimation(
        float3 startColor, float3 endColor,
        float startIntensity, float endIntensity,
        double durationSeconds, EasingType easing,
        double easingPowerStart = 3.0, double easingPowerEnd = 3.0)
    {
        StartColor = startColor;
        EndColor = endColor;
        StartIntensity = startIntensity;
        EndIntensity = endIntensity;
        DurationSeconds = durationSeconds;
        Easing = easing;
        EasingPowerStart = easingPowerStart;
        EasingPowerEnd = easingPowerEnd;
    }

    /// <summary>
    /// Advances the animation by dt seconds and returns the interpolated (color, intensity) for this frame.
    /// When complete, returns exact end values.
    /// </summary>
    public (float3 Color, float Intensity) Update(double dt)
    {
        ElapsedSeconds += dt;

        if (ElapsedSeconds >= DurationSeconds)
        {
            return (EndColor, EndIntensity);
        }

        double rawT = ElapsedSeconds / DurationSeconds;
        float t = (float)EasingHelper.ApplyEasing(rawT, Easing, EasingPowerStart, EasingPowerEnd);

        var color = Lerp(StartColor, EndColor, t);
        float intensity = StartIntensity + (EndIntensity - StartIntensity) * t;

        return (color, intensity);
    }

    private static float3 Lerp(float3 a, float3 b, float t) =>
        new float3(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t);
}
