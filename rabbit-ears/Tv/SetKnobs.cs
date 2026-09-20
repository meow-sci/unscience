using System.Collections.Generic;
using System.Linq;

namespace RabbitEars.Tv;

/// <summary>
/// The television's own controls: flags of `palindrome render`. They are fixed when a decoder starts, so a
/// change means a new decoder process. Defaults are calibrated for this tool's RF signal (40 MS/s IF, ÷2).
/// </summary>
public static class SetKnobs
{
    private const string Picture = "Picture", Tube = "Tube", Supply = "Supply & limiters", Colour = "Colour decoder",
        Sync = "Sync & timebases", Rf = "IF & detector";

    public const string StrideEveryField = "every field (50/s)", StrideEverySecond = "every 2nd field (25/s)";

    public static readonly string[] OpenGroups = { Picture, Tube };

    public static readonly IReadOnlyList<Knob> All = new[]
    {
        Knob.Toggle("colour", "Colour", Picture, "Decode PAL colour, or render monochrome.", true),
        Knob.Slider("contrast", "Contrast", Picture, "Video gain ahead of the gun. 1.07 puts this band's standard white at the readout " +
            "white point (rescaled automatically per raster size). Push it and the limiters fight back.", "--contrast", 0.3, 3.0, 0.01, 1.07),
        Knob.Slider("saturation", "Saturation", Picture, "The colour pot; it rides the contrast. 0.205 is right for a standard-level channel.",
            "--saturation", 0, 0.6, 0.005, 0.205),
        Knob.Slider("gamma", "Gun gamma", Picture, "light = drive ^ gamma. A real tube is about 2.6.", "--gamma", 1.0, 3.0, 0.05, 2.6),
        Knob.Slider("readout_gamma", "Readout (camera) gamma", Picture, "Encodes phosphor light for your monitor. 2.2 is normal; 1.0 is " +
            "raw linear light (the dark double-gamma look).", "--readout-gamma", 1.0, 2.6, 0.05, 2.2),
        Knob.Slider("overscan", "Overscan", Picture, "Fraction of the active picture hidden behind the bezel. Negative shows the blanking.",
            "--overscan", -0.01, 0.15, 0.01, 0.06),
        Knob.Slider("h_shift", "H centring", Picture, "Shifts the picture right, in fractions of a line.", "--h-shift", -0.05, 0.05, 0.001, 0.012),
        Knob.Slider("v_shift", "V centring", Picture, "Shifts the picture down, in fractions of a field.", "--v-shift", -0.05, 0.05, 0.001, 0.008),

        Knob.Choice("raster", "Raster (output pixels)", Tube, "Output framebuffer size. Contrast is recalibrated automatically: light " +
            "per pixel scales with pixel area. Smaller rasters cost the decoder less CPU.", null, new[] { "720x576", "480x384", "360x288" }, "720x576"),
        Knob.Choice("stride", "Readout", Tube, "How often the phosphor is photographed for the stream.", null,
            new[] { StrideEveryField, StrideEverySecond }, StrideEverySecond),
        Knob.Slider("persistence", "Phosphor persistence (fields)", Tube, "How long the phosphor glows. High smears motion and averages " +
            "noise; low flickers.", "--persistence", 0.3, 6.0, 0.1, 1.6),
        Knob.Slider("beam_sigma", "Beam spot, vertical", Tube, "Spot size in scanline pitches. Small = visible scanlines.",
            "--beam-sigma", 0.05, 1.2, 0.01, 0.52),
        Knob.Toggle("round_spot", "Round spot", Tube, "Horizontal spot size follows the vertical one.", true),
        Knob.Slider("beam_sigma_x", "Beam spot, horizontal (columns)", Tube, "Used when 'Round spot' is off. Too small and the sampling " +
            "beat shows as faint stripes.", "--beam-sigma-x", 0.0, 2.5, 0.05, 1.1),

        Knob.Slider("eht_sag", "Blooming: EHT sag", Supply, "How far the final-anode voltage sags under a bright picture: the raster " +
            "breathes (grows), dims and defocuses. 0 = perfectly regulated.", "--eht-sag", 0, 0.25, 0.005, 0.06),
        Knob.Slider("eht_tc", "EHT time constant (fields)", Supply, "How quickly the EHT sags and recovers.", "--eht-tc", 0.25, 10, 0.25, 2.0),
        Knob.Slider("eht_focus", "Blooming: defocus with sag", Supply, "How much the spot grows at full sag. Bright scenes go soft as " +
            "well as large.", "--eht-focus", 0, 1, 0.05, 0.3),
        Knob.Slider("line_pull", "Line pull", Supply, "Line-output loading: bright lines scan wider, so verticals bend beside bright " +
            "content.", "--line-pull", 0, 0.02, 0.0005, 0.003),
        Knob.Slider("bcl", "Beam-current limiter", Supply, "Average beam load above which the set pulls its own contrast down. 0 = off.",
            "--bcl", 0, 1.2, 0.05, 0.7),
        Knob.Slider("bcl_tc", "BCL time constant (fields)", Supply, "How quickly the limiter reacts.", "--bcl-tc", 0.1, 5, 0.1, 0.5),
        Knob.Slider("pwl", "Peak-white limiter", Supply, "Ceiling as a multiple of standard white drive. 0 = off.", "--pwl", 0, 2, 0.05, 1.25),

        Knob.Choice("comb", "Comb mode", Colour, "off = PAL-S (phase errors show as Hanover bars); post = average demodulated U/V; " +
            "delay-line = PAL-D; glass = a real fixed 63.943 us delay line.", "--comb-mode", new[] { "post", "delay-line", "glass", "off" }, "post"),
        Knob.Toggle("killer", "Colour killer", Colour, "Mutes chroma when the ident says this is not PAL.", true),
        Knob.Slider("uv_bandwidth", "U/V bandwidth (Hz)", Colour, "Post-demodulation low-pass. 1.3 MHz is the standard; consumer sets ran " +
            "nearer 600 kHz.", "--uv-bandwidth", 2e5, 2.6e6, 5e4, 1.3e6),
        Knob.Slider("crystal_offset", "Crystal offset (Hz)", Colour, "Detunes the set's 4.43 MHz crystal. Beyond the APC catching range " +
            "the colour never locks and the killer drops it.", null, -2000, 2000, 25, 0),
        Knob.Slider("apc_catch", "APC catching range (Hz)", Colour, "How far the burst phase detector may pull the crystal.",
            "--apc-catch", 0, 2000, 50, 500),
        Knob.Slider("apc_pull", "APC pull", Colour, "Fraction of measured drift folded into the crystal each line.", "--apc-pull", 0.002, 0.2, 0.002, 0.02),
        Knob.Slider("ref_tc", "Reference time constant (lines)", Colour, "How slowly the colour reference follows the burst.", "--ref-tc", 2, 100, 1, 10),
        Knob.Slider("ident_tc", "Ident time constant (lines)", Colour, "Lines the ident integrates before trusting the burst's swing sense.",
            "--ident-tc", 2, 3000, 2, 10),
        Knob.Slider("burst_lo", "Burst gate start", Colour, "Fraction of the line. 0.11 is right for this band's 20 MS/s video.",
            "--burst-lo", 0.06, 0.24, 0.001, 0.11),
        Knob.Slider("burst_hi", "Burst gate end", Colour, "0.14 here. Must stay above the gate start and below H blanking.",
            "--burst-hi", 0.08, 0.28, 0.001, 0.14),
        Knob.Slider("h_blank", "H blanking", Colour, "How far into the line the beam stays blanked. Too small and the burst paints a " +
            "coloured bar down the left edge.", "--h-blank", 0.1, 0.28, 0.005, 0.16),

        Knob.Choice("agc", "AGC scheme", Sync, "sync-tip = the period scheme with absolute levels; adaptive = per-stage autocontrast " +
            "trackers (levels change: expect to retune contrast and saturation).", "--agc", new[] { "sync-tip", "adaptive" }, "sync-tip"),
        Knob.Slider("slice_depth", "Sync slice depth", Sync, "How far below the sync tip the separator slices. 0.2 keeps lock with " +
            "receiver noise in the IF; shallower values chatter on a noisy tip, too deep misses shallow sync.", "--slice-depth", 0.02, 0.3, 0.005, 0.2),
        Knob.Slider("sync_cutoff", "Sync low-pass (Hz)", Sync, "Narrow low-pass on the copy of the signal used for sync.",
            "--sync-cutoff", 3e5, 3e6, 1e5, 1.2e6),
        Knob.Slider("h_kp", "H hold: locked kp", Sync, "Proportional gain of the locked line flywheel.", "--h-kp", 0, 1, 0.02, 0.1),
        Knob.Slider("h_ki", "H hold: locked ki", Sync, "Integral gain of the locked line flywheel.", "--h-ki", 0, 5e-5, 1e-6, 1e-5),
        Knob.Slider("h_acq_kp", "H hold: acquiring kp", Sync, "Proportional gain while acquiring.", "--h-acq-kp", 0, 1, 0.02, 0.5),
        Knob.Slider("h_acq_ki", "H hold: acquiring ki", Sync, "Integral gain while acquiring.", "--h-acq-ki", 0, 5e-4, 1e-5, 1e-4),
        Knob.Slider("h_clamp", "H hold range", Sync, "How far the line rate may stray from nominal.", "--h-clamp", 0.02, 0.3, 0.01, 0.2),
        Knob.Slider("v_level", "V sync threshold", Sync, "Slice level of the vertical-sync integrator.", "--v-level", 0.1, 0.9, 0.01, 0.4),
        Knob.Slider("v_tc", "V integrator (lines)", Sync, "Time constant of the vertical-sync integrator.", "--v-tc", 0.1, 3, 0.1, 0.5),
        Knob.Slider("v_kp", "V hold: kp", Sync, "1.0 snaps to each detected vertical sync.", "--v-kp", 0, 1, 0.02, 1.0),
        Knob.Slider("v_min_field", "V retrigger lockout", Sync, "Ignore a second vertical trigger sooner than this fraction of a field.",
            "--v-min-field", 0, 0.95, 0.05, 0.7),

        Knob.Choice("if", "IF response", Rf, "The IF filter the tuned carrier passes through: saw80 = an early-80s single-SAW set " +
            "(Nyquist flank, chroma slightly down, modest sound trap, some group-delay ripple); saw90 = a cleaner 90s set; flat = an " +
            "ideal symmetric low-pass no real set had. This filter, not the tuner, rejects the adjacent channels.",
            "--if", new[] { "saw80", "saw90", "flat" }, "saw80"),
        Knob.Choice("detector", "Vision detector", Rf, "quasi-sync = a product detector locked to the carrier (linear, no quadrature " +
            "distortion); envelope = a diode detector, with the vestigial-sideband quadrature distortion of early sets.",
            "--detector", new[] { "quasi-sync", "envelope" }, "quasi-sync"),
        Knob.Slider("sound_notch_db", "IF sound rejection (dB)", Rf, "Depth of the IF's sound-carrier trap. Left untouched the IF " +
            "response's own value applies. There is no sound carrier in this band (yet), so it mostly shapes the top of the video band.",
            "--sound-notch-db", 10, 60, 1, 26) with { OnlyWhenChanged = true },
        Knob.Slider("gd_ripple", "IF group-delay ripple (ns)", Rf, "Peak group-delay ripple of the SAW: ringing beside sharp edges. " +
            "Left untouched the IF response's own value applies; 0 = a perfectly phase-corrected IF.",
            "--gd-ripple", 0, 200, 5, 50) with { OnlyWhenChanged = true },
        Knob.Slider("cutoff", "Video low-pass (Hz)", Rf, "Low-pass on the detected video. It must pass 4.43 MHz for colour.",
            "--cutoff", 3e6, 8e6, 5e4, 4.8e6) with { OnlyWhenChanged = true },
    };

    public static readonly IReadOnlyDictionary<string, Knob> ByName = All.ToDictionary(k => k.Name);

    /// <summary>Presets: each is one decoder change on top of the defaults.</summary>
    public static readonly IReadOnlyDictionary<string, Dictionary<string, string>> Looks = new Dictionary<string, Dictionary<string, string>>
    {
        ["Period default"] = new(),
        ["Studio monitor"] = new()
        {
            ["beam_sigma"] = "0.36", ["persistence"] = "0.9", ["eht_sag"] = "0", ["eht_focus"] = "0", ["line_pull"] = "0",
            ["bcl"] = "0", ["overscan"] = "0", ["if"] = "saw90",
        },
        ["Visible scanlines"] = new() { ["beam_sigma"] = "0.26", ["persistence"] = "1.2" },
        ["Tired old set"] = new()
        {
            ["eht_sag"] = "0.2", ["eht_tc"] = "4", ["eht_focus"] = "0.9", ["line_pull"] = "0.014", ["bcl"] = "0.5",
            ["persistence"] = "3.2", ["contrast"] = "1.4", ["beam_sigma"] = "0.7", ["uv_bandwidth"] = "600000",
        },
        ["Contrast cranked"] = new() { ["contrast"] = "2.2", ["saturation"] = "0.3" },
        ["PAL-S (no comb)"] = new() { ["comb"] = "off" },
        ["Diode detector"] = new() { ["detector"] = "envelope" },
    };

    public static Dictionary<string, string> Defaults() => All.ToDictionary(k => k.Name, k => k.Default);
}
