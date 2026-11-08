using System;
using System.Collections.Generic;
using System.Reflection;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using KSA;
using KSA.Rendering;
using RenderCore;

namespace MeowSci.PebblesLib;

internal sealed class ClutterResources : IDisposable
{
    public readonly ClutterGraph Graph = new();
    public readonly GroundClutterRenderer Owner;
    public GroundClutterPlacementData[] Placement = [];
    public ClutterEcotypeRenderData[] Render = [];
    public ClutterEcotypePhysicalData[] Physical = [];
    public BufferEx? MaterialBuffer;
    public Dictionary<KeyHash, uint> MaterialIndices { get; } = [];
    public float MaximumRadius;
    private bool _disposed;
    private bool _complete;
    internal readonly List<object> Constructed = [];

    public ClutterResources(GroundClutterRenderer owner) => Owner = owner;

    // Must run with vehicle AND cloth idle, graphics device idle, on the game thread.
    public void Build(Celestial body, GroundClutterReference baseline, PebblesRecipe recipe, ClutterAssets assets)
    {
        Graph.Build(body, baseline, recipe, assets);
        var renderer = Program.GetRenderer();
        var planet = Program.GetPlanetRenderer();
        var pass = (IRenderPassInfo)ClutterController.Field(typeof(GroundClutterRenderer), "_renderPassInfo").GetValue(Owner)!;
        var count = Graph.Reference.Ecotypes.Count;
        Placement = new GroundClutterPlacementData[count]; Render = new ClutterEcotypeRenderData[count]; Physical = new ClutterEcotypePhysicalData[count];
        using var pool = renderer.Allocator.CreateStagingPool(renderer.GraphicsAndCompute, 1);
        var command = pool.NextCommandBuffer(); command.Begin();
        try
        {
            var length = Math.Max(Graph.Materials.Count, 1);
            MaterialBuffer = renderer.Allocator.CreateBuffer(new BufferEx.CreateInfo { Name = "Pebbles materials", BufferSize = ByteSize.Of<GroundClutterRenderer.GroundClutterGpuMaterial>(length),
                BufferUsage = VkBufferUsageFlags.StorageBufferBit | VkBufferUsageFlags.TransferDstBit, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit });
            var staging = pool.AddStagingBuffer(ByteSize.Of<GroundClutterRenderer.GroundClutterGpuMaterial>(length));
            using (var memory = staging.Map())
            {
                var data = memory.AsSpan<GroundClutterRenderer.GroundClutterGpuMaterial>(); data.Clear();
                for (var i = 0; i < Graph.Materials.Count; i++)
                {
                    var material = Graph.Materials[i]; MaterialIndices.Add(material.Hash, (uint)i);
                    data[i] = material.ToGpuMaterial();
                    if (Graph.SourceColorMaterials.Contains(material.Hash))
                    {
                        data[i].Flags |= 0x80000000u;
                        if (material.DiffuseReference!.Value!.Texture.Format.ToString().Contains("Srgb", StringComparison.OrdinalIgnoreCase)) data[i].Flags |= 0x40000000u;
                    }
                }
            }
            command.CopyBuffer(staging.VkBuffer, MaterialBuffer.Value.VkBuffer, new VkBufferCopy { Size = ByteSize.Of<GroundClutterRenderer.GroundClutterGpuMaterial>(length) });
            var barrier = Utils.CreateBarrier(MaterialBuffer.Value.VkBuffer, VkAccessFlags.TransferWriteBit, VkAccessFlags.ShaderReadBit, VK.WHOLE_SIZE, ByteSize.Zero);
            command.PipelineBarrier(VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.FragmentShaderBit, VkDependencyFlags.None, null, new ReadOnlySpan<VkBufferMemoryBarrier>(in barrier), null);
            using var context = ClutterHooks.Enter(this);
            for (var i = 0; i < count; i++)
            {
                var e = Graph.Reference.Ecotypes[i];
                Placement[i] = new GroundClutterPlacementData(renderer, body, e);
                Placement[i].BuildResources(pool, command);
                Render[i] = new ClutterEcotypeRenderData(renderer, planet, pass, body, e, Placement[i]);
                Render[i].BuildRenderResources(pool, command);
                var radius = Render[i].GetMaxBoundingSphereRadius(applyScale: true);
                MaximumRadius = Math.Max(MaximumRadius, (float)radius);
                Physical[i] = new ClutterEcotypePhysicalData(renderer, planet, body, e, Render[i].EcotypeMeshAtlas,
                    PhysicalObjectRadii(i), Math.Max(radius, Graph.PhysicalRadii[i]), Placement[i]);
                Physical[i].BuildFrameResources(pool, command);
            }
        }
        catch
        {
            // Discard this pool's unsubmitted commands; native nested pools own their own waits.
            var submitted = ClutterController.Field(typeof(StagingPool), "_submitted").GetValue(pool);
            ClutterController.Field(typeof(StagingPool), "_commandBufferIndex").SetValue(pool, submitted);
            throw;
        }
        finally { command.End(); }
        _complete = true;
        // Pool disposal submits and waits before any replacement arrays become visible.
    }

    private float[] PhysicalObjectRadii(int ecotype)
    {
        var values = (float[])Render[ecotype].ObjectBoundingSphereRadii.Clone();
        for (var i = 0; i < values.Length; i++) values[i] = Math.Max(values[i], Graph.PhysicalObjectRadii[ecotype][i]);
        return values;
    }

    public void Dispose()
    {
        if (_disposed) return;
        // Callers establish both CPU/GPU completion and detach all external consumers first.
        var errors = new List<Exception>();
        void Clean(Action action) { try { action(); } catch (Exception ex) { errors.Add(ex); } }
        if (!_complete)
        {
            var cleanup = new ClutterRetirement();
            foreach (var item in Constructed) cleanup.Clean(item);
            errors.AddRange(cleanup.Errors);
        }
        else
        {
        foreach (var p in Physical) if (p != null) Clean(p.Dispose);
        foreach (var r in Render) if (r != null) Clean(r.Dispose);
        foreach (var p in Placement) if (p != null) Clean(p.Dispose);
        }
        if (MaterialBuffer.HasValue) Clean(MaterialBuffer.Value.Dispose);
        Clean(Graph.Dispose);
        _disposed = true;
        if (errors.Count != 0) throw new AggregateException("Pebbles resource retirement failed.", errors);
    }
}
