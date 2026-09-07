using System;
using System.Collections.Generic;
using System.Numerics;
using Brutal;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;

namespace MeowSci.PebblesLib;

/// <summary>Owns GPU copies and descriptor sets; referenced game textures remain borrowed.</summary>
internal sealed unsafe class PreviewScene : IDisposable
{
    private sealed class Draw : IDisposable
    {
        internal BufferEx? Vertices;
        internal BufferEx? Indices;
        internal VkDescriptorSet Set;
        internal int Count;
        internal Vector4 Maps, Options;
        // Hold references for the complete descriptor lifetime, including fallback textures.
        internal TextureReference[] Textures = [];
        public void Dispose() { Indices?.Dispose(); Vertices?.Dispose(); Indices = null; Vertices = null; Textures = []; }
    }

    private readonly List<Draw> _draws = new();
    private DescriptorPoolEx? _pool;

    internal PreviewScene(Renderer renderer, PreviewPipeline pipeline, PreviewGeometry geometry)
    {
        try
        {
            int count = geometry.Draws.Count;
            if (count == 0) throw new InvalidOperationException("No drawable preview primitives.");
            var poolSize = new VkDescriptorPoolSize { Type = VkDescriptorType.CombinedImageSampler, DescriptorCount = checked(count * 5) };
            _pool = renderer.Device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo
            {
                MaxSets = count, PoolSizes = new Span<VkDescriptorPoolSize>(ref poolSize)
            }, null);
            using var submission = new AssetUploadSubmission(renderer);
            var staging = submission.Staging;
            var command = submission.Command;
            foreach (var source in geometry.Draws)
            {
                var draw = new Draw { Count = source.Indices.Length, Maps = source.Maps, Options = source.Options, Textures = source.Textures };
                _draws.Add(draw);
                draw.Vertices = Buffer(renderer, "Pebbles preview vertices", ByteSize.Of<PreviewVertex>(source.Vertices.Length), VkBufferUsageFlags.VertexBufferBit);
                draw.Indices = Buffer(renderer, "Pebbles preview indices", ByteSize.Of<uint>(source.Indices.Length), VkBufferUsageFlags.IndexBufferBit);
                VkUtils.StageAndUploadToBuffer(staging, draw.Vertices.Value.VkBuffer, draw.Vertices.Value.BindOffset, source.Vertices.AsSpan(), command);
                VkUtils.StageAndUploadToBuffer(staging, draw.Indices.Value.VkBuffer, draw.Indices.Value.BindOffset, source.Indices.AsSpan(), command);
                VkBufferMemoryBarrier[] barriers =
                [
                    KSA.Rendering.Utils.CreateBarrier(draw.Vertices.Value.VkBuffer, VkAccessFlags.TransferWriteBit,
                        VkAccessFlags.VertexAttributeReadBit, VK.WHOLE_SIZE, ByteSize.Zero),
                    KSA.Rendering.Utils.CreateBarrier(draw.Indices.Value.VkBuffer, VkAccessFlags.TransferWriteBit,
                        VkAccessFlags.IndexReadBit, VK.WHOLE_SIZE, ByteSize.Zero)
                ];
                command.PipelineBarrier(VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.VertexInputBit,
                    VkDependencyFlags.None, default(ReadOnlySpan<VkMemoryBarrier>), barriers, default(ReadOnlySpan<VkImageMemoryBarrier>));
                draw.Set = renderer.Device.AllocateDescriptorSet(_pool, pipeline.SetLayout!);
                for (int i = 0; i < 5; i++)
                {
                    var image = new VkDescriptorImageInfo
                    {
                        ImageView = source.Textures[i].ImageView, ImageLayout = VkImageLayout.ShaderReadOnlyOptimal, Sampler = pipeline.Sampler
                    };
                    var write = new VkWriteDescriptorSet
                    {
                        DstSet = draw.Set, DstBinding = i, DescriptorType = VkDescriptorType.CombinedImageSampler,
                        DescriptorCount = 1, ImageInfo = &image
                    };
                    renderer.Device.UpdateDescriptorSets(new ReadOnlySpan<VkWriteDescriptorSet>(ref write), default(ReadOnlySpan<VkCopyDescriptorSet>));
                }
            }
            submission.SubmitAndWait();
        }
        catch
        {
            // The staging scope retires its submissions before owned destinations are released.
            renderer.Device.WaitIdle();
            Dispose();
            throw;
        }
    }

    private static BufferEx Buffer(Renderer renderer, string name, ByteSize size, VkBufferUsageFlags usage) =>
        renderer.Allocator.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = name, BufferSize = size, BufferUsage = usage | VkBufferUsageFlags.TransferDstBit,
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit
        });

    internal void Record(CommandBuffer command, PreviewPipeline pipeline, Matrix4x4 viewProjection, Vector3 eye)
    {
        command.BindPipeline(VkPipelineBindPoint.Graphics, pipeline.Pipeline);
        foreach (var draw in _draws)
        {
            var push = new PreviewPush { ViewProjection = viewProjection, Camera = new Vector4(eye, 1), Maps = draw.Maps, Options = draw.Options };
            command.PushConstants(pipeline.Layout, VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit, ByteSize.Zero, push);
            VkDescriptorSet set = draw.Set;
            command.BindDescriptorSets(VkPipelineBindPoint.Graphics, pipeline.Layout, 0,
                new ReadOnlySpan<VkDescriptorSet>(ref set), default(Span<ByteSize32>));
            command.BindVertexBuffer(0, draw.Vertices!.Value.VkBuffer, draw.Vertices.Value.BindOffset);
            VkDeviceExtensions.BindIndexBuffer(command, draw.Indices!.Value.VkBuffer, draw.Indices.Value.BindOffset, VkIndexType.Uint32);
            command.DrawIndexed(draw.Count, 1, 0, 0, 0);
        }
    }

    public void Dispose()
    {
        _pool?.Dispose(); _pool = null;
        foreach (var draw in _draws) draw.Dispose();
        _draws.Clear();
    }
}
