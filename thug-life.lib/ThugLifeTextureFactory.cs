using System;
using Brutal;
using Brutal.Pointers.Extensions;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using Core;
using KSA;
using RenderCore;

namespace MeowSci.ThugLifeLib;

/// <summary>
/// Builds the small thug-life sunglasses texture programmatically from
/// <see cref="ThugLifeTexturePattern"/>.
///
/// Produces an <c>R8G8B8A8UNorm</c> 2D texture so the <c>UnlitMeshFrag</c> shader's
/// <c>gammaToLinear()</c> decode is the only color transform applied to the texel —
/// matching the texture-format guidance in the ksa skill's quad.md doc.
/// </summary>
public sealed class ThugLifeTextureFactory : IDisposable
{
    public SimpleVkTexture Texture { get; }
    public VkSampler Sampler { get; }
    public VkImageView ImageView => Texture.VkImageView;

    private readonly DeviceEx _device;
    private bool _disposed;

    public ThugLifeTextureFactory(Renderer renderer)
    {
        _device = renderer.Device;

        Texture = new SimpleVkTexture(
            "thug-life",
            renderer.Allocator,
            ThugLifeTexturePattern.Width,
            ThugLifeTexturePattern.Height,
            depth: 1,
            VkFormat.R8G8B8A8UNorm,
            mipLevels: 1,
            arrayLayers: 1,
            cubeMap: false,
            flags: VkImageUsageFlags.TransferDstBit | VkImageUsageFlags.SampledBit);

        UploadPixels(renderer);
        Sampler = CreateSampler(_device);
    }

    private unsafe void UploadPixels(Renderer renderer)
    {
        byte[] pixels = BuildPixelBytes();

        using var stagingPool = renderer.Allocator.CreateStagingPool(renderer.Graphics, 1);
        var cmd = stagingPool.NextCommandBuffer();
        cmd.Begin(VkCommandBufferUsageFlags.OneTimeSubmitBit);

        var stagingBuffer = stagingPool.AddStagingBuffer(ByteSize.Of<byte>(pixels.Length));
        using (var mapped = stagingBuffer.Map())
            pixels.AsSpan().CopyTo(mapped.AsSpan());

        Span<int> mipSizes = stackalloc int[1];
        mipSizes[0] = pixels.Length;
        VkBuffer src = stagingBuffer.VkBuffer;
        VkUtils.UploadBufferToImage(cmd, in src, Texture.ImageEx.AllocationInfo, mipSizes);

        cmd.End();
        stagingPool.Submit().Wait();
    }

    private static byte[] BuildPixelBytes()
    {
        int w = ThugLifeTexturePattern.Width;
        int h = ThugLifeTexturePattern.Height;
        byte[] data = new byte[w * h * 4];

        for (int y = 0; y < h; y++)
        {
            string row = ThugLifeTexturePattern.Rows[y];
            for (int x = 0; x < w; x++)
            {
                int offset = (y * w + x) * 4;
                char c = row[x];
                switch (c)
                {
                    case '#':
                        data[offset + 0] = 0;
                        data[offset + 1] = 0;
                        data[offset + 2] = 0;
                        data[offset + 3] = 255;
                        break;
                    case 'W':
                        data[offset + 0] = 255;
                        data[offset + 1] = 255;
                        data[offset + 2] = 255;
                        data[offset + 3] = 255;
                        break;
                    default:
                        data[offset + 0] = 0;
                        data[offset + 1] = 0;
                        data[offset + 2] = 0;
                        data[offset + 3] = 0;
                        break;
                }
            }
        }

        return data;
    }

    private static unsafe VkSampler CreateSampler(DeviceEx device)
    {
        var info = new VkSamplerCreateInfo
        {
            MagFilter = VkFilter.Nearest,
            MinFilter = VkFilter.Nearest,
            MipmapMode = VkSamplerMipmapMode.Nearest,
            AddressModeU = VkSamplerAddressMode.ClampToEdge,
            AddressModeV = VkSamplerAddressMode.ClampToEdge,
            AddressModeW = VkSamplerAddressMode.ClampToEdge,
            MinLod = 0f,
            MaxLod = 0f,
            BorderColor = VkBorderColor.FloatTransparentBlack,
        };
        return device.CreateSampler(info, null);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _device.DestroySampler(Sampler, null); } catch { }
        try { Texture.Dispose(); } catch { }
    }
}
