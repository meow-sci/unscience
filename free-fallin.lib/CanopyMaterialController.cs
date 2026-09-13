using System;
using System.IO;
using Brutal.Numerics;
using Brutal.TextureApi;
using Brutal.TextureApi.Abstractions;
using Brutal.VulkanApi;
using Core;
using KSA;
using MeowSci.KsaAbstractions;
using MeowSci.KsaAbstractions.Persistence;
using RenderCore;

namespace MeowSci.FreeFallinLib;

internal static class CanopyMaterialController
{
    private const string StockMaterialId = "ParachuteCanopy_Material";
    private const string CanopyGlbId = "ParachuteCanopyGlb";
    private static int _revision;
    private static CanopyGpuAssets? _owned;
    private static readonly System.Collections.Generic.List<CanopyGpuAssets> Retired = new();
    internal static CanopyMaterialSettings? AppliedSettings { get; private set; }

    internal static int CurrentMaterialHandle { get; private set; } = -1;
    internal static bool Enabled => CurrentMaterialHandle >= 0;

    internal static float4? CaptureEffectiveAlbedo() => AppliedSettings is { } settings && Enabled
        ? MaterialColorState.GetOrDefault(CurrentMaterialHandle, AuthoredTint(settings)) : null;

    internal static void Apply(CanopyMaterialSettings settings, float4? effectiveAlbedo = null)
    {
        settings = MeowSci.KsaAbstractions.Persistence.SaveJson.FromElement<CanopyMaterialSettings>(
            MeowSci.KsaAbstractions.Persistence.SaveJson.ToElement(settings));
        using var next = new CanopyGpuAssets();
        ApplyCore(settings, next, effectiveAlbedo);
        var previous = _owned;
        _owned = next;
        next.Committed = true;
        if (previous != null) Retired.Add(previous);
        ReleaseRetired();
    }

    private static void ApplyCore(CanopyMaterialSettings settings, CanopyGpuAssets owned, float4? effectiveAlbedo)
    {
        SuperMeshRenderSystem renderSystem = Program.Instance?.SuperMeshRenderSystem
            ?? throw new InvalidOperationException("The KSA render system is not ready yet.");
        PbrMaterialReference stock = ModLibrary.Get<PbrMaterialReference>(StockMaterialId).Get();
        GpuTextureSystem textures = renderSystem.TextureSystem;

        float4 projectionData = ProjectionData(settings, renderSystem);
        TextureBinding albedo = ResolveAlbedo(settings, stock, textures, owned);
        int pbrHandle;
        float4 pbrScale;
        if (settings.UseStockPbrMap)
        {
            pbrHandle = stock.PBRMap?.Get().BindlessHandle ?? renderSystem.GltfSystemSkinned.BlankMaterialTexture.BindlessHandle;
            pbrScale = new float4(settings.AmbientOcclusion, settings.Roughness, settings.Metallic, 1f);
        }
        else
        {
            pbrHandle = UploadSolidPbr(textures, settings.AmbientOcclusion, settings.Roughness, settings.Metallic, owned).BindlessHandle;
            pbrScale = float4.One;
        }

        var tint = effectiveAlbedo ?? AuthoredTint(settings);

        var material = new MaterialData
        {
            AlbedoTexture = albedo.Handle,
            Sampler = albedo.Sampler,
            AlbedoColor = tint,
            NormalTexture = stock.NormalReference?.Get().BindlessHandle
                            ?? renderSystem.GltfSystemSkinned.BlankNormalTexture.BindlessHandle,
            RoughMetallicAOTexture = pbrHandle,
            RoughnessMetalScale = pbrScale,
            ExtraData = projectionData,
            EmissiveTexture = stock.EmissiveMap?.Get().BindlessHandle ?? textures.DefaultBlackTexture.BindlessHandle
        };

        AssetName materialName = $"free-fallin/material/{++_revision}";
        if (!renderSystem.MaterialSystem.CreateObject(materialName, material))
            throw new InvalidOperationException("Could not allocate a custom canopy material.");
        var materialAsset = renderSystem.MaterialSystem.GetOrLoad(materialName);
        int handle = materialAsset.Handle;
        owned.Own(renderSystem.MaterialSystem, materialAsset, () => MaterialColorState.Forget(handle));
        MaterialColorState.Record(handle, tint);
        FreeFallinPatches.ReplaceObserved(handle);
        CurrentMaterialHandle = handle;
        AppliedSettings = settings;
        Console.WriteLine($"free-fallin: applied global canopy material ({settings.TextureMode}, material {CurrentMaterialHandle})");
    }

    private static float4 AuthoredTint(CanopyMaterialSettings settings)
    {
        var tint = settings.Tint;
        tint.X *= settings.Brightness;
        tint.Y *= settings.Brightness;
        tint.Z *= settings.Brightness;
        tint.W = 1f;
        return tint;
    }

    internal static int ResolveStockHandle()
    {
        try { return Program.Instance?.SuperMeshRenderSystem?.MaterialSystem.GetOrLoad(StockMaterialId).Handle ?? -1; }
        catch { return -1; }
    }

    internal static void Disable()
    {
        CurrentMaterialHandle = -1;
        AppliedSettings = null;
        _owned?.Release();
        _owned = null;
        ReleaseRetired();
        Console.WriteLine("free-fallin: restored stock canopy material");
    }

    private static void ReleaseRetired()
    {
        for (int i = Retired.Count - 1; i >= 0; i--) { Retired[i].Release(); Retired.RemoveAt(i); }
    }

    private readonly record struct TextureBinding(int Handle, int Sampler);

    private static TextureBinding ResolveAlbedo(CanopyMaterialSettings settings,
        PbrMaterialReference stock, GpuTextureSystem textures, CanopyGpuAssets owned)
    {
        if (settings.TextureMode == CanopyTextureMode.Stock)
        {
            TextureReference stockAlbedo = stock.DiffuseReference?.Get()
                ?? throw new InvalidOperationException("The stock canopy material has no diffuse texture.");
            return new TextureBinding(stockAlbedo.BindlessHandle, textures.SamplerRepeatHandle);
        }
        if (string.IsNullOrWhiteSpace(settings.TextureName))
            throw new InvalidOperationException("Choose a PNG before applying this texture mode.");

        string path = PngLibrary.FullPath(settings.TextureName);
        if (!File.Exists(path)) throw new FileNotFoundException("The selected PNG no longer exists.", path);
        GenericTexture generated = settings.TextureMode is CanopyTextureMode.Replace or CanopyTextureMode.FullCanopy
            ? LoadReplacement(path)
            : ComposeCenteredDecal(stock, path, settings.DecalScale);
        try
        {
            GpuTextureAssetRef uploaded = Upload(textures, generated, "albedo", owned);
            return new TextureBinding(uploaded.BindlessHandle, uploaded.SamplerHandle);
        }
        finally { generated.Destroy(); }
    }

    private static float4 ProjectionData(CanopyMaterialSettings settings, SuperMeshRenderSystem renderSystem)
    {
        if (settings.TextureMode != CanopyTextureMode.FullCanopy) return float4.Zero;
        CanopyProjectionShaders.RequireAvailable();

        SkeletonAssetRef? skeleton = renderSystem.GltfSystemSkinned.GetOrLoad(CanopyGlbId).Skeleton;
        float radius = skeleton == null ? 0f : ChuteCanopyBones.MeasureBindHemRadius(skeleton.SkeletonData);
        if (!(radius > 0f) || !float.IsFinite(radius))
            throw new InvalidOperationException("Could not measure the stock canopy bind-pose radius for full-canopy projection.");

        float radians = settings.FullCanopyRotationDegrees * (MathF.PI / 180f);
        return new float4(0.5f / radius, MathF.Cos(radians), MathF.Sin(radians),
            CanopyProjectionShaders.MaterialMarker);
    }

    private static GpuTextureAssetRef UploadSolidPbr(GpuTextureSystem textures, float ao, float roughness, float metallic, CanopyGpuAssets owned)
    {
        GenericTexture texture = GenericTexture.Defaults.RGBA8UNorm(new int2(1, 1));
        Span<byte> data = texture.Data;
        data[0] = ToByte(ao); data[1] = ToByte(roughness); data[2] = ToByte(metallic); data[3] = 255;
        try { return Upload(textures, texture, "pbr", owned); }
        finally { texture.Destroy(); }
    }

    private static GpuTextureAssetRef Upload(GpuTextureSystem textures, GenericTexture cpuTexture, string kind, CanopyGpuAssets owned)
    {
        AssetName name = $"free-fallin/{kind}/{++_revision}";
        using var asset = new TextureAsset(cpuTexture, name.ToString());
        if (!textures.TryAddTexture(name, asset)) throw new InvalidOperationException($"Could not upload the canopy {kind} texture.");
        return owned.Own(textures, textures.GetOrLoad(name));
    }

    private static GenericTexture LoadReplacement(string path)
    {
        Brutal.TextureApi.ITexture decoded = TextureLoader.LoadFromMemory(File.ReadAllBytes(path), TextureLoader.FormatType.Png,
            TextureAsset.LoadOptions(VkFormat.R8G8B8A8UNorm, Brutal.KtxApi.KtxTranscodeFmt.Rgba32));
        try
        {
            int width = decoded.Extent.X;
            int height = decoded.Extent.Y;
            var output = GenericTexture.Defaults.RGBA8UNorm(new int2(width, height));
            decoded.ImageData(0, 0, 0).CopyTo(output.Data);
            return output;
        }
        finally { DestroyDecoded(decoded); }
    }

    private static GenericTexture ComposeCenteredDecal(PbrMaterialReference stock, string path, float scale)
    {
        TextureReference stockDiffuse = stock.DiffuseReference?.Get()
            ?? throw new InvalidOperationException("The stock canopy diffuse texture is unavailable.");
        Brutal.TextureApi.ITexture source = stockDiffuse.TextureAsset.Texture;
        Brutal.TextureApi.ITexture? decodedStock = null;

        // KSA normally keeps vessel textures GPU-ready (BC7 on the 5402 build). CPU compositing
        // needs texels, so reopen the original KTX2 and explicitly ask Basis/KTX to transcode it
        // to RGBA32 instead of trying to interpret the runtime BC7 blocks as pixels.
        if (!IsRgba8(source.Format))
        {
            try
            {
                decodedStock = TextureLoader.LoadFromFile(stockDiffuse.ModPath,
                    TextureAsset.LoadOptions(VkFormat.R8G8B8A8UNorm, Brutal.KtxApi.KtxTranscodeFmt.Rgba32));
                source = decodedStock;
            }
            catch (Exception ex)
            {
                // A native-BC7 KTX2 cannot be transcoded by libktx. The decal feature remains
                // useful on a flat tintable canopy, so degrade visually instead of rejecting it.
                Console.WriteLine($"free-fallin: stock canopy RGBA transcode failed; using a flat decal base: {ex.Message}");
            }
        }

        Brutal.TextureApi.ITexture? decal = null;
        try
        {
            decal = TextureLoader.LoadFromMemory(File.ReadAllBytes(path), TextureLoader.FormatType.Png,
                TextureAsset.LoadOptions(VkFormat.R8G8B8A8UNorm, Brutal.KtxApi.KtxTranscodeFmt.Rgba32));
            int width = source.Extent.X;
            int height = source.Extent.Y;
            var output = GenericTexture.Defaults.RGBA8UNorm(new int2(width, height));
            if (IsRgba8(source.Format))
                CopyRows(source, output, width, height);
            else
            {
                output.Data.Fill(255);
                Console.WriteLine($"free-fallin: stock canopy is {source.Format}; centered decal will use a flat tintable base");
            }

            scale = Math.Clamp(scale, 0.05f, 1f);
            float fit = Math.Min(width * scale / decal.Extent.X, height * scale / decal.Extent.Y);
            int drawWidth = Math.Max(1, (int)MathF.Round(decal.Extent.X * fit));
            int drawHeight = Math.Max(1, (int)MathF.Round(decal.Extent.Y * fit));
            int left = (width - drawWidth) / 2;
            int top = (height - drawHeight) / 2;
            Span<byte> src = decal.ImageData(0, 0, 0);
            Span<byte> dst = output.Data;
            int srcPitch = (int)decal.RowPitch(0);
            int dstPitch = width * 4;
            for (int y = 0; y < drawHeight; y++)
            for (int x = 0; x < drawWidth; x++)
            {
                int sx = Math.Min(decal.Extent.X - 1, x * decal.Extent.X / drawWidth);
                int sy = Math.Min(decal.Extent.Y - 1, y * decal.Extent.Y / drawHeight);
                int si = sy * srcPitch + sx * 4;
                int di = (top + y) * dstPitch + (left + x) * 4;
                float alpha = src[si + 3] / 255f;
                for (int c = 0; c < 3; c++) dst[di + c] = (byte)Math.Clamp((int)MathF.Round(src[si + c] * alpha + dst[di + c] * (1f - alpha)), 0, 255);
                // Keep the stock alpha: a transparent decal must not cut holes in the canopy.
            }
            return output;
        }
        finally
        {
            DestroyDecoded(decal);
            DestroyDecoded(decodedStock);
        }
    }

    private static void CopyRows(Brutal.TextureApi.ITexture source, GenericTexture destination, int width, int height)
    {
        Span<byte> src = source.ImageData(0, 0, 0);
        Span<byte> dst = destination.Data;
        int srcPitch = (int)source.RowPitch(0);
        int rowBytes = width * 4;
        for (int y = 0; y < height; y++) src.Slice(y * srcPitch, rowBytes).CopyTo(dst.Slice(y * rowBytes, rowBytes));
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f), 0, 255);

    private static bool IsRgba8(TextureFormat format)
        => format is TextureFormat.R8G8B8A8_UNorm or TextureFormat.R8G8B8A8_SRGB;

    private static void DestroyDecoded(Brutal.TextureApi.ITexture? texture)
    {
        switch (texture)
        {
            case Brutal.TextureApi.Stb.StbTexture stb: stb.Destroy(); break;
            case Brutal.TextureApi.Ktx.KtxTexture ktx: ktx.Destroy(); break;
            case Brutal.TextureApi.Gli.GliTexture gli: gli.Destroy(); break;
        }
    }
}
