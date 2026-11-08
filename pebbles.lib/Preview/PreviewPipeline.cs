using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Brutal;
using Brutal.Pointers.Extensions;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using RenderCore;

namespace MeowSci.PebblesLib;

[StructLayout(LayoutKind.Sequential)]
internal struct PreviewVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 Uv;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PreviewPush
{
    public Matrix4x4 ViewProjection;
    public Vector4 Camera;
    public Vector4 Maps;
    public Vector4 Options;
}

/// <summary>Independent descriptor/pipeline layout: no game viewport or global camera buffer.</summary>
internal sealed unsafe class PreviewPipeline : IDisposable
{
    internal const VkFormat ColorFormat = VkFormat.R8G8B8A8UNorm;
    internal const VkFormat DepthFormat = VkFormat.D32SFloat;
    private readonly Renderer _renderer;
    internal DescriptorSetLayoutEx? SetLayout { get; private set; }
    internal VkPipelineLayout Layout { get; private set; }
    internal VkPipeline Pipeline { get; private set; }
    internal VkSampler Sampler { get; private set; }

    internal PreviewPipeline(Renderer renderer)
    {
        _renderer = renderer;
        if (Marshal.SizeOf<PreviewVertex>() != 32 || Marshal.SizeOf<PreviewPush>() != 112 ||
            Marshal.OffsetOf<PreviewVertex>(nameof(PreviewVertex.Normal)).ToInt32() != 12 ||
            Marshal.OffsetOf<PreviewVertex>(nameof(PreviewVertex.Uv)).ToInt32() != 24 ||
            Marshal.OffsetOf<PreviewPush>(nameof(PreviewPush.Camera)).ToInt32() != 64 ||
            Marshal.OffsetOf<PreviewPush>(nameof(PreviewPush.Maps)).ToInt32() != 80 ||
            Marshal.OffsetOf<PreviewPush>(nameof(PreviewPush.Options)).ToInt32() != 96)
            throw new InvalidOperationException("Workshop shader memory layout is incompatible with this runtime.");
        try
        {
            var bindings = new VkDescriptorSetLayoutBinding[5];
            for (int i = 0; i < bindings.Length; i++) bindings[i] = new()
            {
                Binding = i, DescriptorType = VkDescriptorType.CombinedImageSampler,
                DescriptorCount = 1, StageFlags = VkShaderStageFlags.FragmentBit
            };
            SetLayout = renderer.Device.CreateDescriptorSetLayout(new DescriptorSetLayoutEx.CreateInfo { Bindings = bindings }, null);
            VkDescriptorSetLayout set = SetLayout;
            var push = new VkPushConstantRange
            {
                StageFlags = VkShaderStageFlags.VertexBit | VkShaderStageFlags.FragmentBit,
                Offset = ByteSize.Zero, Size = ByteSize.Of<PreviewPush>()
            };
            Layout = renderer.Device.CreatePipelineLayout(new ReadOnlySpan<VkDescriptorSetLayout>(ref set),
                new ReadOnlySpan<VkPushConstantRange>(ref push), null);
            var sampler = new VkSamplerCreateInfo
            {
                MagFilter = VkFilter.Linear, MinFilter = VkFilter.Linear, MipmapMode = VkSamplerMipmapMode.Linear,
                AddressModeU = VkSamplerAddressMode.Repeat, AddressModeV = VkSamplerAddressMode.Repeat,
                AddressModeW = VkSamplerAddressMode.Repeat, MinLod = 0, MaxLod = 16, MaxAnisotropy = 1
            };
            Sampler = renderer.Device.CreateSampler(in sampler, null);
            Build();
        }
        catch { Dispose(); throw; }
    }

    private void Build()
    {
        var device = _renderer.Device;
        VkShaderModule vertex = default, fragment = default;
        try
        {
            vertex = Compile("Workshop.vert.glsl", VkShaderStageFlags.VertexBit);
            fragment = Compile("Workshop.frag.glsl", VkShaderStageFlags.FragmentBit);
            Span<VkPipelineShaderStageCreateInfo> stages = stackalloc VkPipelineShaderStageCreateInfo[2];
            stages[0] = new() { Name = "main"u8.AsPointer(), Module = vertex, Stage = VkShaderStageFlags.VertexBit };
            stages[1] = new() { Name = "main"u8.AsPointer(), Module = fragment, Stage = VkShaderStageFlags.FragmentBit };
            using var input = new VertexInput(1, 3).AddBinding(0, ByteSize.Of<PreviewVertex>(), VkVertexInputRate.Vertex)
                .AddAttribute(0, 0, VkFormat.R32G32B32SFloat, ByteSize.Zero)
                .AddAttribute(1, 0, VkFormat.R32G32B32SFloat, new ByteSize(12u))
                .AddAttribute(2, 0, VkFormat.R32G32SFloat, new ByteSize(24u)).Check();
            var samples = new VkPipelineMultisampleStateCreateInfo { RasterizationSamples = VkSampleCountFlags._1Bit };
            var depth = new VkPipelineDepthStencilStateCreateInfo
            {
                DepthTestEnable = true, DepthWriteEnable = true, DepthCompareOp = VkCompareOp.LessOrEqual,
                MinDepthBounds = 0, MaxDepthBounds = 1
            };
            VkFormat color = ColorFormat;
            var rendering = new VkPipelineRenderingCreateInfo
            {
                ColorAttachmentCount = 1, ColorAttachmentFormats = &color, DepthAttachmentFormat = DepthFormat
            };
            var info = new VkGraphicsPipelineCreateInfo
            {
                Layout = Layout, Next = &rendering, RenderPass = VkRenderPass.NullHandle,
                StageCount = stages.Length, Stages = stages.AsPointer(),
                DynamicState = _renderer.DynamicStateInfo, ViewportState = _renderer.ViewportState,
                VertexInputState = input, InputAssemblyState = Presets.InputAssembly.TriangleList,
                RasterizationState = Presets.Rasterization.Fill.CullNone, DepthStencilState = &depth,
                ColorBlendState = Presets.BlendState.BlendNone, MultisampleState = &samples
            };
            Pipeline = device.CreateGraphicsPipeline(default(VkPipelineCache), info, null);
        }
        finally
        {
            if (!vertex.IsNull()) device.DestroyShaderModule(vertex, null);
            if (!fragment.IsNull()) device.DestroyShaderModule(fragment, null);
        }
    }

    private VkShaderModule Compile(string suffix, VkShaderStageFlags stage)
    {
        var assembly = typeof(PreviewPipeline).Assembly;
        string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing preview shader {name}.");
        using var reader = new StreamReader(stream);
        return ShaderModuleUtils.FromString(_renderer.Device, Encoding.UTF8.GetBytes(reader.ReadToEnd()), stage,
            null, Encoding.UTF8.GetBytes(name + "\0"));
    }

    public void Dispose()
    {
        if (!Pipeline.IsNull()) { _renderer.Device.DestroyPipeline(Pipeline, null); Pipeline = default; }
        if (!Layout.IsNull()) { _renderer.Device.DestroyPipelineLayout(Layout, null); Layout = default; }
        if (!Sampler.IsNull()) { _renderer.Device.DestroySampler(Sampler, null); Sampler = default; }
        SetLayout?.Dispose(); SetLayout = null;
    }
}
