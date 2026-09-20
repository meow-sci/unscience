using System;

namespace RabbitEars.Sources;

/// <summary>
/// Parses channel source specs:
/// card:&lt;style&gt;[:&lt;name&gt;] | video:&lt;path&gt; | image:&lt;path&gt; | lavfi:&lt;filter spec&gt; | camera:&lt;index&gt;.
/// </summary>
public static class SourceFactory
{
    public const string Help = "card:<bars|grid|hue|checker|steps|rays>[:name] | video:<path> | image:<path> | lavfi:<spec> | camera:<n>";

    /// <summary>A short channel name for a spec, without starting the source: the card's name, or the file / filter name.</summary>
    public static string DisplayName(string spec)
    {
        string[] parts = spec.Split(':', 3);
        if (parts.Length >= 2 && parts[0].Equals("card", StringComparison.OrdinalIgnoreCase))
            return parts.Length == 3 ? parts[2] : $"{parts[1].ToUpperInvariant()} TV";
        string rest = parts.Length >= 2 ? spec.Substring(parts[0].Length + 1) : spec;
        string file = System.IO.Path.GetFileName(rest);
        return file.Length > 0 && file.Length <= 40 ? file : rest.Substring(0, Math.Min(40, rest.Length));
    }

    /// <param name="channelNumber">Shown on cards (1-based).</param>
    /// <param name="caption">Small caption for cards (e.g. the carrier frequency).</param>
    /// <param name="paced">True for live muxing (sources free-run in real time); false for offline.</param>
    /// <param name="startOfDay">Clock shown by cards at frame 0; pass one value to every channel so their clocks agree.</param>
    public static IFrameSource Create(string spec, int channelNumber, string caption, bool paced, TimeSpan? startOfDay = null)
    {
        int colon = spec.IndexOf(':');
        if (colon <= 0) throw new ArgumentException($"bad source '{spec}': expected {Help}");
        string kind = spec.Substring(0, colon).ToLowerInvariant(), rest = spec.Substring(colon + 1);
        switch (kind)
        {
            case "card":
                int nameAt = rest.IndexOf(':');
                string style = nameAt < 0 ? rest : rest.Substring(0, nameAt);
                string name = nameAt < 0 ? $"{style.ToUpperInvariant()} TV" : rest.Substring(nameAt + 1);
                return new CardSource(style, channelNumber, name, caption, startOfDay);
            case "video": return new FfmpegSource(FfmpegInputKind.File, rest, paced);
            case "image": return new FfmpegSource(FfmpegInputKind.Image, rest, paced);
            case "lavfi": return new FfmpegSource(FfmpegInputKind.Lavfi, rest, paced, "lavfi:" + rest);
            case "camera": return new FfmpegSource(FfmpegInputKind.Camera, rest, paced: true);
            default: throw new ArgumentException($"bad source kind '{kind}': expected {Help}");
        }
    }
}
