using System;
using System.Collections.Generic;
using Brutal.Numerics;

namespace MeowSci.ZippoLib;

/// <summary>Authoring settings for independent repeating Disco channels.</summary>
public sealed class DiscoRecipe
{
    public bool Color = true;
    public bool Actuation;
    public bool Spread;
    public bool RandomColors;
    public float PhaseJitter = 1f;
    public List<float3> Palette = new() { new(1f, 0f, 0.2f), new(0f, 0.8f, 1f), new(0.6f, 0f, 1f) };
    public DiscoTiming ColorTiming = new();
    public DiscoTiming ActuationTiming = new();
    public DiscoTiming SpreadTiming = new();
    public float ActuationMin;
    public float ActuationMax = 1f;
    // Cone half-angles in degrees. Inner must not exceed outer at either endpoint.
    public float InnerMin = 5f;
    public float OuterMin = 15f;
    public float InnerMax = 25f;
    public float OuterMax = 45f;

    public DiscoRecipe Clone() => new()
    {
        Color = Color,
        Actuation = Actuation,
        Spread = Spread,
        RandomColors = RandomColors,
        PhaseJitter = PhaseJitter,
        Palette = new List<float3>(Palette),
        ColorTiming = ColorTiming.Clone(),
        ActuationTiming = ActuationTiming.Clone(),
        SpreadTiming = SpreadTiming.Clone(),
        ActuationMin = ActuationMin,
        ActuationMax = ActuationMax,
        InnerMin = InnerMin,
        OuterMin = OuterMin,
        InnerMax = InnerMax,
        OuterMax = OuterMax,
    };

    public void Validate()
    {
        if (Palette == null || Palette.Count is < 1 or > 32
            || Palette.Exists(color => !Unit(color.X) || !Unit(color.Y) || !Unit(color.Z))
            || !float.IsFinite(PhaseJitter) || PhaseJitter < 0f || PhaseJitter > 3600f
            || ColorTiming == null || ActuationTiming == null || SpreadTiming == null
            || !Unit(ActuationMin) || !Unit(ActuationMax) || ActuationMin > ActuationMax
            || !Angle(InnerMin) || !Angle(InnerMax) || !Angle(OuterMin) || !Angle(OuterMax)
            || InnerMin > OuterMin || InnerMax > OuterMax)
        {
            throw new InvalidOperationException(
                "Invalid Disco palette, phase jitter, actuation range, or spotlight cone angles.");
        }

        ColorTiming.Validate();
        ActuationTiming.Validate();
        SpreadTiming.Validate();
    }

    private static bool Unit(float value) => float.IsFinite(value) && value is >= 0f and <= 1f;
    private static bool Angle(float value) => float.IsFinite(value) && value is >= 0.1f and <= 89f;
}
