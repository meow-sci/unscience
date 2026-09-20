using System;
using System.Collections.Generic;
using System.Linq;
using RabbitEars.Pal;

namespace RabbitEars.Tv;

/// <summary>
/// Per-channel "what is transmitted" controls: fields of <see cref="PalSignalParams"/>. They act inside that
/// channel's encoder, before modulation, and take effect on the next line without touching the decoder.
/// </summary>
public static class SignalKnobs
{
    private const string Picture = "Transmitted picture", Link = "Studio link";

    public static readonly IReadOnlyList<Knob> All = new[]
    {
        Knob.Slider("pedestal", "Black level / brightness (V)", Picture, "Lifts or sinks the picture against blanking at the source: what a " +
            "brightness control looks like, since the set restores black from the back porch.", null, -0.15, 0.3, 0.005, 0),
        Knob.Slider("chroma_gain", "Source saturation", Picture, "Chroma amplitude relative to the burst. The set's ACC normalises by burst, " +
            "so this really changes saturation.", null, 0, 2.5, 0.05, 1),
        Knob.Slider("chroma_phase", "Chroma phase error (deg)", Picture, "Rotates the chroma against the burst. PAL's trick: with a comb the " +
            "error cancels into mild desaturation; set comb mode to 'off' and it becomes Hanover bars.", null, -60, 60, 1, 0),
        Knob.Slider("burst_gain", "Burst amplitude", Picture, "Scales the colour burst. Low burst over-saturates (ACC) until the colour " +
            "killer gives up and the picture goes monochrome; 0 = no burst.", null, 0, 2, 0.05, 1),
        Knob.Slider("level", "Video level", Link, "Gain of the whole composite signal before the modulator, sync included: it changes the " +
            "modulation depth, not the carrier (use the channel level for that).", null, 0.2, 1.3, 0.01, 1),
        Knob.Slider("noise", "Video noise (V rms)", Link, "White noise added to the composite video before modulation (a noisy studio " +
            "link). Receiver noise is a separate control and behaves differently.", null, 0, 0.06, 0.001, 0),
        Knob.Slider("ghost", "Ghost", Link, "A delayed positive echo of the video.", null, 0, 0.5, 0.01, 0),
        Knob.Slider("ghost_us", "Ghost delay (us)", Link, "Echo delay; 1 us is about 1/52 of the picture width.", null, 0.3, 6, 0.1, 1.6),
        Knob.Slider("hum", "Mains hum (V)", Link, "50 Hz hum riding on the video, rolling slowly.", null, 0, 0.3, 0.005, 0),
    };

    public static readonly IReadOnlyDictionary<string, Knob> ByName = All.ToDictionary(k => k.Name);

    public static PalSignalParams With(PalSignalParams p, string name, float value) => name switch
    {
        "pedestal" => p with { Pedestal = value },
        "chroma_gain" => p with { ChromaGain = value },
        "chroma_phase" => p with { ChromaPhaseDegrees = value },
        "burst_gain" => p with { BurstGain = value },
        "level" => p with { Level = value },
        "noise" => p with { NoiseVolts = value },
        "ghost" => p with { Ghost = value },
        "ghost_us" => p with { GhostMicroseconds = value },
        "hum" => p with { HumVolts = value },
        _ => throw new ArgumentException($"unknown signal knob '{name}'"),
    };

    public static Dictionary<string, object> Values(PalSignalParams p) => new()
    {
        ["pedestal"] = p.Pedestal, ["chroma_gain"] = p.ChromaGain, ["chroma_phase"] = p.ChromaPhaseDegrees, ["burst_gain"] = p.BurstGain,
        ["level"] = p.Level, ["noise"] = p.NoiseVolts, ["ghost"] = p.Ghost, ["ghost_us"] = p.GhostMicroseconds, ["hum"] = p.HumVolts,
    };
}
