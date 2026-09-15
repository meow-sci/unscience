using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Brutal;
using Brutal.Numerics;
using Brutal.Pointers.Extensions;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using KSA.Rendering;
using RenderCore;

namespace MeowSci.GraffitiLib;

/// <summary>
/// The GPU half of graffiti: one pipeline, one unit-cube mesh and a per-frame ring of
/// scene-depth descriptor sets, recording one projected-decal draw per live decal into the main
/// viewport's colour image right after KSA resolves its attachments.
/// </summary>
/// <remarks>
/// <para><b>Why that seam.</b> Inside the opaque scope the depth attachment is being written and
/// is not sampleable. After <c>RenderTarget.ResolveAttachments</c> the resolved single-sample
/// <c>DepthImage</c> and <c>ColorImage</c> are both current and free — the window KSA's own
/// <c>GridPass</c> draws in; this pass is a near-verbatim port of it.</para>
/// <para><b>Why a box and not a quad.</b> The fragment shader reconstructs the scene position
/// under each pixel from the resolved depth and projects it into decal space, so the decal
/// conforms to whatever geometry is there — hull curvature and tessellated terrain.</para>
/// <para><b>Threading.</b> Constructed and disposed on the game thread; <see cref="RecordPass"/>
/// runs on the main thread inside the frame's command recording — the same thread, so the
/// published entry array needs no locking.</para>
/// </remarks>
internal sealed unsafe class DecalRenderer : IDisposable
{
    /// <summary>Sentinel texId that makes the shader draw a magenta checker instead of sampling.</summary>
    private const uint DebugTextureId = uint.MaxValue;

    /// <summary>
    /// Below this cosine between the receiving surface and the decal's outward axis the decal
    /// fades out and then stops drawing: a projected decal stretches without bound at grazing
    /// angles, and this is the standard cut-off that hides it.
    /// </summary>
    private const float NormalCutoff = 0.2f;

    /// <summary>Push-constant block size in bytes — 6 × vec4 + 4 × 4 B, inside the 128 B Vulkan minimum.</summary>
    private const int PushConstantBytes = 112;

    /// <summary>Indices in the unit cube (12 triangles).</summary>
    private const int CubeIndexCount = 36;

    /// <summary>
    /// Beyond this camera distance a decal is not drawn at all. Mutable — exposed in the panel's
    /// placement settings ("Max draw dist"), because at planetary zoom the cull is what finally
    /// removes a decal (terrain boxes already deepen with distance to survive LOD, see
    /// <see cref="DecalAnchors"/>).
    /// </summary>
    internal static double MaxViewDistanceMetres = 50_000;

    /// <summary>
    /// The per-draw push block. KSA matrices are row-vector (<c>v * M</c>), so component i of a
    /// transform is the dot of (p, 1) with COLUMN i — each vec4 here is one column of the
    /// row-vector matrix, which makes the shader's dot products reproduce
    /// <c>float3.Transform(pos, DecalToEgo)</c> exactly. 112 bytes is exactly full, so debug draw
    /// is signalled by <see cref="TextureId"/> = 0xFFFFFFFF, a value the 1024-slot bindless table
    /// can never hand out.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct DecalPush
    {
        public float4 DecalToEgo0;
        public float4 DecalToEgo1;
        public float4 DecalToEgo2;
        public float4 EgoToDecal0;
        public float4 EgoToDecal1;
        public float4 EgoToDecal2;
        public uint TextureId;
        public float Alpha;
        public float Brightness;
        public float NormalCutoffCos;
    }

    private readonly Renderer _renderer;

    private readonly DescriptorSetLayoutEx _depthSetLayout;
    private readonly DescriptorPoolEx _depthPool;
    private readonly VkDescriptorSet[] _depthSets;
    private readonly VkPipelineLayout _pipelineLayout;
    private readonly VkPipeline _pipeline;
    private readonly BufferEx _vertexBuffer;
    private readonly BufferEx _indexBuffer;

    private bool _disposed;
    private bool _constructed;

    /// <param name="renderer">The live renderer (device, allocator, queue and dynamic state come from it).</param>
    internal DecalRenderer(Renderer renderer)
    {
        _renderer = renderer;
        var device = renderer.Device;

        var binding = new VkDescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = VkDescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = VkShaderStageFlags.FragmentBit,
        };
        _depthSetLayout = device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo
        {
            Bindings = new Span<VkDescriptorSetLayoutBinding>(ref binding),
        }, null);

        // From here on partial construction is possible, so anything already created is released
        // by Dispose() before the exception leaves the constructor.
        try
        {
            // One set per frame in flight: the set for slot i is rewritten only when the engine
            // has already waited on slot i's fence, so no in-flight command buffer can be reading it.
            var frames = Math.Max(1, renderer.MaxFramesInFlight);
            var poolSize = new VkDescriptorPoolSize
            {
                Type = VkDescriptorType.CombinedImageSampler,
                DescriptorCount = frames,
            };
            _depthPool = device.CreateDescriptorPool(new DescriptorPoolEx.CreateInfo
            {
                MaxSets = frames,
                PoolSizes = new Span<VkDescriptorPoolSize>(ref poolSize),
            }, null);
            _depthSets = new VkDescriptorSet[frames];
            for (var i = 0; i < frames; i++)
                _depthSets[i] = device.AllocateDescriptorSet(_depthPool, _depthSetLayout);

            _pipelineLayout = BuildPipelineLayout(device, _depthSetLayout);
            _pipeline = BuildPipeline(device, renderer, _pipelineLayout);
            (_vertexBuffer, _indexBuffer) = BuildGeometry(renderer);
            _constructed = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>True while the renderer is fully built and not yet disposed — the "can I draw" test.</summary>
    internal bool IsValid => _constructed && !_disposed;

    // ---- pipeline ------------------------------------------------------------------------------

    /// <summary>
    /// Three descriptor sets — KSA's global UBO block (set 0, dynamic offset per viewport), our
    /// scene-depth sampler (set 1), KSA's bindless texture table (set 2, declared
    /// UpdateAfterBind|PartiallyBound) — plus the one push-constant range. Set indices are baked
    /// into the GLSL (SET_GLOBAL defaults to 0, SET_TEXTURE is #defined to 2), so this order is
    /// load-bearing.
    /// </summary>
    private static VkPipelineLayout BuildPipelineLayout(DeviceEx device, DescriptorSetLayoutEx depthSetLayout)
    {
        if (Program.Instance?.BindlessTextures is not { } bindless)
            throw new InvalidOperationException("the bindless texture table is not available yet");

        if (sizeof(DecalPush) != PushConstantBytes)
            throw new InvalidOperationException(
                $"the decal push block is {sizeof(DecalPush)} B, but the GLSL declares {PushConstantBytes}");

        Span<VkDescriptorSetLayout> setLayouts = stackalloc VkDescriptorSetLayout[3];
        setLayouts[0] = GlobalShaderBindings.DescriptorSetLayout;
        setLayouts[1] = depthSetLayout;
        setLayouts[2] = bindless.DescriptorSetLayout;

        var pushRange = new VkPushConstantRange
        {
            StageFlags = VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit,
            Offset = ByteSize.Zero,
            Size = ByteSize.Of<DecalPush>(),
        };
        return device.CreatePipelineLayout(setLayouts,
            new ReadOnlySpan<VkPushConstantRange>(ref pushRange), null);
    }

    /// <summary>
    /// Builds the decal pipeline against the resolved offscreen colour image, exactly as KSA's
    /// <c>GridPass.BuildPipeline</c> does for the map grid: hand-built rendering info (this pass
    /// draws AFTER the resolve, into the single-sample output image, with no depth attachment),
    /// <c>CullFront</c> so the box still covers its screen footprint when the camera is inside it
    /// (same reason KSA draws the planet with CullFront), and no depth test — occlusion is
    /// decided per fragment from the sampled scene depth.
    /// </summary>
    private static VkPipeline BuildPipeline(DeviceEx device, Renderer renderer, VkPipelineLayout layout)
    {
        var directory = ShaderIncludeDirectory();
        var vertexModule = Compile(device, DecalShaders.Vertex, VkShaderStageFlags.VertexBit,
            Path.Combine(directory, "graffiti_decal.vert"));
        VkShaderModule fragmentModule;
        try
        {
            fragmentModule = Compile(device, DecalShaders.Fragment, VkShaderStageFlags.FragmentBit,
                Path.Combine(directory, "graffiti_decal.frag"));
        }
        catch
        {
            device.DestroyShaderModule(vertexModule, null);
            throw;
        }

        try
        {
            Span<VkPipelineShaderStageCreateInfo> stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new VkPipelineShaderStageCreateInfo
            {
                Name = "main"u8.AsPointer(),
                Module = vertexModule,
                Stage = VkShaderStageFlags.VertexBit,
            };
            stages[1] = new VkPipelineShaderStageCreateInfo
            {
                Name = "main"u8.AsPointer(),
                Module = fragmentModule,
                Stage = VkShaderStageFlags.FragmentBit,
            };

            var vertexInput = new VertexInput(1, 1)
                .AddBinding(0, ByteSize.Of<float3>(), VkVertexInputRate.Vertex)
                .AddAttribute(0, 0, VkFormat.R32G32B32SFloat, ByteSize.Zero)
                .Check();

            var multisample = new VkPipelineMultisampleStateCreateInfo
            {
                RasterizationSamples = VkSampleCountFlags._1Bit,
            };
            var colorFormat = Program.Instance?.ColorFormat ?? VkFormat.R16G16B16A16SFloat;
            var rendering = new VkPipelineRenderingCreateInfo
            {
                ColorAttachmentCount = 1,
                ColorAttachmentFormats = &colorFormat,
                DepthAttachmentFormat = VkFormat.Undefined,
                StencilAttachmentFormat = VkFormat.Undefined,
                ViewMask = 0,
            };
            var info = new VkGraphicsPipelineCreateInfo
            {
                Layout = layout,
                Next = &rendering,
                RenderPass = VkRenderPass.NullHandle,
                StageCount = stages.Length,
                Stages = stages.AsPointer(),
                DynamicState = renderer.DynamicStateInfo,
                ViewportState = renderer.ViewportState,
                VertexInputState = vertexInput,
                InputAssemblyState = Presets.InputAssembly.TriangleList,
                RasterizationState = Presets.Rasterization.Fill.CullFront,
                DepthStencilState = RenderingPresets.ReverseZDepthStencil.NoDepthTest,
                ColorBlendState = RenderingPresets.BlendState.BlendColorAlphaOver,
                MultisampleState = &multisample,
            };
            return device.CreateGraphicsPipeline(default(VkPipelineCache), info, null);
        }
        finally
        {
            // Ours, unlike ModLibrary's modules: destroy them the moment the pipeline holds the code.
            device.DestroyShaderModule(vertexModule, null);
            device.DestroyShaderModule(fragmentModule, null);
        }
    }

    /// <summary>
    /// The directory KSA's own shaders live in, taken from a shipped asset so it follows the
    /// install rather than being guessed. Every <c>#include</c> in our two shaders resolves
    /// relative to it.
    /// </summary>
    private static string ShaderIncludeDirectory()
    {
        var reference = ModLibrary.Get<ShaderReference>("GridFrag")
                        ?? throw new InvalidOperationException("the 'GridFrag' shader asset is missing");
        return Path.GetDirectoryName(reference.ModPath)
               ?? throw new InvalidOperationException($"'{reference.ModPath}' has no directory");
    }

    /// <summary>Compiles one GLSL string, turning a shaderc failure into a message with the full log.</summary>
    private static VkShaderModule Compile(DeviceEx device, string source, VkShaderStageFlags stage, string debugPath)
    {
        try
        {
            // The NUL is required: the include resolver reads debugName as a C string, and
            // #include resolves relative to the directory of that debug name.
            return ShaderModuleUtils.FromString(device, Encoding.UTF8.GetBytes(source), stage, null,
                Encoding.UTF8.GetBytes(debugPath + "\0"));
        }
        catch (Brutal.ShaderCApi.ShaderException ex)
        {
            throw new InvalidOperationException(
                $"graffiti decal shader '{Path.GetFileName(debugPath)}' failed to compile: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Uploads the unit cube: 8 corners of [-0.5, 0.5]³ and 36 indices, wound counter-clockwise
    /// seen from outside (the glTF convention every KSA mesh renderer assumes). One-shot staging
    /// upload, submitted out of band and waited on — happens once, when the first decal goes live.
    /// </summary>
    private static (BufferEx Vertices, BufferEx Indices) BuildGeometry(Renderer renderer)
    {
        Span<float3> vertices =
        [
            new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f),
            new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f),
        ];
        Span<ushort> indices =
        [
            4, 5, 6, 4, 6, 7, // +z
            0, 3, 2, 0, 2, 1, // -z
            1, 2, 6, 1, 6, 5, // +x
            0, 4, 7, 0, 7, 3, // -x
            3, 7, 6, 3, 6, 2, // +y
            0, 1, 5, 0, 5, 4, // -y
        ];

        var vertexBuffer = renderer.Allocator.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = "graffiti-decal-vb",
            BufferUsage = VkBufferUsageFlags.VertexBufferBit | VkBufferUsageFlags.TransferDstBit,
            BufferSize = ByteSize.Of<float3>(vertices.Length),
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });
        var indexBuffer = renderer.Allocator.CreateBuffer(new BufferEx.CreateInfo
        {
            Name = "graffiti-decal-ib",
            BufferUsage = VkBufferUsageFlags.IndexBufferBit | VkBufferUsageFlags.TransferDstBit,
            BufferSize = ByteSize.Of<ushort>(indices.Length),
            AllocRequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit,
        });

        using var staging = renderer.Allocator.CreateStagingPool(renderer.Graphics, 1);
        var command = staging.NextCommandBuffer();
        command.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);
        VkUtils.StageAndUploadToBuffer(staging, vertexBuffer.VkBuffer, vertexBuffer.BindOffset, vertices, command);
        VkUtils.StageAndUploadToBuffer(staging, indexBuffer.VkBuffer, indexBuffer.BindOffset, indices, command);
        command.End();
        staging.Submit().Wait();
        return (vertexBuffer, indexBuffer);
    }

    // ---- the pass ------------------------------------------------------------------------------

    /// <summary>
    /// Records one draw per drawable decal into the resolved colour image. Called from the
    /// <c>ResolveAttachments</c> postfix, on the main thread, inside the frame's command buffer.
    /// Depth is moved to DepthSampledReadF and left there, exactly as KSA's GridPass leaves it;
    /// the scene depth is reverse-Z, so 0 is the far plane.
    /// </summary>
    /// <param name="commandBuffer">The command buffer KSA is recording the frame into.</param>
    /// <param name="entries">The submod's published array (never mutated here).</param>
    /// <param name="debug">Draw the magenta box checker instead of sampling the image.</param>
    internal void RecordPass(CommandBuffer commandBuffer, ReadOnlySpan<DecalEntry> entries, bool debug)
    {
        if (_disposed || entries.Length == 0)
            return;
        if (Program.OffscreenTarget is not { } target)
            return;
        if (target.DepthImage is not { } depthImage || target.ColorImage is not { } colorImage)
            return;

        var drawable = 0;
        foreach (var entry in entries)
            if (IsDrawable(entry))
                drawable++;
        if (drawable == 0)
            return;

        UpdateDepthDescriptor(depthImage, out var depthSet);

        Span<VkImageMemoryBarrier2> barrierImages = stackalloc VkImageMemoryBarrier2[2];
        var barriers = new BarrierBatch(barrierImages);
        barriers.Add(depthImage, ImageBarrierInfo.Presets.DepthSampledReadF);
        barriers.Add(colorImage, ImageBarrierInfo.Presets.ColorAttachmentReadWrite, 0, inForceBarrier: true);
        barriers.SubmitAndFlush(commandBuffer);

        var attachment = new VkRenderingAttachmentInfo
        {
            ImageView = colorImage.ImageView,
            ImageLayout = VkImageLayout.ColorAttachmentOptimal,
            ResolveMode = VkResolveModeFlags.None,
            LoadOp = VkAttachmentLoadOp.Load,
            StoreOp = VkAttachmentStoreOp.Store,
        };
        var renderingInfo = new VkRenderingInfo
        {
            RenderArea = new VkRect2D { Extent = target.Extent },
            LayerCount = 1,
            ViewMask = 0,
            ColorAttachmentCount = 1,
            ColorAttachments = &attachment,
        };
        commandBuffer.BeginRendering(in renderingInfo);
        try
        {
            commandBuffer.BindPipeline(VkPipelineBindPoint.Graphics, _pipeline);
            Program.SetViewport(commandBuffer);

            var globalOffset = (ByteSize32)GlobalShaderBindings.DynamicOffset(Program.MainViewport.ShaderSlot);
            var globalSet = GlobalShaderBindings.DescriptorSet;
            commandBuffer.BindDescriptorSets(VkPipelineBindPoint.Graphics, _pipelineLayout, 0,
                new ReadOnlySpan<VkDescriptorSet>(ref globalSet),
                new Span<ByteSize32>(ref globalOffset));
            commandBuffer.BindDescriptorSets(VkPipelineBindPoint.Graphics, _pipelineLayout, 1,
                new ReadOnlySpan<VkDescriptorSet>(ref depthSet), default(Span<ByteSize32>));
            if (Program.Instance?.BindlessTextures is { } bindless)
            {
                var bindlessSet = bindless.DescriptorSet;
                commandBuffer.BindDescriptorSets(VkPipelineBindPoint.Graphics, _pipelineLayout, 2,
                    new ReadOnlySpan<VkDescriptorSet>(ref bindlessSet), default(Span<ByteSize32>));
            }

            VkBuffer vertexHandle = _vertexBuffer.VkBuffer;
            var vertexOffset = (ByteSize64)_vertexBuffer.BindOffset;
            commandBuffer.BindVertexBuffers(0,
                new ReadOnlySpan<VkBuffer>(ref vertexHandle),
                new ReadOnlySpan<ByteSize64>(ref vertexOffset));
            commandBuffer.BindIndexBuffer(_indexBuffer.VkBuffer, (ByteSize64)_indexBuffer.BindOffset,
                VkIndexType.UInt16);

            foreach (var entry in entries)
            {
                if (!IsDrawable(entry))
                    continue;
                var push = Push(entry, debug);
                commandBuffer.PushConstants(_pipelineLayout,
                    VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit, ByteSize.Zero, push);
                commandBuffer.DrawIndexed(CubeIndexCount, 1, 0, 0, 0);
            }
        }
        finally
        {
            commandBuffer.EndRendering();
        }
    }

    private static bool IsDrawable(DecalEntry entry)
        => entry.Visible && entry.Live && entry.TextureHandle >= 0
           && entry.DistanceEgo <= MaxViewDistanceMetres;

    private static DecalPush Push(DecalEntry entry, bool debug) => new()
    {
        DecalToEgo0 = Column(in entry.DecalToEgo, 0),
        DecalToEgo1 = Column(in entry.DecalToEgo, 1),
        DecalToEgo2 = Column(in entry.DecalToEgo, 2),
        EgoToDecal0 = Column(in entry.EgoToDecal, 0),
        EgoToDecal1 = Column(in entry.EgoToDecal, 1),
        EgoToDecal2 = Column(in entry.EgoToDecal, 2),
        TextureId = debug ? DebugTextureId : (uint)entry.TextureHandle,
        Alpha = (float)entry.Alpha,
        Brightness = (float)entry.Brightness,
        NormalCutoffCos = NormalCutoff,
    };

    /// <summary>One column of a row-vector matrix — see <see cref="DecalPush"/> for why.</summary>
    private static float4 Column(ref readonly float4x4 matrix, int index) => index switch
    {
        0 => new float4(matrix.X.X, matrix.Y.X, matrix.Z.X, matrix.W.X),
        1 => new float4(matrix.X.Y, matrix.Y.Y, matrix.Z.Y, matrix.W.Y),
        _ => new float4(matrix.X.Z, matrix.Y.Z, matrix.Z.Z, matrix.W.Z),
    };

    /// <summary>Points this frame's ring slot at the live resolved depth image.</summary>
    private void UpdateDepthDescriptor(RenderImage depthImage, out VkDescriptorSet set)
    {
        var slot = Program.Instance is { } program
            ? (uint)program.ResourceFrameIndex % (uint)_depthSets.Length
            : 0u;
        set = _depthSets[slot];

        var imageInfo = new VkDescriptorImageInfo
        {
            ImageLayout = VkImageLayout.DepthReadOnlyOptimal,
            ImageView = depthImage.ImageView,
            Sampler = Program.PointClampedSampler,
        };
        var write = new VkWriteDescriptorSet
        {
            DescriptorType = VkDescriptorType.CombinedImageSampler,
            DstSet = set,
            DstBinding = 0,
            DescriptorCount = 1,
            ImageInfo = &imageInfo,
        };
        _renderer.Device.UpdateDescriptorSets(
            new ReadOnlySpan<VkWriteDescriptorSet>(ref write),
            default(ReadOnlySpan<VkCopyDescriptorSet>));
    }

    /// <summary>
    /// Frees every GPU object, in reverse creation order and best-effort. The caller has already
    /// stopped the render gate and waited for the device to go idle.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var device = _renderer.Device;
        // The shader modules were destroyed at pipeline creation — they are not held here. Every
        // step is guarded because this also runs from a partially built constructor.
        if (_constructed)
        {
            try { _vertexBuffer.Dispose(); } catch (Exception ex) { Note(ex); }
            try { _indexBuffer.Dispose(); } catch (Exception ex) { Note(ex); }
        }

        try { device.DestroyPipeline(_pipeline, null); } catch (Exception ex) { Note(ex); }
        try { device.DestroyPipelineLayout(_pipelineLayout, null); } catch (Exception ex) { Note(ex); }
        // The pool may be unset when a throw beat its creation; the layout always exists by then.
        try { _depthPool?.Dispose(); } catch (Exception ex) { Note(ex); }
        try { _depthSetLayout.Dispose(); } catch (Exception ex) { Note(ex); }
    }

    private static void Note(Exception ex)
        => Console.WriteLine($"graffiti: decal renderer teardown step failed: {ex.Message}");
}
