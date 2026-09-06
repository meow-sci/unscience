using Brutal.Numerics;
using KSA;

namespace MeowSci.PyroLib;

/// <summary>Physical nozzle parameters that drive the plume's size, shape and shock structure.</summary>
public sealed class NozzleSettings
{
    /// <summary>Visual nozzle exit radius (m). Drives the plume's base width.</summary>
    public float ExitRadius = 0.72f;
    /// <summary>Throat radius (m). Together with ExitRadius sets the area ratio → exit Mach / expansion.</summary>
    public float ThroatRadius = 0.103f;
    /// <summary>Chamber (stagnation) pressure in bar.</summary>
    public float ChamberPressureBar = 49f;
    /// <summary>Chamber (stagnation) temperature in kelvin.</summary>
    public float ChamberTemperatureK = 3400f;
    /// <summary>Ratio of specific heats of the exhaust gas.</summary>
    public float Gamma = 1.2f;
    /// <summary>Specific gas constant of the exhaust gas (J/kg·K).</summary>
    public float GasConstant = 350f;

    public NozzleSettings Clone() => (NozzleSettings)MemberwiseClone();
}

/// <summary>A single standalone volumetric plume welded to a part on a vehicle.</summary>
public sealed class PlumeEntry
{
    private static int _nextId = 1;

    public int Id { get; } = _nextId++;

    public Vehicle Vehicle = null!;
    /// <summary>Anchor part (a top-level part or one of its sub-parts). Offsets are in this part's local frame.</summary>
    public Part Part = null!;

    /// <summary>Translation offset from the anchor part origin, in the part's local (asmb) frame (m).</summary>
    public float3 Position;
    /// <summary>Rotation offset in degrees about the part-local X/Y/Z axes, applied to the base exhaust axis (-X).</summary>
    public float3 Rotation;

    /// <summary>Quick on/off. Off plays the template's shutdown transient then stops rendering.</summary>
    public bool Enabled = true;
    /// <summary>Runtime-only cycle; presets deliberately do not capture it.</summary>
    public PlumeCycle Cycle { get; } = new();
    public bool EffectiveEnabled => Enabled && (!Cycle.Running || Cycle.IsOn);
    /// <summary>0..1 throttle fed to the template's throttle modifier curves.</summary>
    public float Throttle = 1f;

    public string TemplateId = "EngineALarge";
    public NozzleSettings Nozzle = new();

    /// <summary>Multiplier on the template's absorption density (per-plume, does not touch the shared template).</summary>
    public float AbsorptionDensityScale = 1f;
    /// <summary>Per-plume refraction (heat-haze) intensity override.</summary>
    public float RefractionIntensity = 1f;

    /// <summary>Live game-side instance driving transients and shader data. Rebuilt when the template changes.</summary>
    public VolumetricExhaustInstance? Instance;
    public string? LastError;
}
