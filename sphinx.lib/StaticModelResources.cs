using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using RenderCore;
using KSA;
using MeowSci.PebblesLib;

namespace MeowSci.SphinxLib;

/// <summary>Private geometry/descriptors using the native static-object shader layouts.</summary>
internal sealed unsafe partial class StaticModelResources : IDisposable
{
    private sealed class Draw : IDisposable
    {
        public BufferEx? Vertices, Indices, Material;
        public VkDescriptorSet Set;
        public int Count;
        public bool Alpha;
        public TextureReference[] Textures = [];
        public StaticVertex[] OriginalVertices = [];
        public void Dispose() { Material?.Dispose(); Indices?.Dispose(); Vertices?.Dispose(); Textures = []; OriginalVertices = []; }
    }
    private readonly List<Draw> _draws = new();
    private DescriptorPoolEx? _pool;
    private BufferEx? _instances;
    private MappedMemory? _mapping;
    private VkSampler _sampler;
    private readonly VkDescriptorSet[] _instanceSets;
    private readonly ulong[] _preparedFrames;
    private readonly int _frames, _stride;
    private ImportedPngTexture? _png;
    private bool _disposed;
    public Renderer Owner { get; }
    public Vector3 Min { get; private set; }
    public Vector3 Max { get; private set; }
    public int VertexCount { get; private set; }

    public StaticModelResources(ClutterAssets assets, string meshId, string? png, int remainingVertices, TextureMapping mapping)
    {
        Owner = Program.GetRenderer();
        _frames = Owner.MaxFramesInFlight;
        _stride = (int)ByteSize.Of<float4x4>().AlignTo(KSA.Rendering.Utils.MinStorageBufferOffsetAlignment);
        _instanceSets = new VkDescriptorSet[_frames * ViewportRegistry.MAX_VIEWPORTS];
        _preparedFrames = new ulong[_instanceSets.Length];
        if (Marshal.SizeOf<StaticVertex>() != 32 || Marshal.SizeOf<StaticObjectModel.PerDrawData>() != 24 || Marshal.SizeOf<float4x4>() != 64 ||
            Marshal.OffsetOf<StaticVertex>(nameof(StaticVertex.Position)).ToInt32() != 0 ||
            Marshal.OffsetOf<StaticVertex>(nameof(StaticVertex.Normal)).ToInt32() != 12 ||
            Marshal.OffsetOf<StaticVertex>(nameof(StaticVertex.Uv)).ToInt32() != 24 ||
            Marshal.OffsetOf<StaticObjectModel.PerDrawData>(nameof(StaticObjectModel.PerDrawData.DiffuseTextureIndex)).ToInt32() != 0 ||
            Marshal.OffsetOf<StaticObjectModel.PerDrawData>(nameof(StaticObjectModel.PerDrawData.NormalTextureIndex)).ToInt32() != 4 ||
            Marshal.OffsetOf<StaticObjectModel.PerDrawData>(nameof(StaticObjectModel.PerDrawData.PbrTextureIndex)).ToInt32() != 8 ||
            Marshal.OffsetOf<StaticObjectModel.PerDrawData>(nameof(StaticObjectModel.PerDrawData.EmissiveTextureIndex)).ToInt32() != 12 ||
            Marshal.OffsetOf<StaticObjectModel.PerDrawData>(nameof(StaticObjectModel.PerDrawData.TfiTextureIndex)).ToInt32() != 16 ||
            Marshal.OffsetOf<StaticObjectModel.PerDrawData>(nameof(StaticObjectModel.PerDrawData.AlphaTextureIndex)).ToInt32() != 20)
            throw new InvalidOperationException("The game's static-object shader layout changed.");
        try
        {
            mapping.Validate();
            if (!string.IsNullOrEmpty(png)) _png = new ImportedPngTexture(png);
            var geometry = StaticGeometry.Read(assets, meshId, _png);
            Min = geometry.Min; Max = geometry.Max; VertexCount = geometry.VertexCount;
            if (VertexCount > remainingVertices) throw new InvalidOperationException("Sphinx has reached eight million placed vertices. Remove some statics first.");
            int count = geometry.Primitives.Count;
            VkDescriptorPoolSize[] sizes =
            [
                new() { Type = VkDescriptorType.StorageBuffer, DescriptorCount = count + _instanceSets.Length },
                new() { Type = VkDescriptorType.Sampler, DescriptorCount = count }
            ];
            _pool = Owner.Device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo { MaxSets = count + _instanceSets.Length, PoolSizes = sizes }, null);
            var sampler = new VkSamplerCreateInfo
            {
                MagFilter = VkFilter.Linear, MinFilter = VkFilter.Linear, MipmapMode = VkSamplerMipmapMode.Linear,
                AddressModeU = VkSamplerAddressMode.Repeat, AddressModeV = VkSamplerAddressMode.Repeat,
                AddressModeW = VkSamplerAddressMode.Repeat, MinLod = 0, MaxLod = 16, MaxAnisotropy = 1
            };
            _sampler = Owner.Device.CreateSampler(in sampler, null);
            _instances = Owner.Allocator.CreateBuffer(new BufferEx.CreateInfo
            {
                Name = "Sphinx instance matrices", BufferSize = new ByteSize((uint)(_stride * _instanceSets.Length)),
                BufferUsage = VkBufferUsageFlags.StorageBufferBit,
                AllocRequiredProperties = VkMemoryPropertyFlags.HostVisibleBit | VkMemoryPropertyFlags.HostCoherentBit
            });
            _mapping = _instances.Value.Map();
            for (int i = 0; i < _instanceSets.Length; i++)
            {
                _instanceSets[i] = Owner.Device.AllocateDescriptorSet(_pool, StaticObjectRenderer.PerInstanceDataDescriptorSetLayout);
                WriteBuffer(_instanceSets[i], _instances.Value, new ByteSize((uint)(i * _stride)), ByteSize.Of<float4x4>());
            }
            using var upload = new AssetUploadSubmission(Owner);
            foreach (var source in geometry.Primitives)
            {
                var draw = new Draw { Count = source.Indices.Length, Alpha = source.Material.AlphaTextureIndex >= 0,
                    Textures = source.Textures, OriginalVertices = source.Vertices };
                _draws.Add(draw);
                draw.Vertices = Buffer("Sphinx vertices", ByteSize.Of<StaticVertex>(source.Vertices.Length), VkBufferUsageFlags.VertexBufferBit);
                draw.Indices = Buffer("Sphinx indices", ByteSize.Of<uint>(source.Indices.Length), VkBufferUsageFlags.IndexBufferBit);
                draw.Material = Buffer("Sphinx material", ByteSize.Of<StaticObjectModel.PerDrawData>(), VkBufferUsageFlags.StorageBufferBit);
                var vertices = mapping.Apply(source.Vertices);
                VkUtils.StageAndUploadToBuffer(upload.Staging, draw.Vertices.Value.VkBuffer, draw.Vertices.Value.BindOffset, vertices.AsSpan(), upload.Command);
                VkUtils.StageAndUploadToBuffer(upload.Staging, draw.Indices.Value.VkBuffer, draw.Indices.Value.BindOffset, source.Indices.AsSpan(), upload.Command);
                var material = source.Material;
                VkUtils.StageAndUploadToBuffer(upload.Staging, draw.Material.Value.VkBuffer, draw.Material.Value.BindOffset, new Span<StaticObjectModel.PerDrawData>(ref material), upload.Command);
                VkBufferMemoryBarrier[] barriers =
                [
                    KSA.Rendering.Utils.CreateBarrier(draw.Vertices.Value.VkBuffer, VkAccessFlags.TransferWriteBit, VkAccessFlags.VertexAttributeReadBit, VK.WHOLE_SIZE, ByteSize.Zero),
                    KSA.Rendering.Utils.CreateBarrier(draw.Indices.Value.VkBuffer, VkAccessFlags.TransferWriteBit, VkAccessFlags.IndexReadBit, VK.WHOLE_SIZE, ByteSize.Zero),
                    KSA.Rendering.Utils.CreateBarrier(draw.Material.Value.VkBuffer, VkAccessFlags.TransferWriteBit, VkAccessFlags.ShaderReadBit, VK.WHOLE_SIZE, ByteSize.Zero)
                ];
                upload.Command.PipelineBarrier(VkPipelineStageFlags.TransferBit, VkPipelineStageFlags.VertexInputBit | VkPipelineStageFlags.FragmentShaderBit,
                    VkDependencyFlags.None, default(ReadOnlySpan<VkMemoryBarrier>), barriers, default(ReadOnlySpan<VkImageMemoryBarrier>));
                draw.Set = Owner.Device.AllocateDescriptorSet(_pool, StaticObjectRenderer.PerDrawDataDescriptorSetLayout);
                WriteBuffer(draw.Set, draw.Material.Value, ByteSize.Zero, ByteSize.Of<StaticObjectModel.PerDrawData>());
                var image = new VkDescriptorImageInfo { Sampler = _sampler };
                var write = new VkWriteDescriptorSet { DstSet = draw.Set, DstBinding = 1, DescriptorCount = 1, DescriptorType = VkDescriptorType.Sampler, ImageInfo = &image };
                Owner.Device.UpdateDescriptorSets(new ReadOnlySpan<VkWriteDescriptorSet>(ref write), default(ReadOnlySpan<VkCopyDescriptorSet>));
            }
            upload.SubmitAndWait();
        }
        catch { Dispose(); throw; }
    }

    private BufferEx Buffer(string name, ByteSize size, VkBufferUsageFlags usage) => Owner.Allocator.CreateBuffer(new BufferEx.CreateInfo
    { Name = name, BufferSize = size, BufferUsage = usage | VkBufferUsageFlags.TransferDstBit, AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit });

    private void WriteBuffer(VkDescriptorSet set, BufferEx buffer, ByteSize offset, ByteSize range)
    {
        var info = new VkDescriptorBufferInfo { Buffer = buffer.VkBuffer, Offset = buffer.BindOffset + offset, Range = range };
        var write = new VkWriteDescriptorSet { DstSet = set, DstBinding = 0, DescriptorCount = 1, DescriptorType = VkDescriptorType.StorageBuffer, BufferInfo = &info };
        Owner.Device.UpdateDescriptorSets(new ReadOnlySpan<VkWriteDescriptorSet>(ref write), default(ReadOnlySpan<VkCopyDescriptorSet>));
    }

    private int Slot(IViewport viewport, int frame)
    {
        if (frame < 0 || frame >= _frames || viewport.ShaderSlot < 0 || viewport.ShaderSlot >= ViewportRegistry.MAX_VIEWPORTS)
            throw new InvalidOperationException("Sphinx viewport/frame allocation no longer matches the game.");
        return viewport.ShaderSlot * _frames + frame;
    }
    public void Prepare(IViewport viewport, int frame, float4x4 model)
    {
        if (_disposed || !ReferenceEquals(Owner, Program.GetRenderer()))
            throw new InvalidOperationException("The renderer changed; remove and re-place this static.");
        int slot = Slot(viewport, frame);
        MemoryMarshal.Write(_mapping!.Value.AsSpan().Slice(slot * _stride, 64), in model);
        _preparedFrames[slot] = Owner.FrameCount;
    }
    public void Record(CommandBuffer command, IViewport viewport, int frame, bool prepass, bool alpha)
    {
        if (_disposed || !ReferenceEquals(Owner, Program.GetRenderer())) return;
        int slot = Slot(viewport, frame);
        if (_preparedFrames[slot] != Owner.FrameCount) return;
        var layout = prepass ? StaticObjectRenderer.PrePassPipelineLayout : StaticObjectRenderer.PipelineLayout;
        foreach (var draw in _draws)
        {
            // Blended cutouts have no prepass: stock's normal prepass does not sample alpha.
            if (draw.Alpha != alpha || (prepass && draw.Alpha)) continue;
            VkDescriptorSet[] sets = [draw.Set, _instanceSets[slot]];
            command.BindDescriptorSets(VkPipelineBindPoint.Graphics, layout, 2, sets, default(Span<ByteSize32>));
            command.BindVertexBuffer(0, draw.Vertices!.Value.VkBuffer, draw.Vertices.Value.BindOffset);
            VkDeviceExtensions.BindIndexBuffer(command, draw.Indices!.Value.VkBuffer, draw.Indices.Value.BindOffset, VkIndexType.Uint32);
            // Direct draw => gl_DrawID == 0; each descriptor points at exactly one PerDrawData.
            command.DrawIndexed(draw.Count, 1, 0, 0, 0);
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        Owner.Device.WaitIdle(); // Retire all viewport uses before freeing buffers, descriptors or images.
        _disposed = true;
        _pool?.Dispose(); _pool = null;
        _mapping?.Dispose(); _mapping = null;
        _instances?.Dispose(); _instances = null;
        foreach (var draw in _draws) draw.Dispose();
        _draws.Clear();
        if (!_sampler.IsNull()) { Owner.Device.DestroySampler(_sampler, null); _sampler = default; }
        _png?.Dispose(); _png = null;
    }
}
