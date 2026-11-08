using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MeowSci.GraffitiLib;
using MeowSci.Unscience;

internal static class ShutdownChecks
{
    internal static void Run()
    {
        KsaAssemblyProbe.VerifyMirror(typeof(KSA.Program).GetMethod(nameof(IDisposable.Dispose))!, Require);
        var harmony = new Harmony("ksa-upgrade.tests.shutdown");
        KSA.Program.Renderer = new(); KSA.Program.RendererReads = 0;
        KSA.LifecycleTrace.Events.Clear();
        var unused = new GraffitiSubmod(false, false);
        unused.Dispose();
        Require(KSA.Program.RendererReads == 0, "unused Graffiti cleanup never obtains or touches Vulkan");
        var texturesOnly = new GraffitiSubmod(false, true);
        texturesOnly.Dispose();
        Require(KSA.LifecycleTrace.Events.SequenceEqual(new[] { "queue idle", "textures dispose" }),
            "textures uploaded without a pipeline are drained and released");
        KSA.LifecycleTrace.Events.Clear();
        var active = new GraffitiSubmod(true, true);
        int callbacks = 0;
        GameShutdownPatches.Apply(harmony, () =>
        {
            callbacks++; active.Dispose();
            GameShutdownPatches.Remove(harmony); // Production Patcher.Unload removes itself here.
        });
        try
        {
            new KSA.Program().Dispose();
            Require(KSA.LifecycleTrace.Events.SequenceEqual(new[]
                { "workers idle", "device idle", "queue idle", "pipeline dispose", "textures dispose", "native dispose" }),
                "actual Harmony prefix disposes mod GPU state before native device destruction");
            int reads = KSA.Program.RendererReads;
            active.Dispose(); // Late StarMap unload used to crash here, after device disposal.
            Require(callbacks == 1 && KSA.Program.RendererReads == reads,
                "late repeated resource cleanup cannot wait on a destroyed device");
            KSA.Program.Renderer = new();
            KSA.LifecycleTrace.Events.Clear();
            GameShutdownPatches.Apply(harmony, () => throw new InvalidOperationException("cleanup failed"));
            new KSA.Program().Dispose();
            Require(KSA.LifecycleTrace.Events.Last() == "native dispose", "cleanup failure does not prevent native teardown");
            KSA.Program.Renderer = new(); KSA.LifecycleTrace.Events.Clear();
            GameShutdownPatches.Remove(harmony);
            new KSA.Program().Dispose();
            Require(KSA.LifecycleTrace.Events.SequenceEqual(new[] { "native dispose" }), "removal leaves native disposal intact");
        }
        finally { GameShutdownPatches.Remove(harmony); KSA.Program.Renderer = new(); }
        Console.WriteLine("PASS: pre-renderer cleanup, native Dispose signature, unused/textures-only Graffiti and late-unload safety");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

namespace KSA
{
    public static class LifecycleTrace { public static readonly List<string> Events = new(); }
    public static class JobSystems { public static void WaitAll() => LifecycleTrace.Events.Add("workers idle"); }
    public sealed class LifecycleRenderer
    {
        public bool Disposed;
        public LifecycleDevice Device => new(this);
        public LifecycleQueue GraphicsAndCompute => new(this);
    }
    public sealed class LifecycleDevice(LifecycleRenderer renderer)
    {
        public void WaitIdle()
        { if (renderer.Disposed) throw new InvalidOperationException("dead device"); LifecycleTrace.Events.Add("device idle"); }
    }
    public sealed class LifecycleQueue(LifecycleRenderer renderer)
    {
        public void WaitIdle()
        { if (renderer.Disposed) throw new InvalidOperationException("dead queue"); LifecycleTrace.Events.Add("queue idle"); }
    }
}

namespace MeowSci.GraffitiLib
{
    public sealed class DecalEntry;
    public sealed class DecalRenderer
    { public void Dispose() => KSA.LifecycleTrace.Events.Add("pipeline dispose"); }
    public sealed class DecalTextureCache(bool owned)
    {
        public bool HasResources { get; private set; } = owned;
        public void DisposeAll() { KSA.LifecycleTrace.Events.Add("textures dispose"); HasResources = false; }
    }
    public sealed partial class GraffitiSubmod
    {
        private static bool _renderActive;
        private readonly List<DecalEntry> _decals = new();
        private DecalEntry[] _published = Array.Empty<DecalEntry>();
        private DecalRenderer? _renderer;
        private readonly DecalTextureCache _textures;
        public static GraffitiSubmod? Instance { get; private set; }
        public GraffitiSubmod(bool pipeline, bool textures)
        { _renderer = pipeline ? new() : null; _textures = new(textures); Instance = this; }
        public bool IsRenderActive => _renderActive;
        public int PublishedCount => _published.Length;
    }
}
