using System;
using System.IO;
using KSA;
using Brutal.VulkanApi;
using MeowSci.KsaAbstractions;

namespace MeowSci.PebblesLib;

/// <summary>Owned PNG color/cutout maps using the same bounded decoder and uploads as GLB materials.</summary>
public sealed class ImportedPngTexture : IDisposable
{
    private readonly GlbTexture _color;
    private readonly GlbTexture? _opacity;
    public TextureReference Color => _color;
    public TextureReference? Opacity => _opacity;

    public ImportedPngTexture(string catalogName)
    {
        string path = PngLibrary.FullPath(catalogName);
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new IOException("PNG override exceeds 64 MiB.");
        var pixels = GlbPixels.Decode(File.ReadAllBytes(path), "image/png");
        string id = "unscience-png-override:" + Guid.NewGuid().ToString("N");
        _color = GlbTexture.Upload(id, pixels);
        try
        {
            bool hasAlpha = false;
            for (int i = 3; i < pixels.Data.Length; i += 4) if (pixels.Data[i] < 255) { hasAlpha = true; break; }
            if (hasAlpha) _opacity = GlbTexture.Upload(id + ":opacity", GlbPixels.Opacity(pixels, 1, .5f));
        }
        catch { _color.Owner.Device.WaitIdle(); _color.Release(); throw; }
    }
    public void Dispose()
    {
        _color.Owner.Device.WaitIdle();
        _opacity?.Release();
        _color.Release();
    }
}
