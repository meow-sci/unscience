using System;
using System.Text;

namespace MeowSci.HumbleArteestLib;

/// <summary>Pure GLSL transform, shared by runtime compilation and managed live-source checks.</summary>
internal static class VehiclePaintShaderSource
{
    // Diagnostic-only: exercise the same intercepted compiler path without changing shader text.
    internal static string? Prepare(string source, bool stockSource, out string? error)
    {
        if (!stockSource) return Inject(source, out error);
        error = null;
        return source;
    }

    internal static string? Inject(string source, out string? error)
    {
        error = null;
        var normalized = source.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        int anchor = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (!trimmed.StartsWith("vec3 sampledColor", StringComparison.Ordinal)) continue;
            if (!trimmed.EndsWith(";", StringComparison.Ordinal)) continue;
            anchor = i;
            break;
        }

        if (anchor < 0)
        {
            error = "the 'vec3 sampledColor = ...;' albedo anchor was not found — the shader changed shape.";
            return null;
        }

        if (!normalized.Contains("inStateFlags"))
        {
            error = "the shader no longer declares the 'inStateFlags' varying that carries paint.";
            return null;
        }

        var builder = new StringBuilder(normalized.Length + 1024);
        for (int i = 0; i < lines.Length; i++)
        {
            builder.Append(lines[i]).Append('\n');
            if (i == anchor) builder.Append(BuildSnippet());
        }
        return builder.ToString();
    }

    private static string BuildSnippet()
    {
        var apply = VehiclePaint.BlendMode switch
        {
            PaintBlendMode.Tint =>
                "            sampledColor = hbPaintColor * (dot(sampledColor, vec3(0.2126, 0.7152, 0.0722)) * 2.0);",
            PaintBlendMode.Replace =>
                "            sampledColor = hbPaintColor;",
            _ =>
                "            sampledColor *= hbPaintColor;",
        };

        return
            "\n" +
            "    // --- humble-arteest: per-instance paint, packed into state-flag bits " +
            VehiclePaint.PaintBitShift + "..30 ---\n" +
            "    {\n" +
            "        uint hbPaintPacked = inStateFlags >> " + VehiclePaint.PaintBitShift + "u;\n" +
            "        if (hbPaintPacked != 0u)\n" +
            "        {\n" +
            "            vec3 hbPaintColor = gammaToLinear(vec3(\n" +
            "                float((hbPaintPacked >> " + (VehiclePaint.ChannelBits * 2) + "u) & " + HexMask + "u),\n" +
            "                float((hbPaintPacked >> " + VehiclePaint.ChannelBits + "u) & " + HexMask + "u),\n" +
            "                float( hbPaintPacked        & " + HexMask + "u)) * (1.0 / " +
            VehiclePaint.ChannelMax + ".0));\n" +
            apply + "\n" +
            "        }\n" +
            "    }\n";
    }

    private static string HexMask => "0x" + VehiclePaint.ChannelMax.ToString("X");

}
