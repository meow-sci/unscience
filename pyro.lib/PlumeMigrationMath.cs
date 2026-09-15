using System;

namespace MeowSci.PyroLib;

/// <summary>Small game-independent guards shared by the synthetic plume input path.</summary>
public static class PlumeMigrationMath
{
    /// <summary>Pressure remains non-zero while an instance plays its shutdown transient.</summary>
    public const float MinimumPressureThrottle = 0.001f;

    public static float ClampThrottle(float throttle)
    {
        return float.IsFinite(throttle) ? Math.Clamp(throttle, 0f, 1f) : 0f;
    }

    public static float SyntheticChamberPressurePa(float chamberPressureBar, float throttle)
    {
        float bar = float.IsFinite(chamberPressureBar) ? Math.Max(chamberPressureBar, 0.01f) : 0.01f;
        return bar * 100000f * Math.Max(ClampThrottle(throttle), MinimumPressureThrottle);
    }

    /// <summary>
    /// Scales the final renderer value relative to the template value. This keeps KSA's atmospheric and
    /// exhaust-temperature multiplier intact after AddInstanceCore rebuilds the struct from the template.
    /// </summary>
    public static float RefractionScale(float templateIntensity, float requestedIntensity)
    {
        if (!float.IsFinite(templateIntensity) || !float.IsFinite(requestedIntensity)) return 1f;
        if (MathF.Abs(templateIntensity) < 0.000001f) return float.NaN;
        return Math.Max(requestedIntensity, 0f) / templateIntensity;
    }

    public static float RefractionFallback(float ambientPressurePa, float exhaustTemperature, float requestedIntensity)
    {
        // KSA stores the camera pressure in atmospheres and the renderer's transition starts at 0.01 atm.
        float ambientAtm = Math.Clamp(ambientPressurePa / 101325f / 0.01f, 0f, 1f);
        float temperature = Math.Clamp(exhaustTemperature / 2000f, 0f, 1f);
        return Math.Max(float.IsFinite(requestedIntensity) ? requestedIntensity : 0f, 0f) * ambientAtm * temperature;
    }
}
