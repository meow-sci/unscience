using Brutal.Numerics;
using KSA;
using System;

namespace MeowSci.PyroLib;

/// <summary>Explicit values exposed by the shared exhaust template editor; no live game references.</summary>
public sealed class SavedPlumeTemplate
{
    public void Validate()
    {
        if (QualitySampleCount is < 1 or > 64 || QualitySelfShadowSampleCount is < 0 or > 10)
            throw new InvalidOperationException("Invalid saved plume sampling quality.");
    }
    public double AbsorptionDensity { get; set; }
    public double AbsorptionScatteringBrightness { get; set; }
    public double AbsorptionScatteringPhaseEccentricity { get; set; }
    public double AbsorptionRefractionIntensity { get; set; }
    public double EmissionBrightness { get; set; }
    public double EmissionFlowMachDiamondsLeadIn { get; set; }
    public double EmissionFlowMachDiamondsLeadOut { get; set; }
    public double EmissionFlowMachDiamondsMiddleRadius { get; set; }
    public double NoiseDensityNoiseSize { get; set; }
    public double NoiseDensityNoiseIntensity { get; set; }
    public double NoiseShapeNoiseSize { get; set; }
    public double NoiseShapeNoiseIntensity { get; set; }
    public double NoiseRadialShapeNoiseSize { get; set; }
    public double NoiseRadialShapeNoiseIntensity { get; set; }
    public double NoiseRadialShapeNoiseSpeed { get; set; }
    public double NoiseRadialShapeNoiseBarrelShockIntensity { get; set; }
    public double LengthWeightsRadiusWeight { get; set; }
    public double LengthWeightsNozzlePressureWeight { get; set; }
    public double LengthWeightsJetExpansionWeight { get; set; }
    public double LengthWeightsExitMachNumberWeight { get; set; }
    public double QualitySampleCount { get; set; }
    public double QualitySelfShadowSampleCount { get; set; }
    public bool CleanBurn { get; set; }
    public bool VesselShadows { get; set; }
    public float3 Color0 { get; set; }
    public float3 Color1 { get; set; }
    public float3 Color2 { get; set; }
    public float3 Color3 { get; set; }

    public static SavedPlumeTemplate Capture(VolumetricExhaustTemplate source) => new()
    {
        AbsorptionDensity = source.Absorption.Density.Value,
        AbsorptionScatteringBrightness = source.Absorption.ScatteringBrightness.Value,
        AbsorptionScatteringPhaseEccentricity = source.Absorption.ScatteringPhaseEccentricity.Value,
        AbsorptionRefractionIntensity = source.Absorption.RefractionIntensity.Value,
        EmissionBrightness = source.Emission.Brightness.Value,
        EmissionFlowMachDiamondsLeadIn = source.Emission.Flow.MachDiamonds.LeadIn.Value,
        EmissionFlowMachDiamondsLeadOut = source.Emission.Flow.MachDiamonds.LeadOut.Value,
        EmissionFlowMachDiamondsMiddleRadius = source.Emission.Flow.MachDiamonds.MiddleRadius.Value,
        NoiseDensityNoiseSize = source.Noise.DensityNoise.Size.Value,
        NoiseDensityNoiseIntensity = source.Noise.DensityNoise.Intensity.Value,
        NoiseShapeNoiseSize = source.Noise.ShapeNoise.Size.Value,
        NoiseShapeNoiseIntensity = source.Noise.ShapeNoise.Intensity.Value,
        NoiseRadialShapeNoiseSize = source.Noise.RadialShapeNoise.Size.Value,
        NoiseRadialShapeNoiseIntensity = source.Noise.RadialShapeNoise.Intensity.Value,
        NoiseRadialShapeNoiseSpeed = source.Noise.RadialShapeNoise.Speed.Value,
        NoiseRadialShapeNoiseBarrelShockIntensity = source.Noise.RadialShapeNoise.BarrelShockIntensity.Value,
        LengthWeightsRadiusWeight = source.LengthWeights.RadiusWeight.Value,
        LengthWeightsNozzlePressureWeight = source.LengthWeights.NozzlePressureWeight.Value,
        LengthWeightsJetExpansionWeight = source.LengthWeights.JetExpansionWeight.Value,
        LengthWeightsExitMachNumberWeight = source.LengthWeights.ExitMachNumberWeight.Value,
        QualitySampleCount = source.Quality.SampleCount.Value,
        QualitySelfShadowSampleCount = source.Quality.SelfShadowSampleCount.Value,
        CleanBurn = source.Absorption.FakeCleanBurnInAtmosphere.Value,
        VesselShadows = source.Quality.VolumetricVesselShadows,
        Color0 = source.Emission.ColorGradient.Color0.Value.AsFloat3,
        Color1 = source.Emission.ColorGradient.Color1.Value.AsFloat3,
        Color2 = source.Emission.ColorGradient.Color2.Value.AsFloat3,
        Color3 = source.Emission.ColorGradient.Color3.Value.AsFloat3,
    };

    public void Apply(VolumetricExhaustTemplate target)
    {
        target.Absorption.Density.Value = AbsorptionDensity;
        target.Absorption.ScatteringBrightness.Value = AbsorptionScatteringBrightness;
        target.Absorption.ScatteringPhaseEccentricity.Value = AbsorptionScatteringPhaseEccentricity;
        target.Absorption.RefractionIntensity.Value = AbsorptionRefractionIntensity;
        target.Emission.Brightness.Value = EmissionBrightness;
        target.Emission.Flow.MachDiamonds.LeadIn.Value = EmissionFlowMachDiamondsLeadIn;
        target.Emission.Flow.MachDiamonds.LeadOut.Value = EmissionFlowMachDiamondsLeadOut;
        target.Emission.Flow.MachDiamonds.MiddleRadius.Value = EmissionFlowMachDiamondsMiddleRadius;
        target.Noise.DensityNoise.Size.Value = NoiseDensityNoiseSize;
        target.Noise.DensityNoise.Intensity.Value = NoiseDensityNoiseIntensity;
        target.Noise.ShapeNoise.Size.Value = NoiseShapeNoiseSize;
        target.Noise.ShapeNoise.Intensity.Value = NoiseShapeNoiseIntensity;
        target.Noise.RadialShapeNoise.Size.Value = NoiseRadialShapeNoiseSize;
        target.Noise.RadialShapeNoise.Intensity.Value = NoiseRadialShapeNoiseIntensity;
        target.Noise.RadialShapeNoise.Speed.Value = NoiseRadialShapeNoiseSpeed;
        target.Noise.RadialShapeNoise.BarrelShockIntensity.Value = NoiseRadialShapeNoiseBarrelShockIntensity;
        target.LengthWeights.RadiusWeight.Value = LengthWeightsRadiusWeight;
        target.LengthWeights.NozzlePressureWeight.Value = LengthWeightsNozzlePressureWeight;
        target.LengthWeights.JetExpansionWeight.Value = LengthWeightsJetExpansionWeight;
        target.LengthWeights.ExitMachNumberWeight.Value = LengthWeightsExitMachNumberWeight;
        target.Quality.SampleCount.Value = QualitySampleCount;
        target.Quality.SelfShadowSampleCount.Value = QualitySelfShadowSampleCount;
        target.Absorption.FakeCleanBurnInAtmosphere.Value = CleanBurn;
        target.Quality.VolumetricVesselShadows = VesselShadows;
        target.Emission.ColorGradient.Color0 = new ColorRgbReference(Color0);
        target.Emission.ColorGradient.Color0.OnDataLoad(Mod.Empty);
        target.Emission.ColorGradient.Color1 = new ColorRgbReference(Color1);
        target.Emission.ColorGradient.Color1.OnDataLoad(Mod.Empty);
        target.Emission.ColorGradient.Color2 = new ColorRgbReference(Color2);
        target.Emission.ColorGradient.Color2.OnDataLoad(Mod.Empty);
        target.Emission.ColorGradient.Color3 = new ColorRgbReference(Color3);
        target.Emission.ColorGradient.Color3.OnDataLoad(Mod.Empty);
    }
}
