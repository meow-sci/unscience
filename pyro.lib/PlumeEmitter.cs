using System;
using Brutal.Numerics;
using KSA;

namespace MeowSci.PyroLib;

/// <summary>
/// Turns a <see cref="PlumeEntry"/> into a renderer submission each frame. The instance owns KSA's pulse
/// tracker; this class supplies its complete physical input and camera-space emitter state.
/// </summary>
public static class PlumeEmitter
{
    /// <summary>Base exhaust axis in part-local space; stock KSA nozzles point down -X.</summary>
    public static readonly float3 BaseAxis = new(-1f, 0f, 0f);

    public static bool PerPlumeLookAvailable => PyroPatches.LookOverrideAvailable;

    /// <summary>Submits one plume, retaining its last active physics while a shutdown pulse fades.</summary>
    public static bool Submit(PlumeEntry plume, Camera camera, VolumetricExhaustRenderer renderer,
        double simulationTime, double simulationDeltaTime, float ambientPressurePa)
    {
        var instance = plume.Instance;
        var template = instance?.Template;
        if (instance == null || template == null)
        {
            plume.LastError = $"Template '{plume.TemplateId}' is not loaded.";
            return false;
        }

        plume.Cycle.Update(simulationTime);
        float throttle = PlumeMigrationMath.ClampThrottle(plume.Throttle);
        bool active = plume.EffectiveEnabled && throttle > 0f;
        bool hasCurrentData = PlumePhysics.TryCompute(plume.Nozzle, template, ambientPressurePa, throttle,
            out var plumeData);
        if (!hasCurrentData && active)
        {
            plume.LastError = "Nozzle settings produce a non-finite plume; adjust radius/pressure.";
            return false;
        }

        // An inactive instance may be midway through its shutdown pulse. KSA's new UpdateState no longer
        // accepts PlumeData and deliberately leaves LastPlumeData untouched while inactive, so the renderer
        // continues to size the tail from the last active state.
        if (!hasCurrentData || (!active && instance.LastPlumeData.Exhaust.Pressure > 0f))
            plumeData = instance.LastPlumeData;
        plume.LastError = null;

        doubleQuat offsetRotation = RotationHelper.FromEulerDegrees(plume.Rotation);
        double3 axisPart = double3.Unpack(in BaseAxis).Transform(offsetRotation);
        double3 positionPart = double3.Unpack(in plume.Position);
        double3 positionVehicleAsmb = positionPart.Transform(plume.Part.MatrixAsmb2VehicleAsmb);
        double3 axisVehicleAsmb = axisPart.Transform(plume.Part.Asmb2VehicleAsmb);

        // Part-local → vehicle assembly → body → camera-ego, matching Vehicle.AddVolumetricExhaustInstances.
        double3 positionEgo = camera.GetPositionEgo(plume.Vehicle)
                              + plume.Vehicle.PosAsmbToBody(positionVehicleAsmb).Transform(plume.Vehicle.Body2Cce);
        double3 axisWorld = axisVehicleAsmb.NormalizeOrZero().Transform(plume.Vehicle.Body2Cce);
        float3 emitterPosition = float3.Pack(in positionEgo);
        float3 emitterAxis = float3.Pack(in axisWorld);
        ComputeAirState(plume.Vehicle, out float3 airVelocity, out float airDensity);

        GasProperties gas = plumeData.Gas;
        GasConditions exhaust = plumeData.Exhaust;
        if (active) instance.LastPlumeData = plumeData;
        instance.UpdateState(simulationTime, active, simulationDeltaTime, in gas, in exhaust,
            plumeData.CoreVelocity, emitterPosition, emitterAxis, emitterPosition, emitterAxis,
            ambientPressurePa, airVelocity, airDensity);
        if (!instance.IsLive) return true;

        float requestedRefraction = float.IsFinite(plume.RefractionIntensity)
            ? Math.Max(plume.RefractionIntensity, 0f) : 0f;
        float templateRefraction = (float)template.Absorption.RefractionIntensity.Value;
        float refractionScale = PlumeMigrationMath.RefractionScale(templateRefraction, requestedRefraction);
        float refractionFallback = PlumeMigrationMath.RefractionFallback(ambientPressurePa,
            plumeData.ExhaustTemperature, requestedRefraction);
        float absorption = (float)template.Absorption.Density.Value;
        absorption = Math.Max(float.IsFinite(absorption) ? absorption : 0f, 0f)
                     * (float.IsFinite(plume.AbsorptionDensityScale) ? Math.Max(plume.AbsorptionDensityScale, 0f) : 1f);

        // AddInstanceCore reconstructs absorption/refraction from the shared template. Scope the final
        // ExhaustInstance seam to this one direct submission so stock engines and shared template state stay
        // untouched, even if AddInstance throws.
        ExhaustBendTarget bendTarget = default;
        ExhaustAxialFade fade = ExhaustAxialFade.NoFadeOut;
        ExhaustDiamondFade diamondFade = ExhaustDiamondFade.None;
        IDisposable lookScope = PyroPatches.BeginLookOverride(absorption, refractionScale, refractionFallback);
        try
        {
            renderer.AddInstance(instance, in bendTarget, in fade, in diamondFade);
        }
        finally
        {
            lookScope.Dispose();
        }
        return true;
    }

    /// <summary>Matches the game's atmospheric wind and density derivation for this vehicle.</summary>
    private static void ComputeAirState(Vehicle vehicle, out float3 airVelocity, out float airDensity)
    {
        var parent = vehicle.Parent;
        airVelocity = float3.Pack(vehicle.GetSurfaceVelocityCci().Transform(parent.GetCci2Cce()));
        airDensity = 0f;
        AtmosphereReference? atmosphere = parent.GetAtmosphereReference();
        if (atmosphere == null) return;

        double altitudeMeters = (vehicle.GetPositionEcl() - parent.GetPositionEcl()).Length() - parent.MeanRadius;
        airDensity = (float)atmosphere.Physical.GetAtmosphericDensityAtAltitude(altitudeMeters);
    }
}
