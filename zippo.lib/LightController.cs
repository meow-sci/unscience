using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.ZippoLib;

/// <summary>Core light manipulation logic for zippo — stateless, reusable from outside the mod.</summary>
public static partial class LightController
{
    private static readonly BindingFlags All =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    public static readonly string[] ColorPresetNames = { "Marine", "HotPink", "RadioactiveGreen", "BabyPurple" };

    // float3 values from KSAColor.Xkcd decompiled sources
    public static float3 GetPresetColor(int idx) => idx switch
    {
        0 => new float3(0.01568628f, 0.1803922f, 0.37647059f), // Marine
        1 => new float3(1f, 0.00784314f, 0.55294118f),          // HotPink
        2 => new float3(0.172549f, 0.9803922f, 0.1215686f),     // RadioactiveGreen
        3 => new float3(0.7921569f, 0.6078432f, 0.9686275f),    // BabyPurple
        _ => new float3(1f, 1f, 1f)
    };

    /// <summary>Finds all KSA.LightModule+TemplateData entries in the part template's Components list.</summary>
    public static List<object> GetLightComponents(PartTemplate t)
    {
        var result = new List<object>();
        var comps = ReflectionHelpers.GetFieldValue(t, "Components") as IList;
        if (comps == null) return result;
        for (int i = 0; i < comps.Count; i++)
        {
            var c = comps[i];
            if (c?.GetType().FullName == "KSA.LightModule+TemplateData")
                { result.Add(c); ComponentOwners[c] = t; }
        }
        return result;
    }

    public static bool HasLights(PartTemplate t) => GetLightComponents(t).Count > 0;

    public static float ReadIntensity(PartTemplate t)
    {
        var lights = GetLightComponents(t);
        if (lights.Count == 0) return 1.0f;
        var intensityRef = ReflectionHelpers.GetFieldValue(lights[0], "Intensity");
        var val = ReflectionHelpers.GetFieldValue(intensityRef, "Value");
        return val is float f ? f : 1.0f;
    }

    public static float3 ReadColor(PartTemplate t)
    {
        var lights = GetLightComponents(t);
        if (lights.Count == 0) return new float3(1f, 1f, 1f);
        var colorRef = ReflectionHelpers.GetFieldValue(lights[0], "ColorRgb");
        if (colorRef == null) return new float3(1f, 1f, 1f);
        var r = ReflectionHelpers.GetFieldValue(colorRef, "R") is float rf ? rf : 1f;
        var g = ReflectionHelpers.GetFieldValue(colorRef, "G") is float gf ? gf : 1f;
        var b = ReflectionHelpers.GetFieldValue(colorRef, "B") is float bf ? bf : 1f;
        return new float3(r, g, b);
    }

    public static void WriteIntensity(List<object> lights, float intensity)
    {
        foreach (var light in lights)
        {
            RememberOriginal(light);
            var intensityRef = ReflectionHelpers.GetFieldValue(light, "Intensity");
            ReflectionHelpers.SetFieldValue(intensityRef, "Value", intensity);
        }
    }

    public static void WriteColor(List<object> lights, float3 color)
    {
        foreach (var light in lights)
        {
            RememberOriginal(light);
            var colorRef = ReflectionHelpers.GetFieldValue(light, "ColorRgb");
            if (colorRef == null) continue;
            ReflectionHelpers.SetFieldValue(colorRef, "R", color.X);
            ReflectionHelpers.SetFieldValue(colorRef, "G", color.Y);
            ReflectionHelpers.SetFieldValue(colorRef, "B", color.Z);
            // Clear any named/indexed color: ColorRgbReference.OnDataLoad re-derives R/G/B from
            // IndexedColor whenever it isn't Invalid, which would discard the RGB we just wrote.
            ReflectionHelpers.SetFieldValue(colorRef, "IndexedColor", IndexedColor.Invalid);
            // OnDataLoad recomputes Value = new float3(R, G, B)
            try { colorRef.GetType().GetMethod("OnDataLoad", All)?.Invoke(colorRef, new object?[] { null }); }
            catch (Exception ex) { Console.WriteLine($"zippo: SetColor OnDataLoad error: {ex.Message}"); }
        }
    }

    public static void ApplyIntensity(Part part, float intensity) =>
        WriteIntensity(GetLightComponents(part.Template), intensity);

    public static void ApplyColor(Part part, float3 color) =>
        WriteColor(GetLightComponents(part.Template), color);

    /// <summary>Returns all parts in the vehicle that have light components.</summary>
    public static List<Part> GetLightParts(Vehicle vehicle) =>
        PartHelpers.GetPartsWhere(vehicle, p => p.Template != null && HasLights(p.Template));

    public static void DumpPartsWithComponents(Part part, string indent = "")
    {
        var tmpl = part.Template;
        if (tmpl != null)
        {
            var compField = tmpl.GetType().GetField("Components", All);
            if (compField?.GetValue(tmpl) is IList comps && comps.Count > 0)
            {
                Console.WriteLine($"zippo: Part {part.Id} has Components[{comps.Count}]:");
                for (int i = 0; i < comps.Count; i++)
                {
                    var c = comps[i];
                    if (c == null) continue;
                    Console.WriteLine($"zippo:   [{i}] {c.GetType().FullName}");
                    var ctype = c.GetType();
                    while (ctype != null && ctype != typeof(object))
                    {
                        foreach (var f in ctype.GetFields(All | BindingFlags.DeclaredOnly))
                        {
                            object? fv = null;
                            try { fv = f.GetValue(c); } catch { fv = "<err>"; }
                            string fvs = fv is ICollection col ? $"[Count={col.Count}]" : fv?.ToString() ?? "null";
                            Console.WriteLine($"zippo:     .{f.Name} ({f.FieldType.Name}) = {fvs}");
                        }
                        ctype = ctype.BaseType;
                    }
                }
            }
        }
        foreach (var sub in part.SubParts)
            DumpPartsWithComponents(sub, indent + "  ");
    }
}
