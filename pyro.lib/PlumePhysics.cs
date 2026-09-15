using System;
using KSA;

namespace MeowSci.PyroLib;

/// <summary>
/// Builds the same complete <see cref="PlumeData"/> that a live rocket nozzle supplies to KSA's renderer.
/// Pressures are pascals internally; the user-facing chamber pressure remains in bar.
/// </summary>
public static class PlumePhysics
{
    private const float MaxExpansionMach = 1f;

    /// <summary>Compatibility overload: computes a full throttle plume.</summary>
    public static bool TryCompute(NozzleSettings n, VolumetricExhaustTemplate template, float ambientPressurePa,
        out PlumeData plume) => TryCompute(n, template, ambientPressurePa, 1f, out plume);

    /// <summary>
    /// Computes an isentropic chamber → throat → exit plume. KSA removed its throttle modifier curves, so
    /// throttle is represented by synthetic chamber pressure while preserving a small pressure during the
    /// shutdown path; the caller keeps the instance's last active data for the visual fade.
    /// </summary>
    public static bool TryCompute(NozzleSettings n, VolumetricExhaustTemplate template, float ambientPressurePa,
        float throttle, out PlumeData plume)
    {
        plume = PlumeData.Zero;

        float exitRadius = Math.Clamp(FiniteOr(n.ExitRadius, 0.72f), 0.001f, 20f);
        float throatRadius = Math.Clamp(FiniteOr(n.ThroatRadius, 0.103f), 0.0005f, exitRadius * 0.999f);
        float gamma = Math.Clamp(FiniteOr(n.Gamma, 1.2f), 1.05f, 1.67f);
        float gasConstant = Math.Max(FiniteOr(n.GasConstant, 350f), 10f);
        float chamberTemperature = Math.Max(FiniteOr(n.ChamberTemperatureK, 3400f), 50f);
        float chamberPressure = PlumeMigrationMath.SyntheticChamberPressurePa(n.ChamberPressureBar, throttle);
        float ambient = FiniteOr(ambientPressurePa, 0f);

        var gas = new GasProperties { Gamma = gamma, SpecificGasConstant = gasConstant };
        float areaRatio = (exitRadius * exitRadius) / (throatRadius * throatRadius);
        float exitMach = RocketDesign.SolveMachNumberFromAreaRatio(gas, areaRatio);
        if (!float.IsFinite(exitMach) || exitMach < MaxExpansionMach) exitMach = MaxExpansionMach;

        float stagnationFactor = 1f + 0.5f * (gamma - 1f) * exitMach * exitMach;
        float exitPressure = chamberPressure * MathF.Pow(stagnationFactor, -gamma / (gamma - 1f));
        float exitTemperature = chamberTemperature / stagnationFactor;
        var exhaust = new GasConditions { Pressure = exitPressure, Temperature = exitTemperature };
        var chamber = new GasConditions { Pressure = chamberPressure, Temperature = chamberTemperature };

        float soundSpeed = gas.ComputeSpeedOfSound(exitTemperature);
        float coreVelocity = exitMach * soundSpeed;
        float exitDensity = exhaust.ComputeDensity(gas);
        float throatDensity = chamber.ComputeDensity(gas);
        float densityThreshold = ComputeMinVisibleDensity(template, exitRadius);
        if (!float.IsFinite(soundSpeed) || !float.IsFinite(coreVelocity) || !float.IsFinite(exitDensity)
            || !float.IsFinite(throatDensity) || !float.IsFinite(densityThreshold) || exitDensity <= 0f)
            return false;

        // PlumeData.Compute owns the derived mass flow, expansion ratios, core Mach and apparent speed. Using
        // it avoids silently dropping fields when KSA extends this struct again.
        plume = PlumeData.Compute(in gas, in exhaust, exitDensity, coreVelocity, exitRadius, chamberPressure,
            ambient, exitMach, densityThreshold, throatRadius, throatDensity);
        return IsFinite(plume);
    }

    /// <summary>Uses KSA's public visibility formula so template edits and engine plumes stay in lockstep.</summary>
    public static float ComputeMinVisibleDensity(VolumetricExhaustTemplate template, float exitRadius)
    {
        return RocketNozzle.ComputeMinGasVisibilityDensity(template, exitRadius);
    }

    /// <summary>Ambient pressure at the camera in pascals (KSA's helper returns atmospheres).</summary>
    public static float AmbientPressurePa(Camera camera)
    {
        const double paPerAtm = 101325.0;
        try { return (float)(PhysicalAtmosphereReference.GetAtmosphericPressure(camera) * paPerAtm); }
        catch { return 0f; }
    }

    private static float FiniteOr(float value, float fallback) => float.IsFinite(value) ? value : fallback;

    private static bool IsFinite(in PlumeData value)
    {
        return float.IsFinite(value.Gas.Gamma)
            && float.IsFinite(value.Gas.SpecificGasConstant)
            && float.IsFinite(value.Exhaust.Pressure)
            && float.IsFinite(value.Exhaust.Temperature)
            && float.IsFinite(value.ApparentExhaustVelocity)
            && float.IsFinite(value.NozzleExitRadius)
            && float.IsFinite(value.NozzlePressureRatio)
            && float.IsFinite(value.StagnationPressure)
            && float.IsFinite(value.JetExpansionRatio)
            && float.IsFinite(value.ExpansionAngle)
            && float.IsFinite(value.ExpansionRadius)
            && float.IsFinite(value.Density)
            && float.IsFinite(value.DensityThreshold)
            && float.IsFinite(value.MachNumber)
            && float.IsFinite(value.CoreVelocity)
            && float.IsFinite(value.MassFlow)
            && float.IsFinite(value.MachAngle)
            && float.IsFinite(value.FullyExpandedMach)
            && float.IsFinite(value.DesignMach)
            && float.IsFinite(value.MachDiskAreaRatio)
            && float.IsFinite(value.ExhaustTemperature)
            && float.IsFinite(value.ThroatRadius)
            && float.IsFinite(value.ThroatDensity);
    }
}
