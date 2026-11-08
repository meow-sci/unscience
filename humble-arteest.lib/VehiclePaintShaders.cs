using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KSA;

namespace MeowSci.HumbleArteestLib;

/// <summary>
/// Runtime GLSL patching for the part fragment shaders.
///
/// KSA compiles the part shaders from disk on every pipeline build
/// (<c>ShaderReference.CompileVariantWithCustomOptions</c> → <c>ShaderModuleUtils.FromFile</c>),
/// once per feature variant, and destroys the module immediately afterwards. That makes the old
/// "swap <c>ShaderReference.Shader</c>" trick inert, so this class intercepts one level lower:
/// <see cref="VehiclePaintPatches"/> prefixes <c>ShaderModuleUtils.FromFile</c> and, for the part
/// fragment shaders, compiles a modified source string instead of the file on disk. The game's own
/// <c>CompileOptions</c> (macro defines + include callback) are passed straight through and the
/// original file path is handed to the compiler as the input file name, so <c>#include</c>
/// resolution and every <c>ENABLE_*</c> variant behave exactly as they do stock.
///
/// Nothing on disk is ever written or modified.
/// </summary>
public static class VehiclePaintShaders
{
    /// <summary>Fragment shaders that render vehicle parts and receive the paint snippet.</summary>
    private static readonly string[] TargetFileNames =
    {
        "MeshIndirect.frag",          // raster part pipeline (static + dynamic variants)
        "MeshIndirectRaytraced.frag", // IVA raytraced part pipeline
    };

    /// <summary>ShaderReference ids matching <see cref="TargetFileNames"/>, used for pre-flight checks.</summary>
    private static readonly string[] TargetShaderIds =
    {
        "MeshIndirectFrag",
        "MeshIndirectRaytracedFrag",
    };

    private static readonly Dictionary<string, byte[]> SourceCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True once the patched shaders are in force.</summary>
    public static bool Installed { get; private set; }

    /// <summary>Most recent install / transform failure, or null.</summary>
    public static string? LastError { get; private set; }

    /// <summary>Diagnostic: number of times a patched source was handed to the compiler.</summary>
    public static int CompileCount { get; private set; }

    private static bool _stockSourceDiagnostic;

    // ---- Lifecycle ----

    /// <summary>
    /// Verifies the shader on disk can still be patched, then arms the interception and asks the
    /// game to rebuild its renderer (which recompiles every part pipeline).
    /// </summary>
    public static bool Install()
    {
        if (Installed) return true;

        LastError = null;
        SourceCache.Clear();
        _stockSourceDiagnostic = string.Equals(Environment.GetEnvironmentVariable("UNSCIENCE_PAINT_SHADER_MODE"),
            "stock", StringComparison.OrdinalIgnoreCase);
        Console.WriteLine($"humble-arteest: paint activation mvid={typeof(VehiclePaint).Module.ModuleVersionId} "
            + $"assembly='{typeof(VehiclePaint).Assembly.Location}' source={(_stockSourceDiagnostic ? "STOCK DIAGNOSTIC" : "paint")} "
            + $"blend={VehiclePaint.BlendMode} shift={VehiclePaint.PaintBitShift} channels={VehiclePaint.ChannelBits} "
                + $"global={VehiclePaint.GlobalEnabled} parts={VehiclePaint.PaintedPartCount} templates={VehiclePaint.PaintedTemplates.Count}");
        VehiclePaintPatches.LogDiagnostics();

        // The raster shader is required; the raytraced one is optional (it only matters in IVA with
        // raytracing on), so a failure there is logged but does not block painting.
        for (int i = 0; i < TargetShaderIds.Length; i++)
        {
            bool required = i == 0;
            var path = TryResolveShaderPath(TargetShaderIds[i]);

            if (path != null && BuildPatchedSource(path) != null)
                continue;

            LastError ??= $"Could not locate {TargetFileNames[i]} — is this a supported KSA build?";
            Console.WriteLine($"humble-arteest: {LastError}");
            if (required) return false;
            LastError = null;
        }

        Installed = true;
        RequestRendererRebuild();
        Console.WriteLine("humble-arteest: paint shaders armed; renderer rebuild requested.");
        return true;
    }

    /// <summary>Disarms the interception and rebuilds back to the stock shaders.</summary>
    public static void Uninstall()
    {
        if (!Installed) return;
        Installed = false;
        SourceCache.Clear();
        RequestRendererRebuild();
        Console.WriteLine("humble-arteest: paint shaders removed; renderer rebuild requested.");
    }

    /// <summary>Invalidates the cached GLSL after the blend mode changed.</summary>
    internal static void OnBlendModeChanged()
    {
        SourceCache.Clear();
        if (Installed) RequestRendererRebuild();
    }

    /// <summary>
    /// Schedules a full renderer rebuild on the next frame boundary. This is the game's own
    /// mechanism for shader-variant changes (it is what toggling Frost/Water in the graphics
    /// settings does), so it happens at a point where destroying pipelines is safe.
    /// </summary>
    private static void RequestRendererRebuild() => Program.RendererRebuildNeeded = true;

    // ---- Interception ----

    /// <summary>True when the given compile target is a part fragment shader we patch.</summary>
    internal static bool IsTarget(string? filePath)
    {
        if (!Installed || string.IsNullOrEmpty(filePath)) return false;
        var name = Path.GetFileName(filePath);
        foreach (var target in TargetFileNames)
        {
            if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns the patched UTF-8 GLSL for a target shader, or null if it should be compiled
    /// unmodified (which is also the safe fallback whenever anything goes wrong).
    /// </summary>
    internal static byte[]? TryGetPatchedSource(string filePath)
    {
        if (!IsTarget(filePath)) return null;
        if (SourceCache.TryGetValue(filePath, out var cached)) return cached;

        var patched = BuildPatchedSource(filePath);
        if (patched == null) return null;

        SourceCache[filePath] = patched;
        Console.WriteLine($"humble-arteest: paint compile source '{filePath}', {patched.Length} bytes, "
            + (_stockSourceDiagnostic ? "untouched stock through FromString" : $"injected {VehiclePaint.BlendMode}"));
        return patched;
    }

    /// <summary>Counts a successful patched compile, for the UI's diagnostics line.</summary>
    internal static void NoteCompiled() => CompileCount++;

    /// <summary>Records a compile failure and falls back to the stock shader.</summary>
    internal static void NoteCompileFailed(string filePath, Exception ex)
    {
        LastError = $"Patched {Path.GetFileName(filePath)} failed to compile: {ex.Message}";
        Console.WriteLine($"humble-arteest: {LastError}");
        // Drop the cache entry so a later attempt can retry rather than replay a bad source.
        SourceCache.Remove(filePath);
    }

    // ---- GLSL transform ----

    private static byte[]? BuildPatchedSource(string filePath)
    {
        try
        {
            var source = File.ReadAllText(filePath);
            var patched = VehiclePaintShaderSource.Prepare(source, _stockSourceDiagnostic, out var error);
            if (patched == null)
            {
                LastError = $"{Path.GetFileName(filePath)}: {error}";
                return null;
            }
            return new UTF8Encoding(false).GetBytes(patched);
        }
        catch (Exception ex)
        {
            LastError = $"Could not read {Path.GetFileName(filePath)}: {ex.Message}";
            return null;
        }
    }

    /// <summary>
    /// Inserts the paint snippet immediately after the albedo sample, so the painted color flows
    /// through thin film, frost, and the whole PBR evaluation exactly like the texture would.
    /// Anchors on the <c>sampledColor</c> declaration rather than an exact line, so incidental
    /// upstream edits do not break it.
    /// </summary>
    // ---- Helpers ----

    private static string? TryResolveShaderPath(string shaderId)
    {
        try
        {
            var reference = ModLibrary.Get<ShaderReference>(shaderId);
            var path = reference?.ModPath;
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"humble-arteest: could not resolve shader '{shaderId}': {ex.Message}");
            return null;
        }
    }
}
