using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

internal static class CoolerRenderCacheChecks
{
    private sealed record ExpectedModel(string Part, string Mesh, string Material, string OldKey);

    public static void Run(Assembly ksa, IReadOnlyDictionary<string, object> decoded)
    {
        Type modelType = ksa.GetType("KSA.PartModel", true)!;
        Type templateType = ksa.GetType("KSA.PartModelModule+Template", true)!;
        MethodInfo getModel = modelType.GetMethod("Get", BindingFlags.Static | BindingFlags.Public)!;
        var instances = (IList)modelType.GetField("Instances")!.GetValue(null)!;
        var rayTraceInstances = (IList)modelType.GetField("InstancesRayTrace")!.GetValue(null)!;
        var firstByHash = (IDictionary)modelType.GetField("FirstInstanceByTemplateId", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        object[] originalInstances = instances.Cast<object>().ToArray();
        object[] originalRayTrace = rayTraceInstances.Cast<object>().ToArray();
        var cacheSnapshot = new List<DictionaryEntry>();
        foreach (DictionaryEntry entry in firstByHash) cacheSnapshot.Add(entry);
        DictionaryEntry[] originalCache = cacheSnapshot.ToArray();
        ExpectedModel[] expected = ExpectedModels();
        object[] templates = expected.Select(item => FindTemplate(decoded[item.Part], item.Mesh)).ToArray();
        try
        {
            // Isolate the real process-global native cache, then restore every original entry below.
            instances.Clear();
            rayTraceInstances.Clear();
            firstByHash.Clear();
            var brokenModels = new List<object>();
            for (int index = 0; index < expected.Length; index++)
            {
                object genericTemplate = Activator.CreateInstance(templateType)!;
                templateType.GetProperty("Key")!.SetValue(genericTemplate, expected[index].OldKey);
                templateType.GetField("Mesh")!.SetValue(genericTemplate, Get(templates[index], "Mesh"));
                templateType.GetField("Material")!.SetValue(genericTemplate, Get(templates[index], "Material"));
                brokenModels.Add(getModel.Invoke(null, [genericTemplate])!);
            }
            object Broken(string part, string channel) => brokenModels[Array.FindIndex(expected,
                item => item.Part == "Unscience_Cooler_" + part && item.OldKey == channel)];
            Require(brokenModels.Distinct(ReferenceEqualityComparer.Instance).Count() == 3,
                "generic Shell/Trim/Foil keys alias all twelve authored templates to three native models");
            Require(ReferenceEquals(Broken("RedBody", "Shell"), Broken("RedLid", "Shell"))
                && ReferenceEquals(Broken("RedBody", "Shell"), Broken("SteelBody", "Shell")),
                "native PartModel.Get reproduces red-body/lid and red/steel shell aliasing");
            CheckReferences(Get(Broken("RedLid", "Shell"), "Template")!, "Unscience_Cooler_BodyShell", "Unscience_Cooler_RedMaterial",
                "old red lid incorrectly receives the red body");
            CheckReferences(Get(Broken("SteelLid", "Shell"), "Template")!, "Unscience_Cooler_BodyShell", "Unscience_Cooler_RedMaterial",
                "old steel lid incorrectly receives the red body/material");
            Require(ReferenceEquals(Broken("RedBody", "Foil"), Broken("RedLid", "Foil")),
                "a generic Foil key also aliases distinct body and lid lining meshes");
            CheckReferences(Get(Broken("SteelLid", "Foil"), "Template")!, "Unscience_Cooler_BodyFoil", "CoreSurfacesB_Material",
                "generic foil incorrectly returns body lining for the lid while retaining the shared Core material");
            ClearProbeModels(instances, rayTraceInstances, firstByHash);

            Require(templates.Select(template => (string)Get(template, "Key")!).Distinct(StringComparer.Ordinal).Count() == 12,
                "all twelve decoded cooler render templates have globally distinct keys");
            var correctedModels = new List<object>();
            for (int index = 0; index < expected.Length; index++)
            {
                object model = getModel.Invoke(null, [templates[index]])!;
                correctedModels.Add(model);
                object returnedTemplate = Get(model, "Template")!;
                Require(ReferenceEquals(returnedTemplate, templates[index]),
                    $"native PartModel.Get retains the requested {expected[index].Part}/{expected[index].Mesh} template");
                CheckReferences(returnedTemplate, expected[index].Mesh, expected[index].Material,
                    $"native model uses the authored {expected[index].Part} geometry/material");
                Require(ReferenceEquals(model, getModel.Invoke(null, [templates[index]])),
                    "repeat lookup of the same template retains native cache identity");
            }
            Require(correctedModels.Distinct(ReferenceEqualityComparer.Instance).Count() == 12 && instances.Count == 12,
                "body/lid shell, trim and foil templates resolve to twelve distinct native PartModels");
        }
        finally
        {
            ClearProbeModels(instances, rayTraceInstances, firstByHash);
            foreach (object instance in originalInstances) instances.Add(instance);
            foreach (object instance in originalRayTrace) rayTraceInstances.Add(instance);
            foreach (DictionaryEntry entry in originalCache) firstByHash.Add(entry.Key, entry.Value);
        }
        Require(instances.Cast<object>().SequenceEqual(originalInstances, ReferenceEqualityComparer.Instance)
            && rayTraceInstances.Cast<object>().SequenceEqual(originalRayTrace, ReferenceEqualityComparer.Instance)
            && firstByHash.Count == originalCache.Length
            && originalCache.All(entry => ReferenceEquals(firstByHash[entry.Key], entry.Value)),
            "native model caches are restored without leaving test-created entries");
        Console.WriteLine("PASS: native PartModel.Get reproduces generic-key aliasing; all twelve render templates retain distinct mesh/material identities including Core foil; caches restored");
    }

    private static ExpectedModel[] ExpectedModels() =>
    [
        new("Unscience_Cooler_RedBody", "Unscience_Cooler_BodyShell", "Unscience_Cooler_RedMaterial", "Shell"),
        new("Unscience_Cooler_RedBody", "Unscience_Cooler_BodyTrim", "Unscience_Cooler_CommonMaterial", "Trim"),
        new("Unscience_Cooler_RedBody", "Unscience_Cooler_BodyFoil", "CoreSurfacesB_Material", "Foil"),
        new("Unscience_Cooler_RedLid", "Unscience_Cooler_LidShell", "Unscience_Cooler_RedMaterial", "Shell"),
        new("Unscience_Cooler_RedLid", "Unscience_Cooler_LidTrim", "Unscience_Cooler_CommonMaterial", "Trim"),
        new("Unscience_Cooler_RedLid", "Unscience_Cooler_LidFoil", "CoreSurfacesB_Material", "Foil"),
        new("Unscience_Cooler_SteelBody", "Unscience_Cooler_BodyShell", "Unscience_Cooler_SteelMaterial", "Shell"),
        new("Unscience_Cooler_SteelBody", "Unscience_Cooler_BodyTrim", "Unscience_Cooler_CommonMaterial", "Trim"),
        new("Unscience_Cooler_SteelBody", "Unscience_Cooler_BodyFoil", "CoreSurfacesB_Material", "Foil"),
        new("Unscience_Cooler_SteelLid", "Unscience_Cooler_LidShell", "Unscience_Cooler_SteelMaterial", "Shell"),
        new("Unscience_Cooler_SteelLid", "Unscience_Cooler_LidTrim", "Unscience_Cooler_CommonMaterial", "Trim"),
        new("Unscience_Cooler_SteelLid", "Unscience_Cooler_LidFoil", "CoreSurfacesB_Material", "Foil")
    ];

    private static object FindTemplate(object part, string mesh)
    {
        object[] templates = ((IEnumerable)Get(part, "Components")!).Cast<object>()
            .Where(component => component.GetType().FullName == "KSA.PartModelModule+Template").ToArray();
        Require(templates.Length == 3, "each cosmetic cooler subpart has exactly shell, trim and foil render templates");
        return templates.Single(template => (string)Get(Get(template, "Mesh")!, "Ref")! == mesh);
    }

    private static void CheckReferences(object template, string mesh, string material, string reason) =>
        Require((string)Get(Get(template, "Mesh")!, "Ref")! == mesh
            && (string)Get(Get(template, "Material")!, "Ref")! == material, reason);

    private static void ClearProbeModels(IList instances, IList rayTraceInstances, IDictionary firstByHash)
    {
        foreach (IDisposable model in instances) model.Dispose();
        instances.Clear();
        rayTraceInstances.Clear();
        firstByHash.Clear();
    }

    private static object? Get(object instance, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = instance.GetType();
        return type.GetProperty(name, flags)?.GetValue(instance) ?? type.GetField(name, flags)?.GetValue(instance)
            ?? throw new MissingMemberException(type.FullName, name);
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
    }
}
