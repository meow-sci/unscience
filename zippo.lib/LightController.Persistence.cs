using System;
using System.Collections.Generic;
using System.Linq;
using Brutal.Numerics;
using KSA;
using MeowSci.KsaAbstractions;

namespace MeowSci.ZippoLib;

public sealed class SavedLightTemplate
{
    public string TemplateId { get; set; } = "";
    public int ComponentIndex { get; set; }
    public float OriginalIntensity { get; set; }
    public float3 OriginalColor { get; set; }
    public float Intensity { get; set; }
    public float3 Color { get; set; }
}

public static partial class LightController
{
    private static readonly Dictionary<object, PartTemplate> ComponentOwners = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<object, (float Intensity, float3 Color)> OriginalSettings = new(ReferenceEqualityComparer.Instance);
    private static bool _restoringOriginals;

    private static (float Intensity, float3 Color) ReadComponent(object component)
    {
        var intensity = ReflectionHelpers.GetFieldValue(component, "Intensity");
        var color = ReflectionHelpers.GetFieldValue(component, "ColorRgb");
        return (ReflectionHelpers.GetFieldValue(intensity, "Value") is float f ? f : 1,
            new float3(ReflectionHelpers.GetFieldValue(color, "R") is float r ? r : 1,
                ReflectionHelpers.GetFieldValue(color, "G") is float g ? g : 1,
                ReflectionHelpers.GetFieldValue(color, "B") is float b ? b : 1));
    }

    private static void RememberOriginal(object component)
    {
        if (!_restoringOriginals && ComponentOwners.ContainsKey(component) && !OriginalSettings.ContainsKey(component))
            OriginalSettings.Add(component, ReadComponent(component));
    }

    internal static List<SavedLightTemplate> CaptureTemplateChanges() => OriginalSettings.Select(pair =>
    {
        var owner = ComponentOwners[pair.Key];
        var current = ReadComponent(pair.Key);
        return new SavedLightTemplate
        {
            TemplateId = owner.Id, ComponentIndex = GetLightComponents(owner).IndexOf(pair.Key),
            OriginalIntensity = pair.Value.Intensity, OriginalColor = pair.Value.Color,
            Intensity = current.Intensity, Color = current.Color
        };
    }).ToList();

    internal static void RestoreTemplateChange(SavedLightTemplate saved)
    {
        var template = ModLibrary.Get<PartTemplate>(saved.TemplateId)
            ?? throw new InvalidOperationException($"Light template {saved.TemplateId} is unavailable.");
        var components = GetLightComponents(template);
        if (saved.ComponentIndex < 0 || saved.ComponentIndex >= components.Count)
            throw new InvalidOperationException($"Light template {saved.TemplateId} changed component layout.");
        object component = components[saved.ComponentIndex];
        OriginalSettings[component] = (saved.OriginalIntensity, saved.OriginalColor);
        var one = new List<object> { component };
        WriteIntensity(one, saved.Intensity); WriteColor(one, saved.Color);
    }

    internal static void ResetTemplateChanges()
    {
        _restoringOriginals = true;
        try
        {
            foreach (var (component, original) in OriginalSettings)
            {
                var one = new List<object> { component };
                WriteIntensity(one, original.Intensity); WriteColor(one, original.Color);
            }
            OriginalSettings.Clear(); ComponentOwners.Clear();
        }
        finally { _restoringOriginals = false; }
    }
}
