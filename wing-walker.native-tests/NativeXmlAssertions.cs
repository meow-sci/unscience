using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml.Serialization;

internal static class NativeXmlAssertions
{
    private const double Tolerance = 1e-9;

    public static int PartCollectionCount(Assembly ksa)
    {
        Type library = RequiredType(ksa, "KSA.ModLibrary");
        object collection = library.GetField("AllParts", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
            .GetValue(null)!;
        return ((ICollection)collection.GetType().GetMethod("GetList")!.Invoke(collection, null)!).Count;
    }

    public static void Verify(Assembly ksa, AssemblyLoadContext context, object tagTable,
        IReadOnlyList<(string Tag, string Id, object Value)> decoded, int partCollectionCountBefore)
    {
        CheckPatchSignatures(ksa, context);
        CheckPartAndSubPartData(ksa, context, decoded);
        CheckStrictDeserialization(ksa, tagTable);
        CheckInstanceSaveSerialization(ksa);
        Require(PartCollectionCount(ksa) == partCollectionCountBefore,
            "parsing and deserializing the mod XML must not register or patch global Core rows");
        Console.WriteLine("PASS: native part schema, patch targets, connector math, and instance crew-save round trip");
    }

    private static void CheckPatchSignatures(Assembly ksa, AssemblyLoadContext context)
    {
        Assembly numericsAssembly = context.LoadFromAssemblyName(new AssemblyName("Brutal.Numerics"));
        Type viewport = RequiredType(ksa, "KSA.IViewport");
        Type seat = RequiredType(ksa, "KSA.IVASeat");
        Type renderable = RequiredType(ksa, "KSA.KittenRenderable");
        Type renderer = RequiredType(ksa, "KSA.SuperMeshRenderSystem");
        Type eva = RequiredType(ksa, "KSA.KittenEva");
        Type vehicle = RequiredType(ksa, "KSA.Vehicle");
        Type door = RequiredType(ksa, "KSA.EVADoor");
        Type crewSeatResult = RequiredType(ksa, "KSA.CrewSeatResult");
        Type keyHash = vehicle.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(method => method.Name == "AddCrew" && method.GetParameters().Length == 3)
            .GetParameters()[0].ParameterType;
        Type float3 = RequiredType(numericsAssembly, "Brutal.Numerics.float3");
        Type double3 = RequiredType(numericsAssembly, "Brutal.Numerics.double3");
        Type double4x4 = RequiredType(numericsAssembly, "Brutal.Numerics.double4x4");

        RequireMethod(typeof(void), renderer, "ClearBuckets", viewport);
        MethodInfo updateSeat = RequireMethod(typeof(void), seat, "UpdateSeatedRenderable",
            viewport, typeof(int), typeof(double), double4x4.MakeByRefType());
        Require(updateSeat.GetParameters()[3].IsIn, "IVASeat.UpdateSeatedRenderable keeps its readonly matrix parameter");
        RequireMethod(typeof(void), renderable, "UpdateSeatedAnimationState", typeof(double));
        MethodInfo origin = RequireMethod(double3, seat, "GetSeatedModelPointAsmb", float3.MakeByRefType(), typeof(double));
        Require(origin.GetParameters()[0].IsIn, "IVASeat.GetSeatedModelPointAsmb keeps its readonly seat-offset parameter");
        RequireMethod(crewSeatResult, eva, "BoardVehicle", vehicle, door);
        RequireMethod(crewSeatResult, vehicle, "AddCrew", keyHash, typeof(string), typeof(string));
        RequireField(renderable, "_seatedIdleAnim", RequiredType(ksa, "KSA.AnimationAssetRef"));
        RequireField(renderable, "_groundIdleAnim", RequiredType(ksa, "KSA.AnimationAssetRef"));
    }

    private static void CheckPartAndSubPartData(Assembly ksa, AssemblyLoadContext context,
        IReadOnlyList<(string Tag, string Id, object Value)> decoded)
    {
        var parts = decoded.Where(item => item.Tag == "Part")
            .ToDictionary(item => (string)GetMember(item.Value, "Id")!, item => item.Value, StringComparer.Ordinal);
        Require(parts.Count == 2, $"expected 2 final Part rows, got {parts.Count}");
        object seated = parts["Unscience_WingWalker_Seated"];
        object standing = parts["Unscience_WingWalker_Standing"];

        object[] seatedSubParts = Items(GetMember(seated, "SubPartInstances")).ToArray();
        object[] standingSubParts = Items(GetMember(standing, "SubPartInstances")).ToArray();
        string[] expectedKeys = ["Base", "Chair", "EvaDoor", "Headrest", "Runners"];
        string[] seatedKeys = seatedSubParts.Select(item => (string)GetMember(item, "Key")!).Order(StringComparer.Ordinal).ToArray();
        string[] standingKeys = standingSubParts.Select(item => (string)GetMember(item, "Key")!).Order(StringComparer.Ordinal).ToArray();
        Require(seatedSubParts.Length == 5 && seatedKeys.SequenceEqual(expectedKeys), "seated part has the five authored keyed subparts");
        Require(standingSubParts.Length == 5 && standingKeys.SequenceEqual(expectedKeys), "native pre-pass flattened all five parent subparts into the standing part");
        Require(seatedSubParts.Select(item => (string)GetMember(item, "Ref")!).Order(StringComparer.Ordinal)
            .SequenceEqual(standingSubParts.Select(item => (string)GetMember(item, "Ref")!).Order(StringComparer.Ordinal)),
            "standing part inherits the same subpart references as the seated parent");

        object[] seatedSeats = ComponentsOf(seated, "KSA.IVASeat+IVASeatTemplate");
        object[] standingSeats = ComponentsOf(standing, "KSA.IVASeat+IVASeatTemplate");
        Require(seatedSeats.Length == 1 && standingSeats.Length == 1, "each final part has exactly one inherited CrewSeat");
        CheckSeat(seatedSeats[0], -0.44326);
        CheckSeat(standingSeats[0], -0.4650768);
        Require(Near(Number(GetMember(GetMember(standingSeats[0], "PositionAsmb")!, "Z")) -
            Number(GetMember(GetMember(seatedSeats[0], "PositionAsmb")!, "Z")), -0.0218168),
            "standing CrewSeat position retains the authored eye-height offset");

        object habitat = ComponentsOf(seated, "KSA.HabitatTemplate").Single();
        object habitableVolume = GetMember(habitat, "HabitableVolume")!;
        object storageVolume = GetMember(habitat, "StorageVolume")!;
        Require(double.IsFinite(Number(GetMember(habitableVolume, "M3"))) && Number(GetMember(habitableVolume, "M3")) > 0,
            "local Habitat has a finite positive habitable volume");
        Require(double.IsFinite(Number(GetMember(storageVolume, "M3"))) && Number(GetMember(storageVolume, "M3")) > 0,
            "local Habitat has a finite positive storage volume");
        CheckFoodReserve(ksa, Number(GetMember(storageVolume, "M3")));

        object[] tanks = ComponentsOf(seated, "KSA.Tank+TemplateData");
        Require(tanks.Length == 2, $"seated part has two native Tank templates, got {tanks.Length}");
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (object tank in tanks)
        {
            object shape = GetMember(tank, "Tank")!;
            roles.Add(GetMember(shape, "RoleAffinity")!.ToString()!);
            object radius = GetMember(shape, "OuterRadius")!;
            object wall = GetMember(shape, "WallThickness")!;
            Require(double.IsFinite(Number(GetMember(radius, "M"))) && Number(GetMember(radius, "M")) > 0,
                "spherical Tank has a finite positive outer radius");
            Require(double.IsFinite(Number(GetMember(wall, "M"))) && Number(GetMember(wall, "M")) > 0,
                "spherical Tank has a finite positive wall thickness");
        }
        Require(roles.SetEquals(["Air", "Water"]), "the two tank roles deserialize as native Air and Water enum values");

        object connector = Items(GetMember(seated, "Connectors"))
            .Single(candidate => HasEnumFlag(GetMember(candidate, "Flags")!, "ToSurface"));
        CheckSurfaceNormal(ksa, context, connector);

        var subparts = decoded.Where(item => item.Tag == "SubPart")
            .ToDictionary(item => item.Id, item => item.Value, StringComparer.Ordinal);
        foreach (string id in new[]
        {
            "Unscience_WingWalker_Chair", "Unscience_WingWalker_Headrest",
            "Unscience_WingWalker_Runners", "Unscience_WingWalker_Base"
        })
        {
            object subpart = subparts[id];
            object model = ComponentsOf(subpart, "KSA.PartModelModule+Template").Single();
            Require(!(bool)GetMember(model, "Internal")!, $"{id} PartModel is external");
            object modelMesh = GetMember(model, "Mesh")!;
            Require(!string.IsNullOrEmpty((string)GetMember(modelMesh, "Ref")!), $"{id} model mesh has a native Ref");
            object view = ComponentsOf(subpart, "KSA.MeshViewModule+Template").Single();
            string viewRef = (string)GetMember(GetMember(view, "MeshView")!, "Ref")!;
            Require(viewRef.EndsWith("_VM", StringComparison.Ordinal), $"{id} MeshView targets a _VM mesh: {viewRef}");
        }

        object evaDoor = subparts["Unscience_WingWalker_Door"];
        object doorTemplate = ComponentsOf(evaDoor, "KSA.EVADoor+Template").Single();
        Require((string)GetMember(doorTemplate, "SeatId")! == "CrewSeat", "EVA helper door binds the inherited CrewSeat");
    }

    private static void CheckSeat(object seat, double expectedZ)
    {
        Require((string)GetMember(seat, "Key")! == "CrewSeat", "crew seat uses the stable CrewSeat key");
        object position = GetMember(seat, "PositionAsmb")!;
        Require(double.IsFinite(Number(GetMember(position, "X"))) && double.IsFinite(Number(GetMember(position, "Y")))
            && double.IsFinite(Number(GetMember(position, "Z"))), "crew seat position coordinates are finite");
        Require(Near(Number(GetMember(position, "Z")), expectedZ), $"crew seat height matches authored eye point ({expectedZ})");
        object up = GetMember(seat, "UpAxisAsmb")!;
        Require(Near(Number(GetMember(up, "Z")), -1), "crew seat up axis points down the KSA model Z axis");
    }

    private static void CheckFoodReserve(Assembly ksa, double storageVolumeM3)
    {
        Type habitatType = RequiredType(ksa, "KSA.Habitat");
        object habitat = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(habitatType);
        SetMember(habitat, "StorageVolume", (float)storageVolumeM3);
        MethodInfo calculate = habitatType.GetMethod("ComputeFullLoad")!;
        object?[] arguments = [0, 0];
        calculate.Invoke(habitat, arguments);
        Require((int)arguments[0]! == 4 && (int)arguments[1]! == 1,
            "native Habitat.ComputeFullLoad fits four meals and one air canister in the local storage volume");
    }

    private static void CheckSurfaceNormal(Assembly ksa, AssemblyLoadContext context, object connector)
    {
        object transform = GetMember(connector, "Transform")!;
        transform.GetType().GetMethod("OnDataLoad")!.Invoke(transform, [null]);
        object rotation = GetMember(transform, "RotationValue")!;
        Assembly numericsAssembly = context.LoadFromAssemblyName(new AssemblyName("Brutal.Numerics"));
        Type double3 = RequiredType(numericsAssembly, "Brutal.Numerics.double3");
        Type quaternion = RequiredType(numericsAssembly, "Brutal.Numerics.doubleQuat");
        object unitX = GetStaticMember(double3, "UnitX")!;
        Type double3Ex = RequiredType(ksa, "KSA.Double3Ex");
        MethodInfo rotate = double3Ex.GetMethod("Transform", BindingFlags.Public | BindingFlags.Static,
            binder: null, [double3, quaternion], modifiers: null)!;
        object normal = rotate.Invoke(null, [unitX, rotation])!;
        Require(Near(Number(GetMember(normal, "X")), 0) && Near(Number(GetMember(normal, "Y")), 0)
            && Near(Number(GetMember(normal, "Z")), 1), "native KSA connector math rotates local +X surface normal to model +Z");
    }

    private static void CheckStrictDeserialization(Assembly ksa, object tagTable)
    {
        Type errorsType = RequiredType(ksa, "KSA.ContentErrors");
        Type stampsType = RequiredType(ksa, "KSA.ContentStamps");
        Type prePassType = RequiredType(ksa, "KSA.ContentPrePass");
        Type deserializerType = RequiredType(ksa, "KSA.ContentDeserializer");
        Type bindingType = RequiredType(ksa, "KSA.ContentTagBinding");
        MethodInfo stamp = stampsType.GetMethod("Stamp", BindingFlags.Public | BindingFlags.Static)!;
        MethodInfo resolve = prePassType.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static,
            binder: null, [typeof(List<System.Xml.Linq.XElement>), errorsType], modifiers: null)!;
        MethodInfo deserialize = deserializerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "Deserialize" && method.GetParameters().Length == 5);
        IEnumerable bindings = (IEnumerable)tagTable.GetType().GetProperty("Bindings")!.GetValue(tagTable)!;
        var bindingByTag = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (object binding in bindings)
            bindingByTag[(string)bindingType.GetProperty("Tag")!.GetValue(binding)!] = binding;

        var root = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(FindRepoRoot(), "wing-walker", "WingWalker.xml"));
        System.Xml.Linq.XElement part = root.Root!.Elements("Part").Single(element => (string?)element.Attribute("Id") == "Unscience_WingWalker_Seated");
        System.Xml.Linq.XElement habitat = part.Element("Components")!.Element("Habitat")!;
        habitat.SetAttributeValue("Unexpected", "invalid");
        var rows = new List<System.Xml.Linq.XElement> { part };
        stamp.Invoke(null, [part, "native-negative", 0, "invalid.xml", 0, 0]);
        object errors = Activator.CreateInstance(errorsType)!;
        var flats = (IEnumerable)resolve.Invoke(null, [rows, errors])!;
        object flat = flats.Cast<object>().Single();
        object? value = deserialize.Invoke(null, [flat, bindingByTag["Part"], tagTable, null, errors]);
        int errorCount = (int)errorsType.GetProperty("Count")!.GetValue(errors)!;
        Require(value == null && errorCount > 0, "native deserializer rejects an unknown schema attribute");
    }

    private static void CheckInstanceSaveSerialization(Assembly ksa)
    {
        Type partInstanceType = RequiredType(ksa, "KSA.PartInstance");
        Type saveType = RequiredType(ksa, "KSA.IVASeat+SaveData");
        Type overridesType = RequiredType(ksa, "KSA.XmlHelper");
        object overrides = overridesType.GetMethod("BuildAttributeOverrides", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, null)!;
        object root = Activator.CreateInstance(partInstanceType)!;
        SetMember(root, "InstanceOf", "NativeRoundTripRoot");
        object seated = NewSeatInstance(partInstanceType, saveType, "Unscience_WingWalker_Seated", 1, "Ada Lovelace");
        object standing = NewSeatInstance(partInstanceType, saveType, "Unscience_WingWalker_Standing", 2, "Mica Orbit");
        var children = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(partInstanceType))!;
        SetMember(root, "Children", children);
        children.Add(seated);
        children.Add(standing);

        var serializer = new XmlSerializer(partInstanceType, (XmlAttributeOverrides)overrides);
        using var stream = new System.IO.MemoryStream();
        serializer.Serialize(stream, root);
        stream.Position = 0;
        object roundTrip = serializer.Deserialize(stream)!;
        object[] crewParts = Items(GetMember(roundTrip, "Children")).ToArray();
        Require(crewParts.Length == 2, "native PartInstance serializer preserves both per-instance parts");
        var crewByPartId = crewParts.ToDictionary(part => (string)GetMember(part, "InstanceOf")!, part =>
            (string)GetMember(Items(GetMember(part, "Components")).Single(), "AssignedKittenName")!, StringComparer.Ordinal);
        Require(crewByPartId.Count == 2
            && crewByPartId["Unscience_WingWalker_Seated"] == "Ada Lovelace"
            && crewByPartId["Unscience_WingWalker_Standing"] == "Mica Orbit",
            "native IVASeatData serialization round-trips distinct crew names for separate instances");
    }

    private static object NewSeatInstance(Type partInstanceType, Type saveType, string partId, uint instanceId, string crewName)
    {
        object part = Activator.CreateInstance(partInstanceType)!;
        SetMember(part, "InstanceOf", partId);
        object saveData = Activator.CreateInstance(saveType)!;
        SetMember(saveData, "InstanceOf", "CrewSeat");
        SetMember(saveData, "LocalInstanceId", instanceId);
        SetMember(saveData, "AssignedKittenName", crewName);
        ((IList)GetMember(part, "Components")!).Add(saveData);
        return part;
    }

    private static object[] ComponentsOf(object template, string fullTypeName) =>
        Items(GetMember(template, "Components")).Where(item => item.GetType().FullName == fullTypeName).ToArray();

    private static bool HasEnumFlag(object value, string flag)
    {
        long mask = Convert.ToInt64(Enum.Parse(value.GetType(), flag));
        return (Convert.ToInt64(value) & mask) != 0;
    }

    private static MethodInfo RequireMethod(Type returnType, Type declaringType, string name, params Type[] parameters)
    {
        MethodInfo? method = declaringType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .SingleOrDefault(candidate => candidate.Name == name && candidate.ReturnType == returnType
                && candidate.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters));
        Require(method != null, $"native Harmony target signature exists: {returnType.Name} {declaringType.FullName}.{name}({string.Join(", ", parameters.Select(type => type.Name))})");
        return method!;
    }

    private static void RequireField(Type declaringType, string name, Type expectedType)
    {
        FieldInfo? field = declaringType.GetField(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Require(field?.FieldType == expectedType, $"native injected field exists with type: {declaringType.FullName}.{name}");
    }

    private static IEnumerable<object> Items(object? value) =>
        ((IEnumerable)(value ?? throw new InvalidOperationException("Expected a native collection, got null.")))
        .Cast<object>();

    private static object? GetStaticMember(Type type, string name) =>
        (object?)type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
        ?? type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

    private static object? GetMember(object instance, string name)
    {
        Type type = instance.GetType();
        for (Type? current = type; current != null; current = current.BaseType)
        {
            PropertyInfo? property = current.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property != null)
                return property.GetValue(instance);
            FieldInfo? field = current.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null)
                return field.GetValue(instance);
        }
        throw new MissingMemberException(type.FullName, name);
    }

    private static void SetMember(object instance, string name, object value)
    {
        Type type = instance.GetType();
        for (Type? current = type; current != null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                field.SetValue(instance, value);
                return;
            }
            PropertyInfo? property = current.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property?.SetMethod != null)
            {
                property.SetValue(instance, value);
                return;
            }
        }
        throw new MissingMemberException(type.FullName, name);
    }

    private static double Number(object? value) => Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
    private static bool Near(double left, double right) => Math.Abs(left - right) <= Tolerance;

    private static Type RequiredType(Assembly assembly, string name) => assembly.GetType(name, throwOnError: true)!;
    private static Type RequiredType(AssemblyLoadContext context, string assemblyName, string typeName) =>
        RequiredType(context.LoadFromAssemblyName(new AssemblyName(assemblyName)), typeName);

    private static string FindRepoRoot()
    {
        for (System.IO.DirectoryInfo? current = new(AppContext.BaseDirectory); current != null; current = current.Parent)
            if (System.IO.File.Exists(System.IO.Path.Combine(current.FullName, "wing-walker", "WingWalker.xml")) &&
                System.IO.File.Exists(System.IO.Path.Combine(current.FullName, "Directory.Build.props")))
                return current.FullName;
        throw new System.IO.DirectoryNotFoundException("Could not locate the unscience repository root.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
