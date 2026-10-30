using System;
using System.Collections.Generic;
using System.IO;
using Brutal.TextureApi;
using Brutal.VulkanApi;
using Brutal.VulkanApi.Abstractions;
using KSA;
using MeowSci.KsaAbstractions;
using RenderCore;

namespace MeowSci.GraffitiLib;

/// <summary>
/// The image half of graffiti: shared PNG file name → a graffiti-owned GPU image occupying one slot of
/// KSA's bindless texture table, so the decal shader can address it with a single uint push
/// constant. Game thread only.
/// </summary>
/// <remarks>
/// <para><b>Uploads.</b> PNGs are decoded with the game's own <c>TextureLoader</c> (stb forced to
/// 4 channels via <c>R8G8B8A8UNorm</c> — a 3-channel PNG would otherwise decode to the widely
/// unsupported R8G8B8), wrapped into a <c>SimpleVkTexture</c> with a full mip chain, and given a
/// slot with <c>BindlessTextureLibrary.AddTexture</c>. The table's layout is
/// UpdateAfterBind|PartiallyBound, so writing a slot while other slots are in flight is legal.</para>
/// <para><b>Hot swap.</b> Entries are keyed by file name + last-write time: when the file on disk
/// changes, the next <see cref="Resolve"/> frees the old slot and uploads the new bytes.</para>
/// <para><b>Deferred destroy.</b> A freed slot is safe immediately (<c>FreeTexture</c> rewrites
/// its descriptor to the engine's empty texture), but the image itself may still be sampled by
/// frames already recorded, so it waits out MaxFramesInFlight + 1 ticks in the retire queue.</para>
/// </remarks>
internal sealed class DecalTextureCache
{
    /// <summary>Hard ceiling on a decal image's longest edge; larger sources are downscaled, not rejected.</summary>
    private const int MaxDimension = 2048;

    private sealed record Bound(SimpleVkTexture Image, int Handle, DateTime LastWriteUtc);

    private readonly Dictionary<string, Bound> _bound = new(StringComparer.OrdinalIgnoreCase);

    // Decodes that threw, keyed by name + the write time that failed, so a broken file is retried
    // exactly once per new version instead of on every resolve.
    private readonly Dictionary<string, DateTime> _failed = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<(SimpleVkTexture Image, int TicksRemaining)> _retiring = new();

    /// <summary>The last decode/upload/bind fault text; empty while healthy.</summary>
    internal string LastError { get; private set; } = "";

    /// <summary>
    /// The bindless slot for <paramref name="name"/>'s current on-disk bytes, or null when the
    /// file is missing or failed to decode. Uploads lazily on first reference.
    /// </summary>
    internal int? Resolve(string name, out DecalTextureState state)
    {
        var path = PngLibrary.FullPath(name);
        if (!File.Exists(path))
        {
            state = DecalTextureState.Missing;
            return null;
        }

        var lastWrite = File.GetLastWriteTimeUtc(path);
        if (_bound.TryGetValue(name, out var bound))
        {
            if (bound.LastWriteUtc == lastWrite)
            {
                state = DecalTextureState.Ready;
                return bound.Handle;
            }
            Release(name); // the file changed on disk — hot-swap to the new bytes
        }

        if (_failed.TryGetValue(name, out var failedWrite) && failedWrite == lastWrite)
        {
            state = DecalTextureState.Failed;
            return null;
        }

        if (Program.GetRenderer() is not { } renderer || Program.Instance?.BindlessTextures is null)
        {
            // Transient (too early in startup) — report missing without latching a failure.
            state = DecalTextureState.Missing;
            return null;
        }

        try
        {
            var live = Bind(renderer, name, path, lastWrite);
            _bound[name] = live;
            _failed.Remove(name);
            LastError = "";
            state = DecalTextureState.Ready;
            return live.Handle;
        }
        catch (Exception ex)
        {
            _failed[name] = lastWrite;
            LastError = ex.Message;
            Console.WriteLine($"graffiti: decal image '{name}' failed to load: {ex.Message}");
            state = DecalTextureState.Failed;
            return null;
        }
    }

    /// <summary>
    /// Frees every image no placed decal references any more. Call after removals — a
    /// steady-state frame never walks this.
    /// </summary>
    internal void Reconcile(IReadOnlySet<string> referencedNames)
    {
        if (_bound.Count == 0)
            return;
        foreach (var name in new List<string>(_bound.Keys))
            if (!referencedNames.Contains(name))
                Release(name);
    }

    /// <summary>One tick: ages the retire queue and destroys whatever has outlived every frame in flight.</summary>
    internal void Drain()
    {
        for (var i = _retiring.Count - 1; i >= 0; i--)
        {
            var (image, ticks) = _retiring[i];
            if (--ticks > 0)
            {
                _retiring[i] = (image, ticks);
                continue;
            }
            _retiring.RemoveAt(i);
            DisposeImage(image);
        }
    }

    /// <summary>
    /// Frees every slot and destroys every image immediately. Only legal once the device is idle
    /// — the submod's teardown waits on the graphics queue first.
    /// </summary>
    internal void DisposeAll()
    {
        foreach (var name in new List<string>(_bound.Keys))
            Release(name);
        _failed.Clear();
        foreach (var (image, _) in _retiring)
            DisposeImage(image);
        _retiring.Clear();
    }

    /// <summary>Decodes one PNG into a graffiti-owned image and claims a bindless slot for it.</summary>
    private Bound Bind(Core.Renderer renderer, string name, string path, DateTime lastWrite)
    {
        if (Program.Instance?.BindlessTextures is not { } bindless)
            throw new InvalidOperationException("the bindless texture table is not available yet");

        var image = Upload(renderer, name, File.ReadAllBytes(path));
        var handle = -1;
        try
        {
            handle = bindless.AddTexture(image.VkImageView);
            return new Bound(image, handle, lastWrite);
        }
        catch
        {
            // Anything after AddTexture must give the slot back: retiring the image while a
            // claimed slot still points at it would leave the table sampling destroyed memory.
            if (handle >= 0)
                bindless.FreeTexture(handle);
            Retire(image);
            throw;
        }
    }

    /// <summary>
    /// Decodes PNG bytes and uploads them as a mip-mapped RGBA8 image — the exact settings pair
    /// KSA's own <c>TextureReference.DoLoad</c> uses for game assets.
    /// </summary>
    private SimpleVkTexture Upload(Core.Renderer renderer, string name, byte[] bytes)
    {
        var decoded = TextureLoader.LoadFromMemory(bytes, TextureLoader.FormatType.Png,
            TextureAsset.LoadOptions(VkFormat.R8G8B8A8UNorm, Brutal.KtxApi.KtxTranscodeFmt.Rgba32));
        try
        {
            // FilePath must be non-empty (the ctor throws otherwise) and names the Vulkan image.
            var asset = new TextureAsset(decoded, $"graffiti:decals/{name}");
            using var pool = renderer.Allocator.CreateStagingPool(renderer.Graphics, 1);
            var image = new SimpleVkTexture(renderer.Allocator, pool, asset,
                new SimpleVkTexture.CreateOptions(MaxDimension,
                    SimpleVkTexture.CreateOptions.ReductionMethod.Downsample, fillMipChain: true));
            pool.Submit().Wait();
            return image;
        }
        finally
        {
            DestroyDecoded(decoded);
        }
    }

    /// <summary>
    /// Returns one slot to the library and queues its image for destruction. The slot is safe the
    /// moment it is freed; the image is not, so it waits out every frame in flight.
    /// </summary>
    private void Release(string name)
    {
        if (!_bound.Remove(name, out var bound))
            return;
        try
        {
            Program.Instance?.BindlessTextures?.FreeTexture(bound.Handle);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"graffiti: bindless slot {bound.Handle} free failed: {ex.Message}");
        }
        Retire(bound.Image);
    }

    private void Retire(SimpleVkTexture image)
    {
        var frames = Program.GetRenderer()?.MaxFramesInFlight ?? 3;
        _retiring.Add((image, frames + 1));
    }

    /// <summary>
    /// Frees the decoded CPU-side image. <c>ITexture</c> is neither IDisposable nor finalized —
    /// only the concrete loaders expose a public Destroy() — so without this every upload leaks
    /// its native decode buffer.
    /// </summary>
    private static void DestroyDecoded(Brutal.TextureApi.ITexture texture)
    {
        try
        {
            switch (texture)
            {
                case Brutal.TextureApi.Stb.StbTexture stb: stb.Destroy(); break;
                case Brutal.TextureApi.Ktx.KtxTexture ktx: ktx.Destroy(); break;
                case Brutal.TextureApi.Gli.GliTexture gli: gli.Destroy(); break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"graffiti: decode cleanup failed: {ex.Message}");
        }
    }

    private static void DisposeImage(SimpleVkTexture image)
    {
        try
        {
            image.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"graffiti: decal image disposal failed: {ex.Message}");
        }
    }
}
