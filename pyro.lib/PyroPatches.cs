using System;
using System.Reflection;
using HarmonyLib;
using KSA;

namespace MeowSci.PyroLib;

/// <summary>
/// Harmony surface for Pyro. Vehicle submission is patched at the current three-argument API, while the
/// final ExhaustInstance enqueue seam is scoped to a Pyro-owned direct renderer submission.
/// </summary>
public static class PyroPatches
{
    [ThreadStatic]
    private static LookOverride? _activeLookOverride;

    public static bool LookOverrideAvailable { get; private set; }

    private static MethodBase? VehicleTarget() => AccessTools.Method(typeof(Vehicle),
        nameof(Vehicle.AddVolumetricExhaustInstances),
        new[] { typeof(Camera), typeof(VolumetricExhaustRenderer), typeof(double) });

    private static MethodBase? FinalInstanceTarget() => AccessTools.Method(typeof(VolumetricExhaustRenderer),
        nameof(VolumetricExhaustRenderer.AddInstance), new[] { typeof(ExhaustInstance), typeof(int) });

    private static MethodInfo VehiclePostfix() =>
        AccessTools.Method(typeof(PyroPatches), nameof(AfterAddVolumetricExhaustInstances))!;

    private static MethodInfo FinalInstancePrefix() =>
        AccessTools.Method(typeof(PyroPatches), nameof(BeforeFinalInstanceSubmission))!;

    public static void Apply(Harmony harmony)
    {
        var vehicleTarget = VehicleTarget();
        var finalTarget = FinalInstanceTarget();
        if (vehicleTarget == null)
            throw new MissingMethodException(typeof(Vehicle).FullName, nameof(Vehicle.AddVolumetricExhaustInstances));
        if (finalTarget == null)
            throw new MissingMethodException(typeof(VolumetricExhaustRenderer).FullName, "AddInstance(ExhaustInstance,int)");

        bool vehiclePatched = false;
        try
        {
            harmony.Patch(vehicleTarget, postfix: new HarmonyMethod(VehiclePostfix()));
            vehiclePatched = true;
            harmony.Patch(finalTarget, prefix: new HarmonyMethod(FinalInstancePrefix()));
            LookOverrideAvailable = true;
        }
        catch
        {
            if (vehiclePatched) harmony.Unpatch(vehicleTarget, VehiclePostfix());
            harmony.Unpatch(finalTarget, FinalInstancePrefix());
            LookOverrideAvailable = false;
            throw;
        }
    }

    public static void Remove(Harmony harmony)
    {
        var vehicleTarget = VehicleTarget();
        if (vehicleTarget != null) harmony.Unpatch(vehicleTarget, VehiclePostfix());
        var finalTarget = FinalInstanceTarget();
        if (finalTarget != null) harmony.Unpatch(finalTarget, FinalInstancePrefix());
        LookOverrideAvailable = false;
        _activeLookOverride = null;
    }

    /// <summary>Begins the narrowly scoped final-instance look override used by one Pyro submission.</summary>
    internal static IDisposable BeginLookOverride(float absorptionDensity, float refractionScale,
        float refractionFallback)
    {
        var previous = _activeLookOverride;
        _activeLookOverride = new LookOverride(absorptionDensity, refractionScale, refractionFallback);
        return new LookOverrideScope(previous);
    }

    private static void BeforeFinalInstanceSubmission(ref ExhaustInstance inInstance)
    {
        var look = _activeLookOverride;
        if (look == null) return;
        inInstance.absorptionDensity = look.AbsorptionDensity;
        inInstance.refractionIntensity = float.IsFinite(look.RefractionScale)
            ? inInstance.refractionIntensity * look.RefractionScale
            : look.RefractionFallback;
    }

    private static void AfterAddVolumetricExhaustInstances(Vehicle __instance, Camera camera,
        VolumetricExhaustRenderer renderer, double frameDeltaTime)
    {
        try
        {
            PyroSubmod.Instance?.SubmitPlumes(__instance, camera, renderer, frameDeltaTime);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"pyro: error submitting plumes: {ex.Message}");
        }
    }

    private sealed class LookOverride
    {
        public readonly float AbsorptionDensity;
        public readonly float RefractionScale;
        public readonly float RefractionFallback;

        public LookOverride(float absorptionDensity, float refractionScale, float refractionFallback)
        {
            AbsorptionDensity = absorptionDensity;
            RefractionScale = refractionScale;
            RefractionFallback = refractionFallback;
        }
    }

    private sealed class LookOverrideScope : IDisposable
    {
        private readonly LookOverride? _previous;
        private bool _disposed;

        public LookOverrideScope(LookOverride? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _activeLookOverride = _previous;
        }
    }
}
