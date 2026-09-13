using System;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Brutal.Numerics;
using MeowSci.BloominOnionLib;
using MeowSci.FreeFallinLib;
using MeowSci.GraffitiLib;
using MeowSci.HotPursuitLib;
using MeowSci.KsaAbstractions.Persistence;
using MeowSci.PebblesLib;
using MeowSci.RockyMcRockFaceLib;
using MeowSci.SphinxLib;


var definition = RingDefinition.CreateDefault();
definition.Name = "Saved ring";
definition.Lods[2].MeshId = "part-mesh";
definition.Stripes[1].Color = new float4(.3f, .6f, .8f, .4f);
var ring = RoundTrip(new BloominOnionSubmod.BodyRingSave { BodyId = "Earth", Definition = definition });
Check(ring.BodyId == "Earth" && ring.Definition.Name == "Saved ring", "ring identity");
Check(ring.Definition.Lods.Count == 5 && ring.Definition.Lods[2].MeshId == "part-mesh", "constructor-backed LOD fields");
Check(ring.Definition.Stripes[1].Color == definition.Stripes[1].Color, "constructor-backed stripe/vector fields");
ring.Definition.Lods[2].MeshId = "changed";
Check(definition.Lods[2].MeshId == "part-mesh", "detached ring lists");

var selection = new RingSelection { SizeM = 37, OverrideFieldSettings = true, DiffuseId = "texture" };
selection.LodMeshIds[3] = "mesh";
var clone = selection.Clone(); clone.LodMeshIds[3] = "new";
Check(selection.LodMeshIds[3] == "mesh" && clone.SizeM == 37, "detached applied ring selection");
var swap = RoundTrip(new RockyMcRockFaceSubmod.RingSwapSave { BodyId = "Saturn", LodMeshes = selection.LodMeshIds.ToArray(),
    SizeM = 37, OverrideField = true, Diffuse = "texture" });
Check(swap.LodMeshes[3] == "mesh" && swap.SizeM == 37 && swap.OverrideField, "explicit get-only LOD array persistence");

var canopy = RoundTrip(new FreeFallinSubmod.CanopySave { EffectiveAlbedo = new float4(.9f, .4f, .7f, .8f), Applied = new CanopyMaterialSettings {
    TextureMode = CanopyTextureMode.FullCanopy, TextureName = "sail.png", Tint = new float4(.1f, .2f, .3f, 1),
    Brightness = 2, FullCanopyRotationDegrees = 39, UseStockPbrMap = false, Roughness = .2f } });
Check(canopy.Applied?.TextureName == "sail.png" && canopy.Applied.Tint.Z == .3f && !canopy.Applied.UseStockPbrMap, "applied canopy recipe");
Check(canopy.EffectiveAlbedo == new float4(.9f, .4f, .7f, .8f), "effective Humble canopy recolor independent of authoring tint");
Check(RoundTrip(new FreeFallinSubmod.CanopySave { Applied = canopy.Applied }).EffectiveAlbedo == null, "legacy canopy record falls back to authored tint");
MaterialColorState.Record(1234567, new float4(.2f, .3f, .4f, 1));
Check(MaterialColorState.GetOrDefault(1234567, float4.One).Z == .4f, "authored material baseline registered");
MaterialColorState.Record(1234567, new float4(.8f, .7f, .6f, 1));
Check(MaterialColorState.GetOrDefault(1234567, float4.One).Z == .6f, "effective material recolor tracked");
MaterialColorState.Forget(1234567);
Check(MaterialColorState.GetOrDefault(1234567, float4.One) == float4.One, "released material cannot donate stale color to reused handle");

var target = new SavedPartReference { VehicleId = "craft", TreePath = [1, 2], SubPartPath = [3], TemplateId = "template" };
var camera = RoundTrip(new HotPursuitSubmod.MountedCameraSave { Target = target, MountPoint = new double3(1,2,3),
    SurfaceNormal = double3.UnitY, MountTangent = double3.UnitX, Translation = new double3(4,5,6), RotationDeg = new double3(7,8,9),
    FieldOfView = 71, Width = 1024, Height = 768, Visible = false, ViewportOpen = true });
Check(camera.Target.TreePath.SequenceEqual([1,2]) && camera.Target.SubPartPath.SequenceEqual([3]) && camera.MountPoint.Z == 3, "durable camera target and mount");
Check(camera.ViewportOpen && !camera.Visible && camera.Height == 768 && camera.RotationDeg.Y == 8, "camera visibility/lease/settings");
var decal = RoundTrip(new GraffitiSubmod.DecalSave { ImageName = "flag.png", Part = target, Kind = DecalAnchorKind.Parachute,
    CanopyIndex = 2, ClothNodeA = 1, ClothNodeB = 17, ClothNodeC = 18, ClothBarycentric = new double3(.2,.3,.5), ClothNormalSign = -1,
    Position = new double3(3,4,5), Normal = double3.UnitY, RotationDeg = 45, Width = 9, Height = 8, Depth = 7, Alpha = .6, Brightness = 2, Visible = true });
Check(decal.ClothBarycentric.Z == .5 && decal.ClothNormalSign == -1 && decal.ClothNodeC == 18 && decal.CanopyIndex == 2, "canopy barycentric anchor");
Check(decal.Part?.TemplateId == "template" && decal.Position.X == 3 && decal.Width == 9 && decal.Visible, "decal appearance and target");

var placed = RoundTrip(new SphinxSubmod.StaticSave { BodyId = "Mars", MeshId = "mesh", Png = "stone.png",
    PositionCcf = new double3(1,2,3), NormalCcf = double3.UnitZ, Scale = new float3(2,3,4), Rotation = new float3(10,20,30),
    Offset = new float3(4,5,6), UvScale = new Vector2(3,4), UvOffset = new Vector2(.2f,.4f), Collision = 2, Align = true, Visible = false });
Check(placed.Scale.Y == 3 && placed.UvOffset.Y == .4f && placed.Collision == 2 && placed.PositionCcf.Z == 3, "static transform/material/collision");
Check(placed.Align && !placed.Visible && placed.Png == "stone.png", "static flags and PNG");

string hash = new('A', 64);
var foreign = new GlbIdentity("C:\\Users\\someone\\.unscience\\glbs\\model.glb", hash, "");
var portable = GlbIdentity.Parse(foreign.MeshId(-1));
Check(portable.LibraryFileName == "model.glb" && portable.Hash == hash, "Windows identity relocates by copied library name with unchanged hash");

try { SaveJson.ToElement(new CanopyMaterialSettings { Brightness = float.NaN }); throw new Exception("Nonfinite save accepted."); }
catch (ArgumentException) { }
RejectOverflow<float>("1e100");
RejectOverflow<double>("1e999");
Check(!SaveJson.ToElement(camera).GetRawText().Contains("PartInstanceId", StringComparison.Ordinal), "no runtime instance identifier in camera DTO");
Check(!SaveJson.ToElement(placed).GetRawText().Contains("Model", StringComparison.Ordinal), "no native static resource graph");
Console.WriteLine("PASS: world save production DTOs, detached ring models, numeric fields, portable GLB identity, native graph exclusion and nonfinite rejection.");

static T RoundTrip<T>(T value) => SaveJson.FromElement<T>(JsonDocument.Parse(SaveJson.ToElement(value).GetRawText()).RootElement);
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void RejectOverflow<T>(string json)
{
    try { _ = SaveJson.FromElement<T>(JsonDocument.Parse(json).RootElement); }
    catch (JsonException) { return; }
    throw new Exception("Overflow literal was accepted for " + typeof(T).Name);
}
