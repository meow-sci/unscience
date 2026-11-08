using System;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using HarmonyLib;
using KSA;

namespace MeowSci.IronManLib;

/// <summary>Extends native world/blueprint serializers without mutating KSA's shared XML overrides.</summary>
internal static class IronManAnchorSerializers
{
    private static readonly object Gate = new();
    private static XmlSerializer? _world;
    private static XmlSerializer? _vehicle;
    private static readonly MethodInfo WorldGetter = AccessTools.PropertyGetter(typeof(GameSaves), nameof(GameSaves.UniverseSerializer));
    private static readonly MethodInfo VehicleGetter = AccessTools.PropertyGetter(typeof(VehicleSaves), nameof(VehicleSaves.VehicleSerializer));

    internal static void Apply(Harmony harmony)
    {
        harmony.Patch(WorldGetter, postfix: new HarmonyMethod(typeof(IronManAnchorSerializers), nameof(WorldPostfix)));
        harmony.Patch(VehicleGetter, postfix: new HarmonyMethod(typeof(IronManAnchorSerializers), nameof(VehiclePostfix)));
    }

    internal static void Remove(Harmony harmony)
    {
        harmony.Unpatch(WorldGetter, AccessTools.Method(typeof(IronManAnchorSerializers), nameof(WorldPostfix)));
        harmony.Unpatch(VehicleGetter, AccessTools.Method(typeof(IronManAnchorSerializers), nameof(VehiclePostfix)));
        lock (Gate) { _world = null; _vehicle = null; }
    }

    private static void WorldPostfix(ref XmlSerializer __result)
    {
        lock (Gate) __result = _world ??= Create(typeof(UniverseData));
    }

    private static void VehiclePostfix(ref XmlSerializer __result)
    {
        lock (Gate) __result = _vehicle ??= Create(typeof(VehicleSaveData));
    }

    internal static XmlSerializer Create(Type root)
    {
        var overrides = XmlHelper.BuildAttributeOverrides();
        var components = overrides[typeof(PartInstance), nameof(PartInstance.Components)]
            ?? throw new InvalidOperationException("KSA's PartInstance.Components XML mapping is missing.");
        components.XmlElements.Add(new XmlElementAttribute("UnscienceIronManAnchors", typeof(IronManAnchorModule.SaveData)));
        var serializer = new XmlSerializer(root, overrides);
        // Legacy blueprints/worlds carried the same bounded v1 payload in the removed Id attribute.
        // XmlSerializer raises this after allocating the PartInstance, before native reconstruction.
        serializer.UnknownAttribute += (_, args) =>
        {
            if (args.ObjectBeingDeserialized is PartInstance part && args.Attr.Name == "Id" &&
                args.Attr.Value.StartsWith("iron-man:v", StringComparison.Ordinal))
                part.Components.Add(new IronManAnchorModule.SaveData { Payload = args.Attr.Value });
        };
        return serializer;
    }

    internal static IronManConnectorData? Decode(PartInstance? instance, PartTemplate template)
    {
        if (instance == null) return null;
        var records = instance.Components.OfType<IronManAnchorModule.SaveData>().ToArray();
        if (records.Length > 1) throw new InvalidOperationException("Duplicate Iron Man anchor records.");
        return records.Length == 0 ? null : IronManConnectorData.Decode(records[0].Payload, template)
            ?? throw new InvalidOperationException("Invalid Iron Man anchor payload.");
    }
}
