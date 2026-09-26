using System;
using System.Collections.Generic;
using Brutal.Numerics;
using KSA;

namespace MeowSci.HumbleArteestLib;

/// <summary>How a paint color is combined with a part's sampled albedo in the fragment shader.</summary>
public enum PaintBlendMode
{
    /// <summary>albedo *= paint. Keeps all texture detail; can only darken. Best for repainting light hulls.</summary>
    Multiply,

    /// <summary>albedo = paint * luminance(albedo) * 2. Keeps shading detail and lets a part become brighter.</summary>
    Tint,

    /// <summary>albedo = paint. Flat color; surface shape still comes from the normal/PBR maps.</summary>
    Replace,
}

/// <summary>
/// Per-part-instance paint for KSA vehicle parts.
///
/// The color travels to the GPU inside the <b>unused high bits of the per-instance
/// <c>StateBitFlag</c></b> (see <see cref="PaintBitShift"/>). The game only uses bits 0..10 of that
/// field, and the field exists at the same offset in every <c>PerInstanceData</c> variant
/// (static / dynamic / glass), so nothing else in the render pipeline has to change: no struct
/// stride change, no new descriptor binding, no clobbering of EmissiveColor / Temperature /
/// TfiThickness / Wetness.
///
/// The fragment shader reads those bits back out of the <c>inStateFlags</c> varying — which every
/// part fragment shader already receives — after a small snippet injected by
/// <see cref="VehiclePaintShaders"/>.
///
/// Resolution order for a part: explicit per-part color, then per-part-type (template id) color,
/// then the global "paint everything" color.
/// </summary>
public static class VehiclePaint
{
    /// <summary>First state-flag bit used to carry paint. The game uses bits 0..10.</summary>
    public const int PaintBitShift = 11;

    /// <summary>Bits per color channel (7:7:7 across the 21 free state-flag bits).</summary>
    public const int ChannelBits = 7;

    /// <summary>Largest quantized channel value.</summary>
    public const int ChannelMax = (1 << ChannelBits) - 1;

    private static readonly Dictionary<Part, PaintEntry> ByPart = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<string, PaintEntry> ByTemplate = new(StringComparer.Ordinal);

    private static PaintEntry _global = PaintEntry.From(new float3(1f, 0.25f, 0.2f));
    private static bool _globalEnabled;
    private static PaintBlendMode _blendMode = PaintBlendMode.Multiply;
    private static int _version;

    // ---- Feature state ----

    /// <summary>True when the patched part shaders are installed and paint can render.</summary>
    public static bool Active => VehiclePaintShaders.Installed;

    /// <summary>
    /// Changes whenever the resolved paint of any part may have changed. KSA 5482 caches part
    /// state flags, so <see cref="VehiclePaintPatches"/> compares this to invalidate them.
    /// </summary>
    internal static int RenderStateVersion => (_version << 1) | (VehiclePaintShaders.Installed ? 1 : 0);

    private static void Changed() => _version++;

    /// <summary>Last activation / shader error, if any.</summary>
    public static string? LastError => VehiclePaintShaders.LastError;

    /// <summary>
    /// Installs the patched part shaders and schedules the renderer rebuild that recompiles them.
    /// The visual change lands on the next frame.
    /// </summary>
    public static bool Enable() => VehiclePaintShaders.Install();

    /// <summary>Removes the patched shaders and schedules a rebuild back to the stock ones.</summary>
    public static void Disable() => VehiclePaintShaders.Uninstall();

    /// <summary>
    /// Blend operator baked into the injected GLSL. Changing it while active triggers a shader
    /// rebuild, so treat it as an occasional setting rather than a per-frame control.
    /// </summary>
    public static PaintBlendMode BlendMode
    {
        get => _blendMode;
        set
        {
            if (_blendMode == value) return;
            _blendMode = value;
            VehiclePaintShaders.OnBlendModeChanged();
        }
    }

    // ---- Global paint ----

    /// <summary>When true every unlisted part is painted with <see cref="GlobalColor"/>.</summary>
    public static bool GlobalEnabled
    {
        get => _globalEnabled;
        set
        {
            if (_globalEnabled == value) return;
            _globalEnabled = value;
            Changed();
        }
    }

    /// <summary>Fallback color used when <see cref="GlobalEnabled"/> is set.</summary>
    public static float3 GlobalColor
    {
        get => _global.Color;
        set
        {
            _global = PaintEntry.From(value);
            Changed();
        }
    }

    // ---- Per-part paint ----

    /// <summary>Paints one specific part instance.</summary>
    public static void SetPart(Part part, float3 color)
    {
        ByPart[part] = PaintEntry.From(color);
        Changed();
    }

    /// <summary>Removes the paint override for one part instance.</summary>
    public static void ClearPart(Part part)
    {
        if (ByPart.Remove(part)) Changed();
    }

    /// <summary>Gets the explicit per-part color, if one was set.</summary>
    public static bool TryGetPartColor(Part part, out float3 color)
    {
        if (ByPart.TryGetValue(part, out var entry)) { color = entry.Color; return true; }
        color = default;
        return false;
    }

    /// <summary>Number of individually painted part instances.</summary>
    public static int PaintedPartCount => ByPart.Count;

    // ---- Per-part-type paint ----

    /// <summary>Paints every instance of a part template (<c>Part.Id</c>).</summary>
    public static void SetTemplate(string templateId, float3 color)
    {
        if (string.IsNullOrEmpty(templateId)) return;
        ByTemplate[templateId] = PaintEntry.From(color);
        Changed();
    }

    /// <summary>Removes the paint override for a part template.</summary>
    public static void ClearTemplate(string templateId)
    {
        if (ByTemplate.Remove(templateId)) Changed();
    }

    /// <summary>Gets the per-part-type color, if one was set.</summary>
    public static bool TryGetTemplateColor(string templateId, out float3 color)
    {
        if (templateId != null && ByTemplate.TryGetValue(templateId, out var entry))
        {
            color = entry.Color;
            return true;
        }
        color = default;
        return false;
    }

    /// <summary>Template ids that currently carry a paint override.</summary>
    public static IReadOnlyCollection<string> PaintedTemplates => ByTemplate.Keys;

    // ---- Bulk operations ----

    /// <summary>Clears every per-part and per-part-type override and disables global paint.</summary>
    public static void ClearAllPaint()
    {
        ByPart.Clear();
        ByTemplate.Clear();
        _globalEnabled = false;
        Changed();
    }

    /// <summary>True when at least one paint source could apply.</summary>
    public static bool HasAnyPaint => _globalEnabled || ByPart.Count > 0 || ByTemplate.Count > 0;

    /// <summary>
    /// Drops paint entries whose parts no longer exist, so the registry does not keep dead part
    /// graphs alive. An empty live set is treated as "nothing enumerated yet" rather than
    /// "everything died", so a scene transition never silently wipes the player's paint.
    /// </summary>
    public static void PruneParts(ICollection<Part> livingParts)
    {
        if (ByPart.Count == 0 || livingParts.Count == 0) return;
        List<Part>? dead = null;
        foreach (var part in ByPart.Keys)
        {
            if (livingParts.Contains(part)) continue;
            (dead ??= new List<Part>()).Add(part);
        }
        if (dead == null) return;
        foreach (var part in dead)
            ByPart.Remove(part);
        Changed();
    }

    /// <summary>Uninstalls the shaders and drops all paint state. Call on mod unload.</summary>
    public static void Cleanup()
    {
        ClearAllPaint();
        VehiclePaintShaders.Uninstall();
    }

    // ---- Hot path (called when KSA rewrites a part's cached render state) ----

    /// <summary>
    /// Resolves the state-flag bits to OR into a part instance's <c>StateBitFlag</c>.
    /// Returns false when the part is unpainted.
    /// </summary>
    internal static bool TryGetPaintBits(Part part, out int bits)
    {
        bits = 0;
        if (!VehiclePaintShaders.Installed) return false;

        if (ByPart.Count > 0 && ByPart.TryGetValue(part, out var entry))
        {
            bits = entry.Bits;
            return true;
        }

        if (ByTemplate.Count > 0 && ByTemplate.TryGetValue(part.Id, out entry))
        {
            bits = entry.Bits;
            return true;
        }

        if (_globalEnabled)
        {
            bits = _global.Bits;
            return true;
        }

        return false;
    }

    // ---- Encoding ----

    /// <summary>
    /// Packs an sRGB color into the free state-flag bits as 7:7:7. Quantizing in sRGB (the shader
    /// converts to linear) keeps the steps perceptually even. Never encodes to zero, because zero
    /// is what the shader reads as "unpainted".
    /// </summary>
    public static int EncodeBits(float3 srgb)
    {
        uint r = Quantize(srgb.X);
        uint g = Quantize(srgb.Y);
        uint b = Quantize(srgb.Z);
        uint packed = (r << (ChannelBits * 2)) | (g << ChannelBits) | b;
        if (packed == 0u) packed = 1u;
        return unchecked((int)(packed << PaintBitShift));
    }

    private static uint Quantize(float channel)
    {
        if (float.IsNaN(channel)) return 0u;
        int quantized = (int)MathF.Round(Math.Clamp(channel, 0f, 1f) * ChannelMax);
        return (uint)Math.Clamp(quantized, 0, ChannelMax);
    }

    private readonly struct PaintEntry
    {
        public readonly float3 Color;
        public readonly int Bits;

        private PaintEntry(float3 color, int bits)
        {
            Color = color;
            Bits = bits;
        }

        public static PaintEntry From(float3 color) => new(color, EncodeBits(color));
    }
}
