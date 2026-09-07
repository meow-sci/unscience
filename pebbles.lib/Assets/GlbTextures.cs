using System;
using System.Buffers.Binary;
using System.Reflection;
using Brutal.TextureApi;
using Brutal.VulkanApi;
using KSA;
using RenderCore;

namespace MeowSci.PebblesLib;

internal sealed partial record GlbPixels
{
    private const int MaxDimension = 4096;

    internal static GlbPixels Decode(byte[] encoded, string mime)
    {
        // Bound native decoder allocation from the encoded header before invoking stb.
        var (width, height) = Dimensions(encoded, mime);
        if (width < 1 || height < 1 || width > MaxDimension || height > MaxDimension)
            throw new InvalidOperationException("GLB image dimensions must be between 1 and 4096 pixels.");
        var format = mime == "image/png" ? TextureLoader.FormatType.Png : TextureLoader.FormatType.Jpg;
        var texture = TextureLoader.LoadFromMemory(encoded, format, Brutal.TextureApi.Stb.Loader.LoadSettings.ForceRgba8);
        try
        {
            int bytes = checked(width * height * 4);
            if (texture.Extent.X != width || texture.Extent.Y != height || texture.Data.Length != bytes)
                throw new InvalidOperationException("Decoded GLB image dimensions/format differ from its header.");
            return new(width, height, texture.Data.ToArray());
        }
        finally { TextureLoader.Unload(texture); }
    }

    private static (int Width, int Height) Dimensions(ReadOnlySpan<byte> bytes, string mime)
    {
        if (mime == "image/png")
        {
            ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
            if (bytes.Length < 33 || !bytes[..8].SequenceEqual(signature) || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
                throw new InvalidOperationException("Invalid embedded PNG header.");
            return (checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4))), checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4))));
        }
        if (mime != "image/jpeg") throw new NotSupportedException("Only embedded PNG and JPEG GLB images are supported.");
        if (bytes.Length < 4 || bytes[0] != 255 || bytes[1] != 216) throw new InvalidOperationException("Invalid embedded JPEG header.");
        int offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset++] != 255) throw new InvalidOperationException("Invalid JPEG marker.");
            while (offset < bytes.Length && bytes[offset] == 255) offset++;
            if (offset >= bytes.Length) break;
            int marker = bytes[offset++];
            if (marker is 216 or 1 || marker is >= 208 and <= 215) continue;
            if (marker is 217 or 218 || offset + 2 > bytes.Length) break;
            int length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (length < 2 || length > bytes.Length - offset) throw new InvalidOperationException("Invalid JPEG segment length.");
            if (marker is >= 192 and <= 207 && marker is not 196 and not 200 and not 204)
            {
                if (length < 8) throw new InvalidOperationException("Invalid JPEG frame header.");
                return (BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2)), BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2)));
            }
            offset += length;
        }
        throw new InvalidOperationException("JPEG image dimensions were not found.");
    }


}

/// <summary>Owns one private image and bindless slot; never registers in ModLibrary.</summary>
internal sealed class GlbTexture : TextureReference
{
    private static readonly PropertyInfo HandleProperty = typeof(TextureReference).GetProperty(nameof(BindlessHandle), BindingFlags.Public | BindingFlags.Instance)
        ?? throw new MissingMemberException(typeof(TextureReference).FullName, nameof(BindlessHandle));
    internal Core.Renderer Owner { get; private set; } = null!;
    private RenderCore.Systems.BindlessTextureLibrary? _bindless;
    private int _ownedHandle;
    private bool _released;

    internal static GlbTexture Upload(string id, GlbPixels pixels)
    {
        var renderer = Program.GetRenderer();
        var result = new GlbTexture { Owner = renderer, Id = id, Width = pixels.Width, Height = pixels.Height };
        result.SetHash();
        try
        {
            result.Texture = new SimpleVkTexture(id, renderer.Allocator, pixels.Width, pixels.Height, 1,
                VkFormat.R8G8B8A8UNorm, SimpleVkTexture.CalculateMaxMipLevels(pixels.Width, pixels.Height));
            result.ImageView = result.Texture.ImageView;
            using (var submission = new AssetUploadSubmission(renderer))
            {
                result.Texture.UploadData(submission.Staging, submission.Command, pixels.Data.AsSpan(), [pixels.Data.Length]);
                submission.SubmitAndWait();
            }
            result._bindless = Program.Instance.BindlessTextures;
            result._ownedHandle = result._bindless.AddTexture(result.ImageView);
            HandleProperty.SetValue(result, result._ownedHandle);
            return result;
        }
        catch { result.Release(); throw; }
    }

    internal void Release()
    {
        if (_released) return;
        _released = true;
        if (_ownedHandle != 0) _bindless!.FreeTexture(_ownedHandle);
        _ownedHandle = 0;
        Texture?.Dispose();
    }
}
