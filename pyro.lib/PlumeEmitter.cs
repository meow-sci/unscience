using System;
using Brutal.Numerics;
using HarmonyLib;
using KSA;

namespace MeowSci.PyroLib;

/// <summary>
/// Turns a <see cref="PlumeEntry"/> into a renderer submission each frame, following the exact path the game
/// uses for real nozzles (<c>RocketNozzleState.AddExhaustInstance</c>): update the instance's transient state,
/// then hand the renderer an emitter position/axis in camera-ego space.
/// </summary>
public static class PlumeEmitter
{
    /// <summary>Base exhaust axis in part-local space; the game's engines all point their nozzles down -X.</summary>
    public static readonly float3 BaseAxis = new float3(-1f, 0f, 0f);

    // The per-instance shader struct is private; we poke absorption/refraction into it so that per-plume
    // look tweaks never touch the shared template (which every real engine also reads).
    private static readonly AccessTools.FieldRef<VolumetricExhaustInstance, ExhaustInstance>? ShaderDataRef =
        TryGetShaderDataRef();

    private static AccessTools.FieldRef<VolumetricExhaustInstance, ExhaustInstance>? TryGetShaderDataRef()
    {
        try { return AccessTools.FieldRefAccess<VolumetricExhaustInstance, ExhaustInstance>("_shaderData"); }
        catch (Exception ex)
        {
            Console.WriteLine($"pyro: VolumetricExhaustInstance._shaderData not found — per-plume look overrides disabled: {ex.Message}");
            return null;
        }
    }

    public static bool PerPlumeLookAvailable => ShaderDataRef != null;

    /// <summary>Submits one plume to the renderer. Returns false (and sets LastError) when the plume cannot render.</summary>
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

        // Sample absolute simulation time so multiple render submissions cannot advance a cycle twice.
        plume.Cycle.Update(simulationTime);
        bool active = plume.EffectiveEnabled && plume.Throttle > 0f;
        if (!PlumePhysics.TryCompute(plume.Nozzle, template, ambientPressurePa, out var plumeData))
        {
            plume.LastError = "Nozzle settings produce a non-finite plume; adjust radius/pressure.";
            return false;
        }
        plume.LastError = null;

        // UpdateState tracks startup/shutdown pulses; false = nothing visible this frame (fully shut down).
        if (!instance.UpdateState(simulationTime, active, simulationDeltaTime, plumeData))
            return true;

        ApplyLookOverrides(plume, instance, template);

        var vehicle = plume.Vehicle;
        var part = plume.Part;

        doubleQuat offsetRotation = RotationHelper.FromEulerDegrees(plume.Rotation);
        double3 axisPart = double3.Unpack(in BaseAxis).Transform(offsetRotation);
        double3 positionPart = double3.Unpack(in plume.Position);

        // part local → vehicle assembly → body → world (camera-ego) — same chain as the game's nozzle FX.
        double3 positionVehicleAsmb = positionPart.Transform(part.MatrixAsmb2VehicleAsmb);
        double3 axisVehicleAsmb = axisPart.Transform(part.Asmb2VehicleAsmb);

        double3 positionEgo = camera.GetPositionEgo(vehicle)
                              + vehicle.PosAsmbToBody(positionVehicleAsmb).Transform(vehicle.Body2Cce);
        double3 axisWorld = axisVehicleAsmb.NormalizeOrZero().Transform(vehicle.Body2Cce);

        ComputeAirState(vehicle, out float3 airVelocity, out float airDensity);
        renderer.AddInstance(float3.Pack(in positionEgo), float3.Pack(in axisWorld), instance, plume.Throttle,
            airVelocity, airDensity);
        return true;
    }

    /// <summary>
    /// Air velocity (ego/CCE frame) and ambient air density the renderer uses to fold and bend the plume in
    /// atmosphere — the same derivation as <c>Vehicle.AddVolumetricExhaustInstances</c> (KSA 5402+).
    /// Density is 0 in vacuum or when the parent body has no atmosphere.
    /// </summary>
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

    private static void ApplyLookOverrides(PlumeEntry plume, VolumetricExhaustInstance instance,
        VolumetricExhaustTemplate template)
    {
        if (ShaderDataRef == null) return;
        ref ExhaustInstance data = ref ShaderDataRef(instance);
        data.absorptionDensity = (float)(template.Absorption.Density.Value * plume.AbsorptionDensityScale);
        data.refractionIntensity = plume.RefractionIntensity;
    }
}
