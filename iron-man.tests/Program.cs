using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using MeowSci.IronManLib;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Reject(Action action, string message)
{
    bool rejected = false;
    try { action(); } catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException) { rejected = true; }
    Check(rejected, message);
}
static bool Near(double3 a, double3 b) => (a - b).LengthSquared() < 1e-18;
static Part.Connector TankEndpoint(string name)
{
    var tank = new Part(name, new PartTemplate { Id = "tank" });
    var connector = new Part.Connector(new Part.Connector.TemplateBase { Capabilities = ConnectorCapabilityFlags.BulkFluid }, tank);
    tank.Connectors.Add(connector);
    return connector;
}

var harmony = new Harmony("MeowSci.IronMan.Tests");
IronManConnectorPatches.Apply(harmony);
var shared = new PartTemplate();
var kitten = new Part("Hunter", shared);
var untouched = new Part("Banjo", shared);
IronManConnectors.EnsureDefaults(kitten);
IronManConnectors.EnsureDefaults(kitten);
Check(kitten.Connectors.Count == 2 && shared.Connectors.Count == 0 && untouched.Connectors.Count == 0,
    "Default anchors must be idempotent and isolated from shared templates and other kittens.");
var up = kitten.Connectors[0];
var down = kitten.Connectors[1];
Check(Near(double3.UnitX.Transform(up.Asmb2ParentAsmb), -double3.UnitZ), "Up normal must be -Z.");
Check(Near(double3.UnitX.Transform(down.Asmb2ParentAsmb), double3.UnitZ), "Down normal must be +Z.");
Check(up.Capabilities == ConnectorCapabilityFlags.BulkFluid, "Anchors must opt into bulk fuel flow.");
Part.Connection.Connect(up, TankEndpoint("Tank"));
Check(!IronManConnectors.Update(up, double3.Zero, double3.UnitX, .1) && !IronManConnectors.Remove(up),
    "Connected anchors must reject edits and removal.");
Check(!IronManConnectors.RemoveAll(kitten) && kitten.Connectors.Count == 2, "Remove all must be atomic if occupied.");
kitten.Scale = new double3(2);
Check(IronManConnectors.Update(down, new double3(.1, .2, -.3), double3.UnitY, .2), "Disconnected edit should work.");
Check(Near(down.Authored.Transform.PositionValue, down.PositionParentAsmb / 2), "Authored offset must survive future scale refresh.");
Reject(() => IronManConnectors.Update(down, new double3(double.NaN), double3.UnitZ, .1), "NaN must be rejected.");
Reject(() => IronManConnectors.Add(kitten, "bad", double3.Zero, double3.Zero, .1), "Zero direction must be rejected.");

uint next = 1;
PartInstance saved = kitten.GetReferenceWithChildren(ref next);
Check(saved.Id.StartsWith(IronManConnectorData.Marker, StringComparison.Ordinal), "The real serialization postfix must emit metadata.");
Check(saved.ConnectedIndices.SequenceEqual(new[] { 0 }), "Connected indexes must remain stock-shaped.");
var serializer = new XmlSerializer(typeof(PartInstance));
using var writer = new StringWriter();
serializer.Serialize(writer, saved);
using var reader = new StringReader(writer.ToString());
var loaded = (PartInstance)serializer.Deserialize(reader)!;
var copied = new Part(loaded.Id, shared, loaded);
Check(copied.Id == "Hunter", "Constructor prefix must prevent metadata leaking into runtime ids.");
Check(copied.Connectors.Count == 2 && IronManConnectors.GetOwned(copied).Count == 2,
    "Constructor postfix must restore anchors before stock connection indexing.");
foreach (int index in loaded.ConnectedIndices) Part.Connection.Connect(copied.Connectors[index], TankEndpoint("CopyTank"));
Check(copied.Connectors[0].Connection != null && Near(copied.Connectors[1].PositionParentAsmb, down.PositionParentAsmb),
    "XML/copy round-trip must preserve occupancy index and edited poses.");
Check(copied.Connectors[0] != up, "Copies own independent connector instances.");
Reject(() => new Part("bad", shared, new PartInstance { Id = "iron-man:v2:unknown" }), "Unknown version must fail before stock reconstruction.");
Reject(() => new Part("bad", shared, new PartInstance { Id = "iron-man:v1:garbage" }), "Malformed marker must fail closed.");
var changedTemplate = new PartTemplate();
changedTemplate.Connectors.Add(new Part.Connector.TemplateBase());
Reject(() => new Part("bad", changedTemplate, loaded), "Changed stock connector indexes must fail closed.");
var data = IronManConnectorData.Decode(saved.Id, shared)!;
data.Anchors[0].Position = new[] { double.PositiveInfinity, 0d, 0d };
Reject(() => data.Restore(new Part("bad", shared)), "Nonfinite restored coordinates must be rejected.");
Check(!untouched.GetReferenceWithChildren(ref next).Id.StartsWith(IronManConnectorData.Marker, StringComparison.Ordinal),
    "Unmodified kittens must never receive a persistence marker.");
up.Connection!.Disconnect();
Check(IronManConnectors.RemoveAll(kitten) && kitten.Connectors.Count == 0, "Disconnected removal must restore original connector list.");
var otherKitten = new Part("Polaris", shared);
IronManConnectors.EnsureDefaults(otherKitten);
Part.Connection.Connect(copied.Connectors[1], otherKitten.Connectors[0]);
Check(!IronManConnectorUnload.PrepareForUnload() && copied.Connectors.Count == 2 && copied.Connectors[1].Connection != null,
    "Unload must refuse a conversion that would drop bulk-fluid capability and preserve the original anchors.");
copied.Connectors[1].Connection!.Disconnect();
Part.Connection.FailNextSurfaceConnection = true;
Check(!IronManConnectorUnload.PrepareForUnload() && copied.Connectors.Count == 2 && copied.Connectors[0].Connection != null,
    "A refused replacement must roll back original links and retain serializable anchors.");
Check(IronManConnectorUnload.PrepareForUnload(), "Ordinary attachments must convert safely on unload.");
Check(copied.Connectors.Count == 0 && copied.Connections.Count == 1 && copied.Tree.Recomputes > 0 && JobSystems.VehicleSolver.Waits > 0,
    "Unload must keep attached equipment, remove owned anchors and refresh graphs after waiting for the solver.");
Check(copied.Connections[0].HasCapabilities(ConnectorCapability.BulkFluid), "Unload must retain bulk propellant flow through the remaining stock tank endpoint.");
IronManConnectorPatches.Remove(harmony);
Console.WriteLine("PASS: runtime anchor isolation, normals, scale-safe edits, occupied guards, real Harmony constructor/serializer bridge, XML/index round-trip, malformed saves and restoration.");
