using Brutal.Numerics;

namespace MeowSci.PyroLib;

/// <summary>
/// Named snapshot of every per-plume setting: template, position/rotation offsets, throttle,
/// nozzle physics and look overrides. The vehicle/part anchor is deliberately not part of a
/// preset — presets describe how a plume looks and behaves, not where it is welded.
/// </summary>
public sealed class PlumePreset
{
    public string TemplateId = "EngineALarge";
    public float3 Position;
    public float3 Rotation;
    public float Throttle = 1f;
    public NozzleSettings Nozzle = new();
    public float AbsorptionDensityScale = 1f;
    public float RefractionIntensity = 1f;

    public PlumePreset Clone()
    {
        var clone = (PlumePreset)MemberwiseClone();
        clone.Nozzle = Nozzle.Clone();
        return clone;
    }

    /// <summary>Captures a plume's current settings as a preset.</summary>
    public static PlumePreset FromPlume(PlumeEntry plume) => new()
    {
        TemplateId = PlumeTemplates.NormalizeId(plume.TemplateId),
        Position = plume.Position,
        Rotation = plume.Rotation,
        Throttle = plume.Throttle,
        Nozzle = plume.Nozzle.Clone(),
        AbsorptionDensityScale = plume.AbsorptionDensityScale,
        RefractionIntensity = plume.RefractionIntensity,
    };
}
