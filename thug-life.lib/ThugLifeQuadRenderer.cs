using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Brutal;
using Brutal.Numerics;
using Brutal.Pointers.Extensions;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;

namespace MeowSci.ThugLifeLib;

/// <summary>
/// Owns the per-draw GPU resources (pipeline, descriptor set, vertex/index buffers,
/// texture) needed to draw a textured unit-square quad in the scene. The model matrix
/// is rebuilt per-entry per-frame and pushed as a vertex push constant.
/// </summary>
public sealed unsafe class ThugLifeQuadRenderer : IDisposable
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct QuadVertex
    {
        public float3 Pos;
        public float2 Uv;
    }

    private readonly Renderer _renderer;
    private readonly ThugLifeTextureFactory _texture;

    private readonly DescriptorSetLayoutEx _descriptorSetLayout;
    private readonly DescriptorPoolEx _descriptorPool;
    private readonly VkDescriptorSet _descriptorSet;
    private readonly VkPipelineLayout _pipelineLayout;
    private readonly VkPipeline _pipeline;
    private readonly BufferEx _vb;
    private readonly BufferEx _ib;
    private readonly int _indexCount;

    private bool _disposed;

    public bool IsValid => !_disposed;

    public ThugLifeQuadRenderer(Renderer renderer, ThugLifeTextureFactory texture)
    {
        _renderer = renderer;
        _texture = texture;

        var device = renderer.Device;

        var binding = new VkDescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = VkDescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = VkShaderStageFlags.FragmentBit,
        };
        _descriptorSetLayout = device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo
        {
            Bindings = new Span<VkDescriptorSetLayoutBinding>(ref binding),
        }, null);

        var poolSize = new VkDescriptorPoolSize
        {
            Type = VkDescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
        };
        _descriptorPool = device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo
        {
            MaxSets = 1,
            PoolSizes = new Span<VkDescriptorPoolSize>(ref poolSize),
        }, null);
        _descriptorSet = device.AllocateDescriptorSet(_descriptorPool, _descriptorSetLayout);

        var imageInfo = new VkDescriptorImageInfo
        {
            ImageView = texture.ImageView,
            ImageLayout = VkImageLayout.ShaderReadOnlyOptimal,
            Sampler = texture.Sampler,
        };
        VkWriteDescriptorSet write = new VkWriteDescriptorSet
        {
            DstSet = _descriptorSet,
            DstBinding = 0,
            DescriptorCount = 1,
            DescriptorType = VkDescriptorType.CombinedImageSampler,
            ImageInfo = &imageInfo,
        };
        device.UpdateDescriptorSets(
            new ReadOnlySpan<VkWriteDescriptorSet>(ref write),
            default(ReadOnlySpan<VkCopyDescriptorSet>));

        VkPushConstantRange pushRange = new VkPushConstantRange
        {
            StageFlags = VkShaderStageFlags.VertexBit,
            Offset = ByteSize.Zero,
            Size = ByteSize.Of<float4x4>(),
        };
        VkDescriptorSetLayout dslHandle = _descriptorSetLayout;
        _pipelineLayout = device.CreatePipelineLayout(
            new ReadOnlySpan<VkDescriptorSetLayout>(ref dslHandle),
            new ReadOnlySpan<VkPushConstantRange>(ref pushRange),
            null);

        _pipeline = BuildPipeline(device, renderer, _pipelineLayout);
        (_vb, _ib, _indexCount) = BuildGeometry(renderer);
    }

    private static VkPipeline BuildPipeline(DeviceEx device, Renderer renderer, VkPipelineLayout layout)
    {
        var shaderRefs = new ShaderReference[]
        {
            ModLibrary.Get<ShaderReference>("UnlitMeshVert"),
            ModLibrary.Get<ShaderReference>("UnlitMeshFrag"),
        };
        var stages = RenderTechnique.CreateShaderStages(device, shaderRefs.AsSpan());

        var vertexInput = new VertexInput(1, 2)
            .AddBinding(0, ByteSize.Of<QuadVertex>(), VkVertexInputRate.Vertex)
            .AddAttribute(0, 0, VkFormat.R32G32B32SFloat, ByteSize.Zero)
            .AddAttribute(1, 0, VkFormat.R32G32SFloat, ByteSize.Of<float3>())
            .Check();

        var info = new VkGraphicsPipelineCreateInfo
        {
            Layout = layout,
            StageCount = stages.Count,
            Stages = stages,
            DynamicState = renderer.DynamicStateInfo,
            ViewportState = renderer.ViewportState,
            VertexInputState = vertexInput,
            InputAssemblyState = Presets.InputAssembly.TriangleList,
            RasterizationState = Presets.Rasterization.Fill.CullNone,
            DepthStencilState = RenderingPresets.ReverseZDepthStencil.DepthTestWrite,
            ColorBlendState = Presets.BlendState.BlendColorAlpha,
        };

        // KSA 2026.8.19.5261 migrated the main scene pass from classic Vulkan render
        // passes to dynamic rendering. Program.OffScreenPass (RenderPassState, with
        // .Pass/.SampleCount) no longer exists; the offscreen target is now
        // Program.OffscreenTarget (RenderTarget : IRenderPassInfo), and pass
        // compatibility is established by SetupGraphicsPipeline, which chains a
        // VkPipelineRenderingCreateInfo describing the colour/depth formats onto
        // pNext, sets RenderPass to VK_NULL_HANDLE, and fills in MultisampleState
        // with the target's sample count. So RenderPass/Subpass/MultisampleState must
        // NOT be set by hand here — this call supplies all three. Must stay
        // immediately before CreateGraphicsPipeline: the structures it points pNext
        // at are owned and overwritten by the RenderTarget on each call.
        // This mirrors the game's own main-pass pipelines (GenericMeshRenderer,
        // PartModelRenderer, PartModelGlass).
        Program.OffscreenTarget.SetupGraphicsPipeline(ref info);

        return device.CreateGraphicsPipeline(default(VkPipelineCache), info, null);
    }

    /// <summary>
    /// Builds geometry as one small quad per opaque pixel of <see cref="ThugLifeTexturePattern"/>.
    /// Transparent pixels emit no geometry, which is what produces the cut-out blocky sunglasses
    /// shape — the stock <c>UnlitMeshFrag</c> shader hard-writes <c>alpha = 1.0</c>, so alpha-blend
    /// transparency is not available; cut-out-via-geometry is.
    /// </summary>
    private static (BufferEx vb, BufferEx ib, int indexCount) BuildGeometry(Renderer renderer)
    {
        int w = ThugLifeTexturePattern.Width;
        int h = ThugLifeTexturePattern.Height;

        var verts = new List<QuadVertex>(capacity: w * h * 4);
        var indices = new List<ushort>(capacity: w * h * 6);

        // Tiny per-texel UV inset so a quad samples the centre of its texel and never
        // bleeds into the next one under nearest-neighbour filtering.
        const float uvInset = 0.001f;

        for (int row = 0; row < h; row++)
        {
            string rowStr = ThugLifeTexturePattern.Rows[row];
            for (int col = 0; col < w; col++)
            {
                if (rowStr[col] == '.') continue; // transparent — skip

                float x0 = -0.5f + (float)col / w;
                float x1 = -0.5f + (float)(col + 1) / w;
                // Flip Y so pattern row 0 maps to the top of the quad (+Y) and row N-1 to the bottom.
                float y1 = 0.5f - (float)row / h;
                float y0 = 0.5f - (float)(row + 1) / h;

                float u0 = ((float)col + uvInset) / w;
                float u1 = ((float)(col + 1) - uvInset) / w;
                float v0 = ((float)row + uvInset) / h;
                float v1 = ((float)(row + 1) - uvInset) / h;

                ushort baseIdx = (ushort)verts.Count;
                verts.Add(new QuadVertex { Pos = new float3(x0, y0, 0f), Uv = new float2(u0, v1) });
                verts.Add(new QuadVertex { Pos = new float3(x1, y0, 0f), Uv = new float2(u1, v1) });
                verts.Add(new QuadVertex { Pos = new float3(x1, y1, 0f), Uv = new float2(u1, v0) });
                verts.Add(new QuadVertex { Pos = new float3(x0, y1, 0f), Uv = new float2(u0, v0) });

                indices.Add((ushort)(baseIdx + 0));
                indices.Add((ushort)(baseIdx + 1));
                indices.Add((ushort)(baseIdx + 2));
                indices.Add((ushort)(baseIdx + 0));
                indices.Add((ushort)(baseIdx + 2));
                indices.Add((ushort)(baseIdx + 3));
            }
        }

        var vbSpan = CollectionsMarshal.AsSpan(verts);
        var ibSpan = CollectionsMarshal.AsSpan(indices);

        var vb = renderer.Allocator.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = "thug-life-vb",
            BufferUsage = VkBufferUsageFlags.VertexBufferBit | VkBufferUsageFlags.TransferDstBit,
            BufferSize = ByteSize.Of<QuadVertex>(vbSpan.Length),
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        var ib = renderer.Allocator.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = "thug-life-ib",
            BufferUsage = VkBufferUsageFlags.IndexBufferBit | VkBufferUsageFlags.TransferDstBit,
            BufferSize = ByteSize.Of<ushort>(ibSpan.Length),
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });

        using var staging = renderer.Allocator.CreateStagingPool(renderer.Graphics, 1);
        var cmd = staging.NextCommandBuffer();
        cmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        VkUtils.StageAndUploadToBuffer(staging, vb.VkBuffer, vb.BindOffset, vbSpan, cmd);
        VkUtils.StageAndUploadToBuffer(staging, ib.VkBuffer, ib.BindOffset, ibSpan, cmd);
        cmd.End();
        staging.Submit().Wait();

        return (vb, ib, ibSpan.Length);
    }

    /// <summary>
    /// Records the draw for a single entry. Caller must already be inside the offscreen
    /// render pass (i.e. invoked from a postfix on <c>SuperMeshRenderSystem.RenderMainPass</c>).
    ///
    /// Matrices come from <c>Program.GetRenderCamera()</c> — the camera of the viewport
    /// currently being rendered — NOT the main camera. <c>RenderMainPass</c> runs once per
    /// visible viewport, which since KSA 5261 includes the two crew-portrait viewports (they
    /// are always visible). Ego space is camera-relative and the view-projection is
    /// per-camera, so the main camera would draw the portrait passes with the wrong clip
    /// transform.
    /// </summary>
    public void RecordDraw(CommandBuffer cmd, ThugLifeEntry entry)
    {
        if (_disposed || !entry.Visible) return;

        var camera = Program.GetRenderCamera();
        if (camera == null) return;
        if (!TryComputeModelEgo(entry, camera, out float4x4 modelEgo)) return;

        float4x4 mvp = modelEgo * camera.MVP.viewProjection;

        cmd.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
        VkDescriptorSet setCopy = _descriptorSet;
        cmd.BindDescriptorSets(VkPipelineBindPoint.Graphics, _pipelineLayout, 0,
            new ReadOnlySpan<VkDescriptorSet>(ref setCopy),
            default(Span<ByteSize32>));

        Program.SetViewport(cmd);
        cmd.PushConstants(_pipelineLayout, VkShaderStageFlags.VertexBit, ByteSize.Zero, mvp);

        VkBuffer vbHandle = _vb.VkBuffer;
        ByteSize64 vbOff = (ByteSize64)_vb.BindOffset;
        cmd.BindVertexBuffers(0,
            new ReadOnlySpan<VkBuffer>(ref vbHandle),
            new ReadOnlySpan<ByteSize64>(ref vbOff));
        cmd.BindIndexBuffer(_ib.VkBuffer, (ByteSize64)_ib.BindOffset, VkIndexType.UInt16);
        cmd.DrawIndexed(_indexCount, 1, 0, 0, 0);
    }

    private static bool TryComputeModelEgo(ThugLifeEntry entry, Camera camera, out float4x4 model)
    {
        model = float4x4.Identity;
        if (entry.Vehicle == null || entry.Part == null) return false;

        double4x4 vehMat = entry.Vehicle.GetMatrixAsmb2Ego(camera);
        double3 partPos = entry.Part.PositionEgo(in vehMat);
        doubleQuat partRot = entry.Part.Asmb2Ego(entry.Vehicle.Asmb2Ego);

        float4x4 partRotMat = float4x4.CreateFromQuaternion(floatQuat.Pack(in partRot));
        float4x4 partTransMat = float4x4.CreateTranslation(float3.Pack(in partPos));
        float4x4 partEgo = partRotMat * partTransMat;

        const float deg2rad = MathF.PI / 180f;
        float4x4 userRot = float4x4.CreateRotationX(entry.Rotation.X * deg2rad)
                         * float4x4.CreateRotationY(entry.Rotation.Y * deg2rad)
                         * float4x4.CreateRotationZ(entry.Rotation.Z * deg2rad);
        float4x4 userTrans = float4x4.CreateTranslation(entry.Position);
        float4x4 scaleMat = float4x4.CreateScale(entry.Width, entry.Height, 1f);

        // v_local → scale → userRot → userTrans → partEgo → ego
        model = scaleMat * userRot * userTrans * partEgo;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var device = _renderer.Device;
        try { _vb.Dispose(); } catch { }
        try { _ib.Dispose(); } catch { }
        try { device.DestroyPipeline(_pipeline, null); } catch { }
        try { device.DestroyPipelineLayout(_pipelineLayout, null); } catch { }
        try { _descriptorPool.Dispose(); } catch { }
        try { _descriptorSetLayout.Dispose(); } catch { }
    }
}
