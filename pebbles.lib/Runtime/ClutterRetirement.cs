using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepuPhysics.Collidables;
using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using KSA;
using RenderCore;
using RenderCore.Mesh;
using RenderCore.Pipelines;

namespace MeowSci.PebblesLib;

/// <summary>Best-effort, once-only retirement of reachable native fields after an interrupted build.
/// Never walks arbitrary game references. Native constructor-local allocations remain a game limitation.</summary>
internal sealed class ClutterRetirement
{
    private readonly HashSet<object> _seen = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _handles = [];
    public readonly List<Exception> Errors = [];
    private void Try(Action action) { try { action(); } catch (Exception e) { Errors.Add(e); } }
    public void Clean(object? value)
    {
        if (value == null) return;
        if (value.GetType().IsValueType && value.Equals(Activator.CreateInstance(value.GetType()))) return;
        if (!value.GetType().IsValueType && !_seen.Add(value)) return;
        if (value is BufferEx b) { if (b.IsNotNull() && _handles.Add(b.VkBuffer)) Try(b.Dispose); return; }
        if (value is MappedMemory memory) { if (!memory.Equals(default(MappedMemory)) && _handles.Add(memory)) Try(memory.Unmap); return; }
        if (value is DescriptorPoolEx or DescriptorSetLayoutEx)
        { if (_handles.Add(value)) Try(((IDisposable)value).Dispose); return; }
        if (value is BufferPartitionInfo partition)
        { if (!partition.Equals(default(BufferPartitionInfo)) && _handles.Add(partition)) Try(() => partition.FreePartitionOrBuffer()); return; }
        if (value is SimpleComputePipeline compute)
        { Try(() => compute.FreeDescriptorSet(Program.Instance.RenderGlobals.DescriptorPool)); Try(compute.Dispose); return; }
        if (value is SimpleGraphicsPipeline graphics) { Try(graphics.Dispose); return; }
        if (value is SimpleVkTexture texture) { Try(texture.Dispose); return; }
        if (value is IDictionary map) { foreach (var item in map.Values) Clean(item); return; }
        if (value is IEnumerable sequence) { foreach (var item in sequence) Clean(item); return; }
        if (value is not (GroundClutterPlacementData or ClutterEcotypeRenderData or ClutterEcotypePhysicalData or ClutterCubeCellGrid or ClutterViewResources or SimpleVkMeshAtlas)) return;
        if (value is ClutterEcotypePhysicalData physical) Try(() => RemoveShapes(physical));
        var fields = value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        // Persistent maps must be unmapped before their underlying buffers are released. KSA 5638 nests
        // per-viewport/per-frame maps (MappedMemory[][]), so match the element type at any depth.
        var mapped = fields.Where(f => FieldShapes.Holds(f.FieldType, typeof(MappedMemory))).ToArray();
        foreach (var field in mapped) Clean(field.GetValue(value));
        foreach (var field in fields)
        {
            if (mapped.Contains(field)) continue;
            if (value is ClutterEcotypePhysicalData && (field.Name == "MeshAtlas" || field.Name == "PlacementData")) continue;
            var item = field.GetValue(value);
            if (item is VkSampler sampler) { if (_handles.Add(sampler)) Try(() => Program.GetRenderer().Device.DestroySampler(sampler, null)); }
            else if (item is VkImageView imageView) { if (_handles.Add(imageView)) Try(() => Program.GetRenderer().Device.DestroyImageView(imageView, null)); }
            else Clean(item);
        }
        if (value is GroundClutterPlacementData placement && placement.AltitudeLutBindlessHandle != 0)
            Try(() => Program.Instance.BindlessTextures.FreeTexture(placement.AltitudeLutBindlessHandle));
    }
    private static void RemoveShapes(ClutterEcotypePhysicalData data)
    {
        using var unlock = ConstraintSim.UnlockShapes();
        foreach (var name in new[] { "_compoundShapes", "_primitiveShapes" })
        {
            var list = (List<TypedIndex>?)ClutterController.Field(typeof(ClutterEcotypePhysicalData), name).GetValue(data);
            if (list == null) continue;
            foreach (var shape in list)
                if (name == "_compoundShapes") unlock.Shapes.RecursivelyRemoveAndDispose(shape, unlock.BufferPool);
                else unlock.Shapes.RemoveAndDispose(shape, unlock.BufferPool);
            list.Clear();
        }
    }
}
