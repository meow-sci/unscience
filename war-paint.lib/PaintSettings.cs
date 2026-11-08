using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;

namespace MeowSci.WarPaintLib;

/// <summary>A detached paint recipe, suitable for scene saves and the global preset library.</summary>
public sealed class PaintSettings
{
    public float4 Tint { get; set; } = float4.One;
    public List<PaintMaterialSettings> Materials { get; set; } = new();

    public PaintSettings Clone() => new()
    {
        Tint = Tint,
        Materials = Materials.Select(m => new PaintMaterialSettings
            { Source = m.Source, Name = m.Name, Slot = m.Slot, Color = m.Color }).ToList()
    };

    public void Validate()
    {
        if (!Finite(Tint) || Materials == null || Materials.Count > 10000
            || Materials.Any(m => m == null || string.IsNullOrWhiteSpace(m.Name)
                || m.Source == null || m.Slot < -1 || m.Name.Length > 4096 || m.Source.Length > 4096 || !Finite(m.Color))
            || Materials.Select(m => (m.Source, m.Slot >= 0 ? "slot:" + m.Slot : m.Name)).Distinct().Count() != Materials.Count)
            throw new InvalidOperationException("Invalid War Paint material recipe.");
    }

    internal static bool Finite(float4 value) => float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

public sealed class PaintMaterialSettings
{
    public string Source { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Additive role identity; legacy recipes without it use exact source/name.</summary>
    public int Slot { get; set; } = -1;
    public float4 Color { get; set; } = float4.One;
}
