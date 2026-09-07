using System;
using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using KSA.Rendering;

namespace MeowSci.PebblesLib;

/// <summary>Private images; caller retires UI consumers before resizing or destruction.</summary>
internal sealed unsafe class PreviewTarget : IDisposable
{
    private readonly Renderer _renderer;
    private ImageEx? _colorImage;
    private ImageViewEx? _colorView;
    private ImageEx? _depth;
    private ImageViewEx? _depthView;
    private bool _initialized;
    internal int Width { get; }
    internal int Height { get; }
    internal ImTextureRef Texture { get; private set; }

    internal PreviewTarget(Renderer renderer, int width, int height)
    {
        _renderer = renderer; Width = width; Height = height;
        try
        {
            var colorInfo = ImageInfo("Pebbles Workshop color", PreviewPipeline.ColorFormat,
                VkImageUsageFlags.ColorAttachmentBit | VkImageUsageFlags.SampledBit);
            _colorImage = renderer.Allocator.CreateImage(colorInfo);
            _colorView = _colorImage.Value.CreateImageView(VkImageViewType._2D, Subresource(VkImageAspectFlags.ColorBit), null);
            _depth = renderer.Allocator.CreateImage(ImageInfo("Pebbles Workshop depth", PreviewPipeline.DepthFormat, VkImageUsageFlags.DepthStencilAttachmentBit));
            _depthView = _depth.Value.CreateImageView(VkImageViewType._2D, Subresource(VkImageAspectFlags.DepthBit), null);
        }
        catch { Dispose(); throw; }
    }

    private ImageEx.CreateInfo ImageInfo(string name, VkFormat format, VkImageUsageFlags usage) => new()
    {
        Name = name, AllocationInfo = new AllocationInfo.CreateInfo { RequiredProperties = VkMemoryPropertyFlags.DeviceLocalBit },
        ImageType = VkImageType._2D, ImageFormat = format, ImageExtent = new VkExtent3D(Width, Height, 1),
        ImageMipLevels = 1, ImageArrayLayers = 1, ImageSamples = VkSampleCountFlags._1Bit,
        ImageTiling = VkImageTiling.Optimal, ImageUsage = usage, ImageSharingMode = VkSharingMode.Exclusive,
        ImageInitialLayout = VkImageLayout.Undefined
    };

    private static VkImageSubresourceRange Subresource(VkImageAspectFlags aspect) => new()
    {
        AspectMask = aspect, BaseMipLevel = 0, LevelCount = 1, BaseArrayLayer = 0, LayerCount = 1
    };

    internal void Render(PreviewPipeline pipeline, PreviewScene scene, Matrix4x4 viewProjection, Vector3 eye)
    {
        using var submission = new AssetUploadSubmission(_renderer);
        var command = submission.Command;
        ImageTransition[] transitions =
        [
            new(_colorImage!.Value.VkImage, _initialized ? ImageBarrierInfo.Presets.SampledReadF : ImageBarrierInfo.Presets.Undefined,
                ImageBarrierInfo.Presets.ColorAttachmentWrite),
            new(_depth!.Value.VkImage, _initialized ? ImageBarrierInfo.Presets.DepthStencilAttachmentWrite : ImageBarrierInfo.Presets.Undefined,
                ImageBarrierInfo.Presets.DepthStencilAttachmentWrite, ImageTransition.Subresource(VkImageAspectFlags.DepthBit))
        ];
        command.TransitionImages2(transitions);
        var color = new VkRenderingAttachmentInfo
        {
            ImageView = _colorView!.Value.VkImageView, ImageLayout = VkImageLayout.ColorAttachmentOptimal,
            LoadOp = VkAttachmentLoadOp.Clear, StoreOp = VkAttachmentStoreOp.Store,
            ClearValue = new VkClearValue { Color = new VkClearColorValue { Float32 = new float4(.055f, .068f, .086f, 1) } }
        };
        var depth = new VkRenderingAttachmentInfo
        {
            ImageView = _depthView!.Value.VkImageView, ImageLayout = VkImageLayout.DepthStencilAttachmentOptimal,
            LoadOp = VkAttachmentLoadOp.Clear, StoreOp = VkAttachmentStoreOp.DontCare,
            ClearValue = new VkClearValue { DepthStencil = new VkClearDepthStencilValue { Depth = 1, Stencil = 0 } }
        };
        var extent = new VkExtent2D(Width, Height);
        var rendering = new VkRenderingInfo
        {
            RenderArea = new VkRect2D(extent), LayerCount = 1, ColorAttachmentCount = 1,
            ColorAttachments = &color, DepthAttachment = &depth
        };
        command.BeginRendering(in rendering);
        try
        {
            var viewport = new VkViewport { Width = Width, Height = Height, MinDepth = 0, MaxDepth = 1 };
            var scissor = new VkRect2D(extent);
            command.SetViewport(0, new ReadOnlySpan<VkViewport>(ref viewport));
            command.SetScissor(0, new ReadOnlySpan<VkRect2D>(ref scissor));
            scene.Record(command, pipeline, viewProjection, eye);
        }
        finally { command.EndRendering(); }
        var sampled = new ImageTransition(_colorImage!.Value.VkImage,
            ImageBarrierInfo.Presets.ColorAttachmentWrite, ImageBarrierInfo.Presets.SampledReadF);
        command.TransitionImages2(new ReadOnlySpan<ImageTransition>(ref sampled));
        submission.SubmitAndWait();
        _initialized = true;
        if (Texture._TexID.Value == IntPtr.Zero)
            Texture = ImGuiBackend.Vulkan.AddTexture(pipeline.Sampler, _colorView!.Value.VkImageView);
    }

    public void Dispose()
    {
        if (Texture._TexID.Value != IntPtr.Zero) ImGuiBackend.Vulkan.RemoveTexture(Texture);
        Texture = default;
        _colorView?.Dispose(); _colorView = null;
        _colorImage?.Dispose(); _colorImage = null;
        _depthView?.Dispose(); _depthView = null;
        _depth?.Dispose(); _depth = null;
    }
}
