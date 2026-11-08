using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;
using Tomlyn.Model;

namespace MeowSci.BloominOnionLib;

/// <summary>TOML (de)serialization of <see cref="RingDefinition"/> plus import from a game ring reference.</summary>
public static class RingDefinitionSerializer
{
    public static TomlTable ToToml(RingDefinition d)
    {
        var t = new TomlTable
        {
            ["name"] = d.Name,
            ["ecliptic_frame"] = d.UseEclipticFrame,
            ["inclination_deg"] = d.InclinationDeg,
            ["lan_deg"] = d.LongitudeOfAscendingNodeDeg,
            ["inner_radius_km"] = d.InnerRadiusKm,
            ["outer_radius_km"] = d.OuterRadiusKm,
            ["detail_scale"] = d.DetailScale,
            ["band_source"] = d.BandSource.ToString(),
            ["band_texture"] = d.BandTextureId,
            ["control_texture"] = d.ControlTextureId,
            ["base_color"] = Color(d.BaseColor),
            ["noise_amount"] = d.NoiseAmount,
            ["noise_scale"] = d.NoiseScale,
            ["noise_seed"] = (long)d.NoiseSeed,
            ["mesh_coverage_threshold"] = d.MeshCoverageThreshold,
            ["volume_min_thickness_km"] = d.VolumeMinThicknessKm,
            ["volume_max_thickness_km"] = d.VolumeMaxThicknessKm,
            ["volume_min_render_distance_km"] = d.VolumeMinRenderDistanceKm,
            ["volume_max_render_distance_km"] = d.VolumeMaxRenderDistanceKm,
            ["step_scale"] = (double)d.StepScale,
            ["step_min_size_km"] = d.StepMinSizeKm,
            ["step_max_size_km"] = d.StepMaxSizeKm,
            ["fade_to_meshes"] = d.FadeToMeshes,
            ["objects_name"] = d.ObjectsName,
            ["object_size_m"] = d.ObjectSizeM,
            ["object_thickness_km"] = d.ObjectThicknessKm,
            ["object_render_distance_km"] = d.ObjectRenderDistanceKm,
            ["object_density_per_km3"] = d.ObjectDensityPerKm3,
            ["diffuse"] = d.DiffuseId,
            ["normal"] = d.NormalId,
            ["pbr"] = d.PbrId,
        };

        var stripes = new TomlTableArray();
        foreach (var stripe in d.Stripes)
        {
            stripes.Add(new TomlTable
            {
                ["start"] = stripe.Start,
                ["end"] = stripe.End,
                ["feather"] = stripe.Feather,
                ["color"] = Color(stripe.Color),
            });
        }
        t["stripes"] = stripes;

        var lods = new TomlTableArray();
        foreach (var lod in d.Lods)
            lods.Add(new TomlTable { ["min_screen_size"] = (double)lod.MinScreenSizePixels, ["mesh"] = lod.MeshId });
        t["lods"] = lods;
        return t;
    }

    public static RingDefinition FromToml(TomlTable t)
    {
        var d = new RingDefinition
        {
            Name = Str(t, "name", "Unnamed"),
            UseEclipticFrame = Bool(t, "ecliptic_frame", false),
            InclinationDeg = Num(t, "inclination_deg", 0.0001),
            LongitudeOfAscendingNodeDeg = Num(t, "lan_deg", 0),
            InnerRadiusKm = Num(t, "inner_radius_km", 69000),
            OuterRadiusKm = Num(t, "outer_radius_km", 313900),
            DetailScale = Num(t, "detail_scale", 300),
            BandSource = Enum.TryParse(Str(t, "band_source", "Painted"), out RingBandSource source) ? source : RingBandSource.Painted,
            BandTextureId = Str(t, "band_texture", ""),
            ControlTextureId = Str(t, "control_texture", ""),
            BaseColor = ColorOf(t, "base_color", new float4(0.55f, 0.5f, 0.42f, 0f)),
            NoiseAmount = Num(t, "noise_amount", 0.35),
            NoiseScale = Num(t, "noise_scale", 1),
            NoiseSeed = (int)Num(t, "noise_seed", 1),
            MeshCoverageThreshold = Num(t, "mesh_coverage_threshold", 0.15),
            VolumeMinThicknessKm = Num(t, "volume_min_thickness_km", 1.25),
            VolumeMaxThicknessKm = Num(t, "volume_max_thickness_km", 4000),
            VolumeMinRenderDistanceKm = Num(t, "volume_min_render_distance_km", 1000),
            VolumeMaxRenderDistanceKm = Num(t, "volume_max_render_distance_km", 100000),
            StepScale = (float)Num(t, "step_scale", 0.006),
            StepMinSizeKm = Num(t, "step_min_size_km", 0.1),
            StepMaxSizeKm = Num(t, "step_max_size_km", 1000),
            FadeToMeshes = Bool(t, "fade_to_meshes", true),
            ObjectsName = Str(t, "objects_name", "BloominOnionRocks"),
            ObjectSizeM = Num(t, "object_size_m", 10),
            ObjectThicknessKm = Num(t, "object_thickness_km", 1),
            ObjectRenderDistanceKm = Num(t, "object_render_distance_km", 20),
            ObjectDensityPerKm3 = Num(t, "object_density_per_km3", 3125),
            DiffuseId = Str(t, "diffuse", ""),
            NormalId = Str(t, "normal", ""),
            PbrId = Str(t, "pbr", ""),
        };

        if (t.TryGetValue("stripes", out var stripesObj) && stripesObj is TomlTableArray stripes)
        {
            foreach (var s in stripes)
                d.Stripes.Add(new RingStripe(Num(s, "start", 0), Num(s, "end", 0),
                    ColorOf(s, "color", new float4(0.8f, 0.75f, 0.6f, 1f)), Num(s, "feather", 0.01)));
        }
        if (t.TryGetValue("lods", out var lodsObj) && lodsObj is TomlTableArray lods)
        {
            foreach (var l in lods)
                d.Lods.Add(new RingLodDefinition((float)Num(l, "min_screen_size", 2), Str(l, "mesh", "")));
        }
        if (d.Lods.Count == 0) d.ResetLodsToStock();
        return d;
    }

    /// <summary>A definition mirroring an existing game ring (e.g. Saturn's) so it can be tweaked.</summary>
    public static RingDefinition FromReference(string name, PlanetaryRingsReference rings)
    {
        var d = new RingDefinition
        {
            Name = name,
            UseEclipticFrame = rings.DefinitionFrame == OrbitDefinitionFrame.Ecliptic,
            InclinationDeg = rings.Inclination.ToDegrees(),
            LongitudeOfAscendingNodeDeg = rings.LongitudeOfAscendingNode.ToDegrees(),
            InnerRadiusKm = rings.InnerRadius.InKilometers(),
            OuterRadiusKm = rings.OuterRadius.InKilometers(),
            DetailScale = rings.DetailScale,
            BandSource = RingBandSource.Texture,
            BandTextureId = rings.Texture?.Value?.Id ?? "",
            ControlTextureId = rings.ControlTexture?.Value?.Id ?? "",
        };
        d.ResetStripesToSaturnLike();

        var volume = rings.Volume;
        if (volume != null)
        {
            d.VolumeMinThicknessKm = volume.MinThickness.InKilometers();
            d.VolumeMaxThicknessKm = volume.MaxThickness.InKilometers();
            d.VolumeMinRenderDistanceKm = volume.MinRenderDistance.InKilometers();
            d.VolumeMaxRenderDistanceKm = volume.MaxRenderDistance.InKilometers();
            d.StepScale = volume.Step.Scale;
            d.StepMinSizeKm = volume.Step.MinSize.InKilometers();
            d.StepMaxSizeKm = volume.Step.MaxSize.InKilometers();
            d.FadeToMeshes = volume.FadeToMeshes;
        }

        var objects = rings.RingObjects;
        if (objects != null)
        {
            d.ObjectsName = "BloominOnionRocks";
            d.ObjectSizeM = objects.Size.InMeters();
            d.ObjectThicknessKm = objects.Thickness.InKilometers();
            d.ObjectRenderDistanceKm = objects.RenderDistance.InKilometers();
            d.ObjectDensityPerKm3 = objects.Density;
            foreach (var lod in objects.Lods)
                d.Lods.Add(new RingLodDefinition(lod.MinScreenSizePixels, lod.MeshFileReference?.Value?.Mesh?.Id ?? ""));
            d.DiffuseId = objects.MaterialReference?.Value?.DiffuseReference?.Value?.Id ?? "";
            d.NormalId = objects.MaterialReference?.Value?.NormalReference?.Value?.Id ?? "";
            d.PbrId = objects.MaterialReference?.Value?.PBRMap?.Value?.Id ?? "";
        }
        if (d.Lods.Count == 0) d.ResetLodsToStock();
        return d;
    }

    private static TomlArray Color(float4 c) => new() { (double)c.X, (double)c.Y, (double)c.Z, (double)c.W };

    private static float4 ColorOf(TomlTable t, string key, float4 fallback)
    {
        if (!t.TryGetValue(key, out var v) || v is not TomlArray a || a.Count < 4) return fallback;
        return new float4(ToF(a[0]), ToF(a[1]), ToF(a[2]), ToF(a[3]));
    }

    private static float ToF(object? o) => (float)Convert.ToDouble(o ?? 0.0);
    private static string Str(TomlTable t, string key, string fallback) => t.TryGetValue(key, out var v) && v is string s ? s : fallback;
    private static bool Bool(TomlTable t, string key, bool fallback) => t.TryGetValue(key, out var v) && v is bool b ? b : fallback;

    private static double Num(TomlTable t, string key, double fallback)
    {
        if (!t.TryGetValue(key, out var v) || v == null) return fallback;
        try { return Convert.ToDouble(v); } catch { return fallback; }
    }
}
