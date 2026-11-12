using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using System.Xml.Serialization;

internal static class CoolerNativeChecks
{
    private const string Red = "Unscience_Cooler_Red";
    private const string Steel = "Unscience_Cooler_StainlessSteel";

    public static void Run(Assembly ksa, object tagTable, string repoRoot)
    {
        string path = Path.Combine(repoRoot, "beverage-cooler", "BeverageCooler.xml");
        List<XElement> source = XDocument.Load(path).Root!.Elements().ToList();
        object errors = Activator.CreateInstance(Type(ksa, "ContentErrors"))!;
        object flats = Flatten(ksa, source, errors);
        CheckVariants(ksa, flats, errors);
        Require(ErrorCount(errors) == 0, "cooler inheritance and native variant-family validation have no errors");
        var decoded = Deserialize(ksa, tagTable, flats, errors);
        Require(ErrorCount(errors) == 0, "cooler native strict XML deserialization has no errors");

        object red = decoded[Red];
        object steel = decoded[Steel];
        foreach (object part in new[] { red, steel })
        {
            Require((string)Get(part, "DisplayName")! == "Beverage Cooler", "cosmetic variants inherit one display name");
            Require(Items(Get(part, "EditorTagRefs")).Select(item => (string)Get(item, "Ref")!)
                .ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "Unscience Supplies", "Structural", "Life Support" }),
                "both variants inherit native life-support/structural and custom supplies editor filters");
            Require(Items(Get(part, "VariantRefs")).Select(item => (string)Get(item, "Ref")!)
                .SequenceEqual(new[] { Red, Steel }), "both variants inherit the same native head-first family");
            object[] subparts = Items(Get(part, "SubPartInstances")).ToArray();
            Require(subparts.Select(item => (string)Get(item, "Key")!).SequenceEqual(new[] { "Body", "Lid", "LidCollider" }),
                "both variants retain identical ordered animation/save placement keys");
            Require((string)Get(subparts[2], "Ref")! == "Unscience_Cooler_LidCollision",
                "both variants use the same native mechanical lid template");
            object transform = Get(subparts[1], "Transform")!;
            transform.GetType().GetMethod("OnDataLoad")!.Invoke(transform, [null]);
            object position = Get(transform, "PositionValue")!;
            Require(Near(Get(position, "X"), -0.19) && Near(Get(position, "Z"), -0.409), "lid uses the authored hinge origin");
            object habitat = Component(part, "HabitatTemplate");
            Require((string)Get(habitat, "Key")! == "Supplies" && (bool)Get(habitat, "PassesSurfaceAttachments")!,
                "locker permits a native habitat passage at its own surface attachments");
            Require(Near(Get(Get(habitat, "StorageVolume")!, "M3"), 0.03)
                && Near(Get(Get(habitat, "HabitableVolume")!, "M3"), 0), "locker has 30 L storage and no crew volume");
            object animation = Component(part, "KeyframeAnimationModule+Template");
            Require((string)Get(animation, "Key")! == "LidAnimation" && !(bool)Get(animation, "ShowDeployRetract")!,
                "head-owned LidAnimation exposes the native Actuate slider");
            Require((string)Get(Get(animation, "KeyframeAnimationTemplate")!, "Ref")! == "Unscience_Cooler_LidAnimation",
                "both variants reference the same native keyframe animation");
            object collider = Component(part, "ColliderModule+Template");
            Require(Items(Get(collider, "Colliders")).Count() == 5, "both variants inherit the five-box hollow body collider");
            object floor = Items(Get(collider, "Colliders")).Single(item => (string)Get(item, "Key")! == "Floor");
            Require(Near(Get(Get(floor, "LocationAsmb")!, "Z"), -0.056)
                && Near(Get(Get(floor, "LengthZ")!, "M"), 0.112), "body floor reaches the visible insulated interior bottom at Z=-0.112");
        }
        foreach (string id in new[] { "Unscience_Cooler_RedBody", "Unscience_Cooler_RedLid", "Unscience_Cooler_SteelBody", "Unscience_Cooler_SteelLid" })
            Require(Items(Get(decoded[id], "Components")).All(component => component.GetType().FullName
                is "KSA.PartModelModule+Template" or "KSA.MeshViewModule+Template"), "cosmetic subparts have no gameplay modules");

        object nativeHabitat = RuntimeHelpers.GetUninitializedObject(Type(ksa, "Habitat"));
        Set(nativeHabitat, "StorageVolume", 0.03f);
        object?[] capacity = [0, 0];
        nativeHabitat.GetType().GetMethod("ComputeFullLoad")!.Invoke(nativeHabitat, capacity);
        Require((int)capacity[0]! == 12 && (int)capacity[1]! == 2, "native 30 L capacity is 12 meals and 2 CO₂ canisters");
        CheckDriftRejection(ksa, path);
        CheckSaveRoundTrip(ksa);
        CheckImportedAnimation(ksa, repoRoot);
        Console.WriteLine("PASS: cooler native XML, cosmetic variant family, inherited mechanics, Actuate module, 30 L supplies, and native save-data round trip");
    }

    private static object Flatten(Assembly ksa, List<XElement> rows, object errors)
    {
        MethodInfo stamp = Type(ksa, "ContentStamps").GetMethod("Stamp", BindingFlags.Static | BindingFlags.Public)!;
        for (int index = 0; index < rows.Count; index++)
            stamp.Invoke(null, [rows[index], "unscience", 0, "BeverageCooler.xml", 0, index]);
        return Type(ksa, "ContentPrePass").GetMethod("Resolve", BindingFlags.Static | BindingFlags.Public,
            null, [typeof(List<XElement>), errors.GetType()], null)!.Invoke(null, [rows, errors])!;
    }

    private static void CheckVariants(Assembly ksa, object flats, object errors) =>
        Type(ksa, "VariantFamilyCheck").GetMethod("Run", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, [flats, errors]);

    private static Dictionary<string, object> Deserialize(Assembly ksa, object tags, object flats, object errors)
    {
        var bindings = Items(Get(tags, "Bindings")).ToDictionary(binding => (string)Get(binding, "Tag")!);
        MethodInfo deserialize = Type(ksa, "ContentDeserializer").GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Single(method => method.Name == "Deserialize" && method.GetParameters().Length == 5);
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (object flat in Items(flats))
        {
            string tag = (string)Get(flat, "Tag")!;
            string id = (string)Get(flat, "Id")!;
            object? value = deserialize.Invoke(null, [flat, bindings[tag], tags, null, errors]);
            Require(value != null, $"native deserializer accepts cooler <{tag} Id=\"{id}\">");
            result.Add(id, value!);
        }
        return result;
    }

    private static void CheckDriftRejection(Assembly ksa, string path)
    {
        List<XElement> rows = XDocument.Load(path).Root!.Elements().ToList();
        rows.Single(row => (string?)row.Attribute("Id") == Steel).SetAttributeValue("DisplayName", "Different gameplay metadata");
        object errors = Activator.CreateInstance(Type(ksa, "ContentErrors"))!;
        object flats = Flatten(ksa, rows, errors);
        Require(ErrorCount(errors) == 0, "negative variant fixture still has valid inheritance");
        CheckVariants(ksa, flats, errors);
        Require(ErrorCount(errors) == 1, "native variant validator rejects a differing member DisplayName");

        rows = XDocument.Load(path).Root!.Elements().ToList();
        XElement collision = new(rows.Single(row => (string?)row.Attribute("Id") == "Unscience_Cooler_LidCollision"));
        collision.SetAttributeValue("Id", "VariantOnlyCollision");
        rows.Add(collision);
        rows.Single(row => (string?)row.Attribute("Id") == Steel).Element("SubParts")!.Add(
            new XElement("SubPart", new XAttribute("Key", "LidCollider"), new XAttribute("Ref", "VariantOnlyCollision"),
                new XAttribute("Modify", "Patch")));
        errors = Activator.CreateInstance(Type(ksa, "ContentErrors"))!;
        flats = Flatten(ksa, rows, errors);
        Require(ErrorCount(errors) == 0, "negative collider fixture still has valid inheritance");
        CheckVariants(ksa, flats, errors);
        Require(ErrorCount(errors) == 1, "native variants reject a unique mechanical subpart even when its collider is identical");

        rows = XDocument.Load(path).Root!.Elements().ToList();
        rows.Single(row => (string?)row.Attribute("Id") == Steel).Add(
            new XElement("Components", new XAttribute("Modify", "Extend"),
                new XElement("Habitat", new XAttribute("Key", "Supplies"), new XAttribute("Modify", "Patch"),
                    new XElement("StorageVolume", new XAttribute("M3", "0.06")))));
        errors = Activator.CreateInstance(Type(ksa, "ContentErrors"))!;
        flats = Flatten(ksa, rows, errors);
        Require(ErrorCount(errors) == 0, "negative capacity fixture still has valid inherited Habitat schema");
        CheckVariants(ksa, flats, errors);
        Require(ErrorCount(errors) == 1, "native cosmetic variants reject a different Habitat storage capacity");
    }

    private static void CheckSaveRoundTrip(Assembly ksa)
    {
        Type instanceType = Type(ksa, "PartInstance");
        object root = Activator.CreateInstance(instanceType)!;
        Set(root, "InstanceOf", Red);
        AddStates(ksa, root, 8, 1, 0.6f, 1.2f);
        object child = Activator.CreateInstance(instanceType)!;
        Set(child, "InstanceOf", Steel);
        AddStates(ksa, child, 3, 0, 0.9f, 0f);
        var children = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(instanceType))!;
        children.Add(child);
        Set(root, "Children", children);
        var overrides = (XmlAttributeOverrides)Type(ksa, "XmlHelper").GetMethod("BuildAttributeOverrides")!.Invoke(null, null)!;
        var serializer = new XmlSerializer(instanceType, overrides);
        using var stream = new MemoryStream();
        serializer.Serialize(stream, root);
        stream.Position = 0;
        object restored = serializer.Deserialize(stream)!;
        CheckStates(restored, Red, 8, 1, 0.6f, 1.2f);
        CheckStates(Items(Get(restored, "Children")).Single(), Steel, 3, 0, 0.9f, 0f);
        MethodInfo derive = Type(ksa, "KeyframeAnimationModule").GetMethod("DeriveDeploymentState")!;
        Require(derive.Invoke(null, [1.2f, 0.6f])!.ToString() == "Deploying"
            && derive.Invoke(null, [0f, 0.9f])!.ToString() == "Retracting", "native saved phases distinguish opening and closing");
    }

    private static void AddStates(Assembly ksa, object instance, int meals, int canisters, float current, float goal)
    {
        object supplies = Activator.CreateInstance(Type(ksa, "Habitat+SaveData"))!;
        Set(supplies, "InstanceOf", "Supplies");
        Set(supplies, "LocalInstanceId", 1u);
        Set(supplies, "Meals", meals);
        Set(supplies, "Canisters", canisters);
        Set(supplies, "MealsOwed", 0.25);
        Set(supplies, "WaterOwed", 0.125);
        Set(supplies, "CanisterUsedKittenDays", 0.5);
        object lid = Activator.CreateInstance(Type(ksa, "KeyframeAnimationModule+SaveData"))!;
        Set(lid, "InstanceOf", "LidAnimation");
        Set(lid, "LocalInstanceId", 2u);
        Set(lid, "TimeCurrent", current);
        Set(lid, "TimeGoal", goal);
        ((IList)Get(instance, "Components")!).Add(supplies);
        ((IList)Get(instance, "Components")!).Add(lid);
    }

    private static void CheckStates(object instance, string id, int meals, int canisters, float current, float goal)
    {
        Require((string)Get(instance, "InstanceOf")! == id, "native PartInstance preserves the selected cosmetic template ID");
        object[] modules = Items(Get(instance, "Components")).ToArray();
        object supplies = modules.Single(item => item.GetType().FullName == "KSA.Habitat+SaveData");
        object lid = modules.Single(item => item.GetType().FullName == "KSA.KeyframeAnimationModule+SaveData");
        Require((string)Get(supplies, "InstanceOf")! == "Supplies" && (string)Get(lid, "InstanceOf")! == "LidAnimation",
            "native module save keys survive serialization");
        Require((int)Get(supplies, "Meals")! == meals && (int)Get(supplies, "Canisters")! == canisters
            && Near(Get(supplies, "MealsOwed"), 0.25) && Near(Get(supplies, "WaterOwed"), 0.125)
            && Near(Get(supplies, "CanisterUsedKittenDays"), 0.5), "native HabitatData preserves depleted stock and fractional debts");
        Require(Near(Get(lid, "TimeCurrent"), current) && Near(Get(lid, "TimeGoal"), goal), "native KeyframeAnimationData preserves partial actuation and target");
    }

    private static void CheckImportedAnimation(Assembly ksa, string repoRoot)
    {
        string animationPath = Path.Combine(repoRoot, "beverage-cooler", "Animations", "BeverageCooler_Lid.glb");
        Require(File.Exists(animationPath), "runtime cooler animation GLB is present");
        object mod = Activator.CreateInstance(Type(ksa, "Mod"))!;
        Set(mod, "DirectoryPath", Path.GetDirectoryName(animationPath)!);
        object template = Activator.CreateInstance(Type(ksa, "KeyframeAnimationData+Template"))!;
        Set(template, "Id", "CoolerNativeAnimationProbe");
        Set(template, "Mod", mod);
        Set(template, "LocalPath", Path.GetFileName(animationPath));
        template.GetType().GetMethod("DoLoad", BindingFlags.Instance | BindingFlags.NonPublic,
            null, System.Type.EmptyTypes, null)!.Invoke(template, null);
        object animation = template.GetType().GetMethod("GetInstance")!.Invoke(template, null)!;
        Require(Near(Get(animation, "Duration"), 1.2), "native GLB importer sees the 1.2-second animation");
        var parts = (IDictionary)Get(animation, "PartLookup")!;
        Require(parts.Keys.Cast<string>().ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "Lid", "LidCollider" }),
            "native importer binds both unanimated render and collider nodes below their animated hinge");
        MethodInfo evaluate = animation.GetType().GetMethod("EvaluateWorldMatrix")!;
        foreach (object part in parts.Values)
        {
            foreach ((double time, double expectedX, double expectedZ) in new[]
            {
                (0d, 0.19, -0.409), (0.6, 0.0542593, -0.7000969), (1.2, -0.2559863, -0.7832270)
            })
            {
                object matrix = evaluate.Invoke(animation, [part, time, false])!;
                double frontX = 0.38 * Convert.ToDouble(Get(matrix, "M11")) + Convert.ToDouble(Get(matrix, "M41"));
                double frontZ = 0.38 * Convert.ToDouble(Get(matrix, "M13")) + Convert.ToDouble(Get(matrix, "M43"));
                Require(Math.Abs(frontX - expectedX) < 2e-6 && Math.Abs(frontZ - expectedZ) < 2e-6,
                    "native GLB interpolation raises the front of both lid slots around the rear hinge");
                Require(Near(Get(matrix, "M41"), -0.19) && Near(Get(matrix, "M43"), -0.409),
                    "native animation leaves the hinge fixed while the lid turns");
            }
        }
        Console.WriteLine("PASS: native GLB loader binds lid render/collider nodes and samples closed, halfway and 100-degree-open transforms");
    }

    private static object Component(object part, string type) =>
        Items(Get(part, "Components")).Single(item => item.GetType().FullName == "KSA." + type);
    private static Type Type(Assembly ksa, string name) => ksa.GetType("KSA." + name, true)!;
    private static int ErrorCount(object errors) => (int)Get(errors, "Count")!;
    private static bool Near(object? value, double expected) => Math.Abs(Convert.ToDouble(value) - expected) < 1e-7;
    private static IEnumerable<object> Items(object? value) => ((IEnumerable)value!).Cast<object>();
    private static object? Get(object value, string name)
    {
        for (Type? type = value.GetType(); type != null; type = type.BaseType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            if (type.GetProperty(name, flags) is PropertyInfo property) return property.GetValue(value);
            if (type.GetField(name, flags) is FieldInfo field) return field.GetValue(value);
        }
        throw new MissingMemberException(value.GetType().FullName, name);
    }
    private static void Set(object value, string name, object memberValue)
    {
        for (Type? type = value.GetType(); type != null; type = type.BaseType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            if (type.GetField(name, flags) is FieldInfo field) { field.SetValue(value, memberValue); return; }
            if (type.GetProperty(name, flags) is PropertyInfo property) { property.SetValue(value, memberValue); return; }
        }
        throw new MissingMemberException(value.GetType().FullName, name);
    }
    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
    }
}
