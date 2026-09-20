#!/usr/bin/env python3
"""live_tv.py - a continuously running PAL television, decoded by the real PALindrome.

    moving source (test card + clock + bouncing ball, an image, or ffmpeg video/camera)
      -> real-time PAL composite encoder (numpy, 625/50 interlaced, 16 MS/s, 25 frames/s)
      -> optional impairments (noise, ghost, hum, level, chroma phase error, "aerial unplugged" ...)
      -> `palindrome render --live --input composite`   (native decoder + CRT model, own process)
      -> raw RGB fields on a pipe -> MJPEG -> your browser   (+ optional ffplay window)

Everything between "encoder" and "MJPEG" is PALindrome itself: sync separation, flywheel lock,
burst-locked chroma decoding, colour killer, beam/phosphor model. The web page only displays.

Two kinds of control:
  * SIGNAL controls change what the encoder transmits and take effect immediately.
  * SET controls are PALindrome's own knobs. They are constructor-time in the library, so a change
    means a new decoder process. With "seamless" on, the new set is warmed up beside the old one
    and swapped in once it has locked; with it off (or "Power cycle") you watch it re-acquire.

Run through live_tv.sh (creates the numpy/pillow venv), or directly:
    python live_tv.py --palindrome /path/to/palindrome [--image X | --video X | --lavfi SPEC | --camera N]
"""
import argparse
import json
import os
import subprocess
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from io import BytesIO
from pathlib import Path
from urllib.parse import parse_qsl, urlparse

import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent

# ---------------------------------------------------------------- PAL constants (System I)
FS = 16_000_000                     # line-locked: exactly 1024 samples per 64 us line
SPL = 1024
LINES = 625
FRAME = SPL * LINES                 # 640000 samples = one 25 Hz frame
FSC = 4433618.75
A0 = round(10.4e-6 * FS)            # sync leading edge -> active video
NACT = round(52e-6 * FS)            # 832 active samples
SRC_W, SRC_H = NACT, 576            # the encoder's native picture grid (4:3 on screen)
SYNC_V, WHITE_V, BURST_V = -0.3, 0.7, 0.15
WARM_FIELDS = 30                    # seamless swap: fields a new decoder runs before it goes on screen


def _font(size):
    for p in ("/System/Library/Fonts/Supplemental/Arial Bold.ttf", "/System/Library/Fonts/Helvetica.ttc",
              "/Library/Fonts/Arial.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(p, size)
        except OSError:
            pass
    return ImageFont.load_default()


# ---------------------------------------------------------------- knob tables
def knob(name, label, group, help, flag=None, lo=None, hi=None, step=None, default=None, choices=None, boolean=False):
    return dict(name=name, label=label, group=group, help=help, flag=flag, min=lo, max=hi, step=step,
                default=default, choices=choices, boolean=boolean)


# What the encoder transmits. Applied per frame, no decoder restart.
SIGNAL = [
    knob("noise", "Noise (V rms)", "Reception", "White noise added to the composite signal. The composite input "
         "loses sync abruptly above ~15 mV: clamp and AGC both peak-detect the sync tip.", lo=0, hi=0.06, step=0.001, default=0),
    knob("ghost", "Ghost", "Reception", "A delayed positive echo. Above ~0.33 the echo of the sync pulse out-digs the "
         "fixed-depth sync slicer and horizontal lock goes.", lo=0, hi=0.5, step=0.01, default=0),
    knob("ghost_us", "Ghost delay (us)", "Reception", "Echo delay; 1 us is about 1/52 of the picture width.",
         lo=0.3, hi=6, step=0.1, default=1.6),
    knob("hum", "Mains hum (V)", "Reception", "50 Hz hum riding on the signal, rolling slowly. The sync-tip clamp "
         "rejects most of it (try the 'Clamp release' set control).", lo=0, hi=0.3, step=0.005, default=0),
    knob("level", "Signal level", "Reception", "Attenuates the whole signal, sync included. The composite input has a "
         "declared scale, so this costs contrast first and sync below ~0.35.", lo=0.2, hi=1.3, step=0.01, default=1),
    knob("pedestal", "Black level / brightness (V)", "Transmitted picture", "PALindrome has no brightness pot: black is "
         "DC-restored from the back porch. This lifts (or sinks) the picture relative to blanking at the source, which is "
         "what a brightness control looks like.", lo=-0.15, hi=0.3, step=0.005, default=0),
    knob("chroma_gain", "Source saturation", "Transmitted picture", "Chroma amplitude relative to the burst. The set's ACC "
         "normalises by burst, so this really changes saturation.", lo=0, hi=2.5, step=0.05, default=1),
    knob("chroma_phase", "Chroma phase error (deg)", "Transmitted picture", "Rotates the chroma against the burst, as a "
         "bad link would. PAL's trick: with a comb the error cancels into mild desaturation; set comb mode to 'off' "
         "(PAL-S) and it becomes Hanover bars.", lo=-60, hi=60, step=1, default=0),
    knob("burst_gain", "Burst amplitude", "Transmitted picture", "Scales the colour burst. Low burst over-saturates (ACC) "
         "until the colour killer decides this is not PAL and the picture goes monochrome; 0 = no burst.",
         lo=0, hi=2, step=0.05, default=1),
]

# PALindrome's own knobs (flags of `palindrome render`). A change means a new decoder process.
SET = [
    knob("colour", "Colour", "Picture", "Decode PAL colour, or render monochrome.", flag="--colour", boolean=True, default=1),
    knob("contrast", "Contrast", "Picture", "Video gain ahead of the gun. 1.0 puts a standard white at the readout white "
         "point (the demo rescales it per raster size). Push it and the limiters fight back.",
         flag="--contrast", lo=0.3, hi=3.0, step=0.05, default=1.0),
    knob("saturation", "Saturation", "Picture", "The colour pot. Rides the contrast, as the TDA3561A gangs them. 0.214 is "
         "right for a standard-level source.", flag="--saturation", lo=0, hi=0.6, step=0.005, default=0.214),
    knob("gamma", "Gun gamma", "Picture", "light = drive ^ gamma. A real tube is about 2.6.",
         flag="--gamma", lo=1.0, hi=3.0, step=0.05, default=2.6),
    knob("readout_gamma", "Readout (camera) gamma", "Picture", "Encodes phosphor light for your monitor. 2.2 is normal; "
         "1.0 is raw linear light (the dark double-gamma look).", flag="--readout-gamma", lo=1.0, hi=2.6, step=0.05, default=2.2),
    knob("overscan", "Overscan", "Picture", "Fraction of the active picture hidden behind the bezel. Negative shows the "
         "blanking.", flag="--overscan", lo=-0.01, hi=0.15, step=0.01, default=0.06),
    knob("h_shift", "H centring", "Picture", "Shifts the picture right, in fractions of a line.",
         flag="--h-shift", lo=-0.05, hi=0.08, step=0.001, default=0.03),
    knob("v_shift", "V centring", "Picture", "Shifts the picture down, in fractions of a field.",
         flag="--v-shift", lo=-0.05, hi=0.05, step=0.001, default=0.0),

    knob("raster", "Raster (output pixels)", "Tube", "Output framebuffer size. Contrast is recalibrated automatically: "
         "light per pixel scales with pixel area.", choices=["720x576", "480x384", "360x288"], default="720x576"),
    knob("persistence", "Phosphor persistence (fields)", "Tube", "How long the phosphor glows. High smears motion and "
         "averages noise; low flickers.", flag="--persistence", lo=0.3, hi=6.0, step=0.1, default=1.6),
    knob("beam_sigma", "Beam spot, vertical", "Tube", "Spot size in scanline pitches. Small = visible scanlines.",
         flag="--beam-sigma", lo=0.05, hi=1.2, step=0.01, default=0.52),
    knob("round_spot", "Round spot", "Tube", "Horizontal spot size follows the vertical one.", boolean=True, default=1),
    knob("beam_sigma_x", "Beam spot, horizontal (columns)", "Tube", "Used when 'Round spot' is off. Too small and the "
         "sampling beat shows as faint stripes.", flag="--beam-sigma-x", lo=0.0, hi=2.5, step=0.05, default=1.1),
    knob("stride", "Readout", "Tube", "How often the phosphor is photographed for the stream.",
         choices=["every field (50/s)", "every 2nd field (25/s)"], default="every field (50/s)"),

    knob("eht_sag", "Blooming: EHT sag", "Supply & limiters", "How far the final-anode voltage sags under a bright "
         "picture: the raster breathes (grows), dims and defocuses. 0 = perfectly regulated.",
         flag="--eht-sag", lo=0, hi=0.25, step=0.005, default=0.06),
    knob("eht_tc", "EHT time constant (fields)", "Supply & limiters", "How quickly the EHT sags and recovers.",
         flag="--eht-tc", lo=0.25, hi=10, step=0.25, default=2.0),
    knob("eht_focus", "Blooming: defocus with sag", "Supply & limiters", "How much the spot grows at full sag. Bright "
         "scenes go soft as well as large.", flag="--eht-focus", lo=0, hi=1, step=0.05, default=0.3),
    knob("line_pull", "Line pull", "Supply & limiters", "Line-output loading: bright lines scan wider, so verticals bend "
         "beside bright content.", flag="--line-pull", lo=0, hi=0.02, step=0.0005, default=0.003),
    knob("bcl", "Beam-current limiter", "Supply & limiters", "Average beam load above which the set pulls its own "
         "contrast down. 0 = off.", flag="--bcl", lo=0, hi=1.2, step=0.05, default=0.7),
    knob("bcl_tc", "BCL time constant (fields)", "Supply & limiters", "How quickly the limiter reacts.",
         flag="--bcl-tc", lo=0.1, hi=5, step=0.1, default=0.5),
    knob("pwl", "Peak-white limiter", "Supply & limiters", "Ceiling as a multiple of standard white drive. 0 = off.",
         flag="--pwl", lo=0, hi=2, step=0.05, default=1.25),

    knob("comb", "Comb mode", "Colour decoder", "off = PAL-S (phase errors show as Hanover bars); post = average "
         "demodulated U/V; delay-line = PAL-D; glass = the real fixed 63.943 us delay line.",
         flag="--comb-mode", choices=["post", "delay-line", "glass", "off"], default="post"),
    knob("killer", "Colour killer", "Colour decoder", "Mutes chroma when the ident says this is not PAL.",
         boolean=True, default=1),
    knob("uv_bandwidth", "U/V bandwidth (Hz)", "Colour decoder", "Post-demodulation low-pass. 1.3 MHz is the standard; "
         "consumer sets ran nearer 600 kHz.", flag="--uv-bandwidth", lo=2e5, hi=2.6e6, step=5e4, default=1.3e6),
    knob("crystal_offset", "Crystal offset (Hz)", "Colour decoder", "Detunes the set's 4.43 MHz crystal. Beyond the APC "
         "catching range the colour never locks and the killer drops it.", lo=-2000, hi=2000, step=25, default=0),
    knob("apc_catch", "APC catching range (Hz)", "Colour decoder", "How far the burst phase detector may pull the crystal.",
         flag="--apc-catch", lo=0, hi=2000, step=50, default=500),
    knob("apc_pull", "APC pull", "Colour decoder", "Fraction of measured drift folded into the crystal each line.",
         flag="--apc-pull", lo=0.002, hi=0.2, step=0.002, default=0.02),
    knob("ref_tc", "Reference time constant (lines)", "Colour decoder", "How slowly the colour reference follows the burst.",
         flag="--ref-tc", lo=2, hi=100, step=1, default=10),
    knob("ident_tc", "Ident time constant (lines)", "Colour decoder", "Lines the ident integrates before trusting the "
         "burst's swing sense.", flag="--ident-tc", lo=2, hi=3000, step=2, default=10),
    knob("burst_lo", "Burst gate start", "Colour decoder", "Fraction of the line. Depends on the sample rate: 0.116 at "
         "16 MS/s.", flag="--burst-lo", lo=0.06, hi=0.24, step=0.001, default=0.116),
    knob("burst_hi", "Burst gate end", "Colour decoder", "0.151 at 16 MS/s. Must stay below H blanking.",
         flag="--burst-hi", lo=0.08, hi=0.28, step=0.001, default=0.151),
    knob("h_blank", "H blanking", "Colour decoder", "How far into the line the beam stays blanked. Too small and the "
         "burst paints a coloured bar down the left edge.", flag="--h-blank", lo=0.1, hi=0.28, step=0.005, default=0.16),

    knob("agc", "AGC scheme", "Sync & timebases", "sync-tip = the period scheme with absolute levels; adaptive = the "
         "legacy autocontrast trackers (levels change: expect to retune contrast and saturation).",
         flag="--agc", choices=["sync-tip", "adaptive"], default="sync-tip"),
    knob("slice_depth", "Sync slice depth", "Sync & timebases", "How far below the sync tip the separator slices "
         "(sync-tip AGC). Deeper survives ghosts; too deep misses shallow sync.",
         flag="--slice-depth", lo=0.02, hi=0.3, step=0.005, default=0.08),
    knob("sync_cutoff", "Sync low-pass (Hz)", "Sync & timebases", "Narrow low-pass on the copy of the signal used for sync.",
         flag="--sync-cutoff", lo=3e5, hi=3e6, step=1e5, default=1.2e6),
    knob("h_kp", "H hold: locked kp", "Sync & timebases", "Proportional gain of the locked line flywheel.",
         flag="--h-kp", lo=0, hi=1, step=0.02, default=0.1),
    knob("h_ki", "H hold: locked ki", "Sync & timebases", "Integral gain of the locked line flywheel.",
         flag="--h-ki", lo=0, hi=5e-5, step=1e-6, default=1e-5),
    knob("h_acq_kp", "H hold: acquiring kp", "Sync & timebases", "Proportional gain while acquiring.",
         flag="--h-acq-kp", lo=0, hi=1, step=0.02, default=0.5),
    knob("h_acq_ki", "H hold: acquiring ki", "Sync & timebases", "Integral gain while acquiring.",
         flag="--h-acq-ki", lo=0, hi=5e-4, step=1e-5, default=1e-4),
    knob("h_clamp", "H hold range", "Sync & timebases", "How far the line rate may stray from nominal.",
         flag="--h-clamp", lo=0.02, hi=0.3, step=0.01, default=0.2),
    knob("v_level", "V sync threshold", "Sync & timebases", "Slice level of the vertical-sync integrator.",
         flag="--v-level", lo=0.1, hi=0.9, step=0.01, default=0.4),
    knob("v_tc", "V integrator (lines)", "Sync & timebases", "Time constant of the vertical-sync integrator.",
         flag="--v-tc", lo=0.1, hi=3, step=0.1, default=0.5),
    knob("v_kp", "V hold: kp", "Sync & timebases", "1.0 snaps to each detected vertical sync.",
         flag="--v-kp", lo=0, hi=1, step=0.02, default=1.0),
    knob("v_min_field", "V retrigger lockout", "Sync & timebases", "Ignore a second vertical trigger sooner than this "
         "fraction of a field.", flag="--v-min-field", lo=0, hi=0.95, step=0.05, default=0.7),

    knob("cutoff", "Video low-pass (Hz)", "Input stage", "Front-end low-pass on the composite input. Must pass 4.43 MHz for "
         "colour; 8 MHz (Nyquist here) bypasses it.", flag="--cutoff", lo=3e6, hi=8e6, step=5e4, default=5.5e6),
    knob("composite_sync", "Declared sync amplitude (V)", "Input stage", "What the set believes the sync amplitude is. "
         "Declare it too small and everything decodes hot; far too small and nothing locks.",
         flag="--composite-sync", lo=0.05, hi=1.0, step=0.005, default=0.3),
    knob("composite_clamp", "Clamp release (lines)", "Input stage", "Sync-tip clamp release. Short tracks hum faster but "
         "droops on bright content.", flag="--composite-clamp", lo=4, hi=512, step=4, default=128),
]

LOOKS = {   # SET presets: one decoder change each
    "Period default": {},
    "Studio monitor": {"beam_sigma": 0.36, "persistence": 0.9, "eht_sag": 0, "eht_focus": 0, "line_pull": 0, "bcl": 0, "overscan": 0.0},
    "Visible scanlines": {"beam_sigma": 0.26, "persistence": 1.2},
    "Tired old set": {"eht_sag": 0.2, "eht_tc": 4, "eht_focus": 0.9, "line_pull": 0.014, "bcl": 0.5, "persistence": 3.2,
                      "contrast": 1.35, "beam_sigma": 0.7, "uv_bandwidth": 6e5},
    "Contrast cranked": {"contrast": 2.2, "saturation": 0.3},
    "PAL-S (no comb)": {"comb": "off"},
}


# ---------------------------------------------------------------- real-time encoder
class PalEncoder:
    """RGB (576 x 832 x 3, uint8, gamma-encoded) -> one frame of CVBS as int16 (1.0 == 1 V).

    Everything that does not depend on the picture is precomputed for the 4-frame PAL sequence
    (the subcarrier advances 0.75 cycle per frame at this sample rate; the V switch alternates
    every line and 625 is odd), so the per-frame work is a matrix, two chroma low-passes and a
    handful of multiply-adds over the active picture.
    """

    def __init__(self):
        sync = np.zeros((LINES, SPL), np.float64)
        burst_env = np.zeros((LINES, SPL), np.float64)
        n_sync, n_eq = round(4.7e-6 * FS), round(2.35e-6 * FS)
        n_broad = SPL // 2 - round(4.7e-6 * FS)
        b0, nb = round(5.6e-6 * FS), round(10 / FSC * FS)
        kinds = {line: [(0, "S")] for line in range(1, LINES + 1)}
        vbi = set()

        def half(slots, kind):
            for line, h in slots:
                if line not in vbi:
                    vbi.add(line)
                    kinds[line] = []
                kinds[line].append((0 if h == "a" else SPL // 2, kind))

        half([(1, "a"), (1, "b"), (2, "a"), (2, "b"), (3, "a")], "B")
        half([(3, "b"), (4, "a"), (4, "b"), (5, "a"), (5, "b")], "E")
        half([(311, "a"), (311, "b"), (312, "a"), (312, "b"), (313, "a")], "E")
        half([(313, "b"), (314, "a"), (314, "b"), (315, "a"), (315, "b")], "B")
        half([(316, "a"), (316, "b"), (317, "a"), (317, "b"), (318, "a")], "E")
        kinds[623] = [(0, "S"), (SPL // 2, "E")]
        half([(624, "a"), (624, "b"), (625, "a"), (625, "b")], "E")
        width = {"S": n_sync, "E": n_eq, "B": n_broad}
        for line, pulses in kinds.items():
            for start, kind in pulses:
                sync[line - 1, start:start + width[kind]] = 1.0
            if line not in vbi:
                burst_env[line - 1, b0:b0 + nb] = 1.0
        hann = np.hanning(7)[1:-1]
        sync = np.convolve(sync.ravel(), hann / hann.sum(), mode="same").reshape(LINES, SPL)   # monotonic edges
        hann = np.hanning(9)[1:-1]
        burst_env = np.convolve(burst_env.ravel(), hann / hann.sum(), mode="same").reshape(LINES, SPL)

        n = np.arange(FRAME, dtype=np.float64).reshape(LINES, SPL)
        line_idx = np.arange(LINES).reshape(LINES, 1)
        self.sync = (SYNC_V * sync).astype(np.float32)
        self.burst, self.s1, self.c1, self.s2, self.c2, self.v1, self.v2 = [], [], [], [], [], [], []
        act = slice(A0, A0 + NACT)
        for k in range(4):
            theta = 2 * np.pi * np.mod(FSC / FS * n + 0.75 * k, 1.0)
            s, c = np.sin(theta), np.cos(theta)
            vsw = np.where(((line_idx + k * LINES) & 1) == 0, 1.0, -1.0)
            self.burst.append((BURST_V / np.sqrt(2) * burst_env * (-s + vsw * c)).astype(np.float32))
            self.s1.append((WHITE_V * s[22:310, act]).astype(np.float32))
            self.c1.append((WHITE_V * (vsw * c)[22:310, act]).astype(np.float32))
            self.s2.append((WHITE_V * s[335:623, act]).astype(np.float32))
            self.c2.append((WHITE_V * (vsw * c)[335:623, act]).astype(np.float32))
            self.v1.append(vsw[22:310].astype(np.float32))
            self.v2.append(vsw[335:623].astype(np.float32))
        self.rng = np.random.default_rng(1)
        self.noise = self.rng.standard_normal(3 * FRAME).astype(np.float32)
        self.k = 0
        self.hum_phase = 0.0

    @staticmethod
    def _chroma_lp(a):
        """~1.3 MHz low-pass: area-average down to 208 samples per line, bicubic back up."""
        im = Image.fromarray(np.ascontiguousarray(a, dtype=np.float32))      # float32 -> mode 'F'
        return np.asarray(im.resize((SRC_W // 4, a.shape[0]), Image.BOX).resize((SRC_W, a.shape[0]), Image.BICUBIC))

    def encode(self, rgb, p):
        k = self.k
        self.k = (k + 1) & 3
        out = self.sync + np.float32(p["burst_gain"]) * self.burst[k]
        flat = out.reshape(-1)
        if p["aerial_out"]:
            off = int(self.rng.integers(0, 2 * FRAME))
            flat[:] = 0.35 + 0.5 * self.noise[off:off + FRAME]       # no carrier: wideband noise, clipped by the ADC
            return np.clip(flat * np.float32(32768), -32768, 32767).astype("<i2").tobytes()

        x = rgb.astype(np.float32) * np.float32(1 / 255)
        y = x @ np.array([0.299, 0.587, 0.114], np.float32)
        g = np.float32(p["chroma_gain"])
        u = self._chroma_lp(np.float32(0.493) * g * (x[..., 2] - y))
        v = self._chroma_lp(np.float32(0.877) * g * (x[..., 0] - y))
        ped = np.float32(p["pedestal"])
        phi = np.deg2rad(p["chroma_phase"])
        cp, sp = np.float32(np.cos(phi)), np.float32(np.sin(phi))
        act = slice(A0, A0 + NACT)
        for rows, lines, s, c, vs in ((slice(0, None, 2), slice(22, 310), self.s1[k], self.c1[k], self.v1[k]),
                                      (slice(1, None, 2), slice(335, 623), self.s2[k], self.c2[k], self.v2[k])):
            uu, vv = u[rows], v[rows]
            if sp != 0:      # U sin(wt+phi) +/- V cos(wt+phi): the error alternates with the PAL switch
                uu, vv = uu * cp - vs * vv * sp, vv * cp + vs * uu * sp
            out[lines, act] += WHITE_V * y[rows] + ped + s * uu + c * vv
        half_a, half_b = slice(A0, A0 + NACT // 2), slice(A0 + NACT // 2, A0 + NACT)
        out[22, half_a] = self.sync[22, half_a]                     # line 23 and 623 are half lines
        out[622, half_b] = self.sync[622, half_b]

        if p["ghost"] > 0:
            d = max(1, round(p["ghost_us"] * 1e-6 * FS))
            tmp = flat.copy()
            flat[d:] += np.float32(p["ghost"]) * tmp[:-d]
        if p["level"] != 1.0:
            flat *= np.float32(p["level"])
        if p["hum"] > 0:
            t = (np.arange(LINES, dtype=np.float32) * np.float32(64e-6)).reshape(LINES, 1)
            out += np.float32(p["hum"]) * np.sin(2 * np.pi * 50.0 * t + self.hum_phase).astype(np.float32)
            self.hum_phase = (self.hum_phase + 2 * np.pi * 0.2 / 25) % (2 * np.pi)   # rolls at 0.2 Hz
        if p["noise"] > 0:
            off = int(self.rng.integers(0, 2 * FRAME))
            flat += np.float32(p["noise"]) * self.noise[off:off + FRAME]
        return np.clip(flat * np.float32(32768), -32768, 32767).astype("<i2").tobytes()


# ---------------------------------------------------------------- sources
class CardSource:
    """Static picture + things that move, so it is obviously live."""

    def __init__(self, base_rgb, overlays=True):
        self.base = base_rgb.resize((SRC_W, SRC_H), Image.LANCZOS)
        self.overlays = overlays
        self.f_big, self.f_small = _font(34), _font(20)
        self.t0 = time.monotonic()
        self.n = 0
        self.ticker = ("   UNSCIENCE TV  ***  live PAL decode by PALindrome  ***  sync separator > flywheels > "
                       "burst-locked chroma > CRT beam + phosphor  ***  ")

    def frame(self):
        im = self.base.copy()
        if self.overlays:
            d = ImageDraw.Draw(im)
            t = time.monotonic() - self.t0
            self.n += 1
            bx = 40 + abs((t * 190) % (2 * (SRC_W - 120)) - (SRC_W - 120))      # bouncing ball
            by = 60 + abs((t * 130) % (2 * (SRC_H - 200)) - (SRC_H - 200))
            d.ellipse([bx, by, bx + 56, by + 56], fill=f"hsl({int(t * 40) % 360},100%,50%)", outline=(255, 255, 255), width=2)
            d.rectangle([SRC_W - 370, 40, SRC_W - 50, 86], fill=(0, 0, 0))        # clock + frame counter
            d.text((SRC_W - 362, 42), time.strftime("%H:%M:%S") + f".{int((time.time() % 1) * 100):02d}",
                   font=self.f_big, fill=(255, 255, 255))
            d.text((SRC_W - 148, 52), f"#{self.n % 10000:04d}", font=self.f_small, fill=(255, 220, 60))
            d.rectangle([0, SRC_H - 66, SRC_W, SRC_H - 34], fill=(10, 10, 60))    # ticker
            x = int(t * 120) % int(d.textlength(self.ticker, font=self.f_small))
            d.text((-x, SRC_H - 62), self.ticker * 3, font=self.f_small, fill=(255, 255, 255))
        return np.asarray(im)


class FfmpegSource:
    """Raw RGB frames from ffmpeg: a file (looped), a lavfi generator, or an avfoundation camera."""

    def __init__(self, input_args):
        vf = ("fps=25,scale=768:576:force_original_aspect_ratio=decrease,"
              f"pad=768:576:(ow-iw)/2:(oh-ih)/2:black,scale={SRC_W}:{SRC_H}")
        cmd = ["ffmpeg", "-loglevel", "error", *input_args, "-an", "-vf", vf, "-pix_fmt", "rgb24", "-f", "rawvideo", "-"]
        self.proc = subprocess.Popen(cmd, stdout=subprocess.PIPE, stdin=subprocess.DEVNULL)
        self.latest = np.zeros((SRC_H, SRC_W, 3), np.uint8)
        threading.Thread(target=self._pump, daemon=True).start()

    def _pump(self):
        size = SRC_W * SRC_H * 3
        while True:
            buf = self.proc.stdout.read(size)
            if len(buf) < size:
                print("live_tv: ffmpeg source ended", file=sys.stderr)
                return
            self.latest = np.frombuffer(buf, np.uint8).reshape(SRC_H, SRC_W, 3)

    def frame(self):
        return self.latest


# ---------------------------------------------------------------- the television (PALindrome processes)
class Instance:
    """One `palindrome render --live` process and what we know about it."""

    def __init__(self, proc, gen, width, height, channels, knobs, cmd):
        self.proc, self.gen, self.width, self.height, self.channels = proc, gen, width, height, channels
        self.knobs, self.cmd = knobs, cmd
        self.frames = 0
        self.started = time.monotonic()
        self.stderr_tail = []

    def alive(self):
        return self.proc.poll() is None


class Television:
    RASTERS = {"720x576": (720, 576), "480x384": (480, 384), "360x288": (360, 288)}

    def __init__(self, binary, ffplay=False):
        self.binary = binary
        self.ffplay = ffplay
        self.defaults = {k["name"]: k["default"] for k in SET}
        self.knobs = dict(self.defaults)
        self.good_knobs = dict(self.defaults)
        self.lock = threading.RLock()
        self.cond = threading.Condition()
        self.jpeg, self.seq = None, 0
        self.active = None
        self.pending = None
        self.viewer = None
        self.gen = 0
        self.out_times = []
        self.message = ""
        self.failures = 0               # consecutive decoders that died before their first picture

    # -- command line
    def args(self, knobs):
        w, h = self.RASTERS[knobs["raster"]]
        # Deposits are not normalised by samples-per-pixel, so light per pixel scales with pixel
        # area; the gun is a power law, hence this contrast rule (measured, RESEARCH 4.3).
        raster_cal = (w * h / (720 * 576)) ** (1 / knobs["gamma"])
        stride = 1 if knobs["stride"].startswith("every field") else 2
        a = [self.binary, "render", "--live", "--input", "composite", "--sample-rate", str(FS), "--sample-format", "s16",
             "--frame-stride", str(stride), "--width", str(w), "--height", str(h), "--deposit-threads", "2"]
        for k in SET:
            name, value = k["name"], knobs[k["name"]]
            if name == "contrast":
                a += ["--contrast", f"{value * raster_cal:.4f}"]
            elif name == "colour":
                a += ["--colour"] if value else []
            elif name == "killer":
                a += [] if value else ["--no-killer"]
            elif name == "crystal_offset":
                a += ["--subcarrier", f"{FSC + value:.2f}"] if value else []
            elif name == "beam_sigma_x":
                a += [] if knobs["round_spot"] else ["--beam-sigma-x", str(value)]
            elif k["flag"]:
                if not isinstance(value, str):
                    value = str(int(value)) if float(value).is_integer() else repr(float(value))
                a += [k["flag"], value]
        return a

    # -- process management
    def _spawn(self, knobs):
        r, w = os.pipe()
        cmd = self.args(knobs)
        proc = subprocess.Popen(cmd + ["--frame-fd", str(w)], stdin=subprocess.PIPE, stdout=subprocess.DEVNULL,
                                stderr=subprocess.PIPE, pass_fds=(w,))
        os.close(w)
        self.gen += 1
        width, height = self.RASTERS[knobs["raster"]]
        inst = Instance(proc, self.gen, width, height, 3 if knobs["colour"] else 1, dict(knobs), " ".join(cmd[1:]))
        threading.Thread(target=self._read_frames, args=(inst, os.fdopen(r, "rb", buffering=0)), daemon=True).start()
        threading.Thread(target=self._read_stderr, args=(inst,), daemon=True).start()
        return inst

    @staticmethod
    def _retire(inst):
        def work():
            try:
                inst.proc.stdin.close()
            except OSError:
                pass
            try:
                inst.proc.wait(timeout=1.0)
            except subprocess.TimeoutExpired:
                inst.proc.kill()
        if inst is not None:
            threading.Thread(target=work, daemon=True).start()

    def launch(self, seamless):
        """Start a decoder with the current knobs: beside the old one (seamless) or instead of it."""
        with self.lock:
            inst = self._spawn(self.knobs)
            if seamless and not self.ffplay and self.active is not None and self.active.alive():
                self._retire(self.pending)
                self.pending = inst
                self.message = "warming up the new set beside the old one ..."
            else:
                self._retire(self.pending)
                self._retire(self.active)
                self.pending, self.active = None, inst
                self._start_viewer(inst)

    def _start_viewer(self, inst):
        if not self.ffplay:
            return
        if self.viewer is not None:
            self.viewer.kill()
        stride = 1 if inst.knobs["stride"].startswith("every field") else 2
        self.viewer = subprocess.Popen(
            ["ffplay", "-loglevel", "error", "-f", "rawvideo", "-pixel_format", "rgb24" if inst.channels == 3 else "gray",
             "-video_size", f"{inst.width}x{inst.height}", "-framerate", str(50 // stride), "-vf", "scale=768:576",
             "-window_title", "PALindrome live", "-"], stdin=subprocess.PIPE)

    def set_knobs(self, changes, seamless):
        with self.lock:
            self.knobs.update(changes)
            self.launch(seamless)

    def feed(self, pcm):
        active, pending = self.active, self.pending
        ok = False
        for inst in (active, pending):
            if inst is None:
                continue
            try:
                inst.proc.stdin.write(pcm)
                ok = ok or inst is active
            except (BrokenPipeError, ValueError, OSError):
                pass
        return ok

    def _failed(self, inst):
        """A decoder died before producing a picture: almost always a knob combination it rejects."""
        with self.lock:
            reason = " ".join(inst.stderr_tail[-2:]) or "exited without a message"
            self.failures += 1
            if inst.knobs == self.good_knobs or self.failures > 4:
                self.message = f"decoder will not start: {reason}"
                return
            self.message = f"decoder refused these settings: {reason}  (reverted)"
            self.knobs = dict(self.good_knobs)
            if inst is self.pending:
                self.pending = None
            elif inst is self.active:
                self.active = None
                self.launch(seamless=False)

    def _read_stderr(self, inst):
        for line in inst.proc.stderr:
            inst.stderr_tail = (inst.stderr_tail + [line.decode(errors="replace").strip()])[-6:]

    def _read_frames(self, inst, pipe):
        size = inst.width * inst.height * inst.channels
        mode = "RGB" if inst.channels == 3 else "L"
        while True:
            buf = bytearray()
            while len(buf) < size:
                chunk = pipe.read(size - len(buf))
                if not chunk:
                    if inst.frames == 0 and (inst is self.active or inst is self.pending):
                        time.sleep(0.2)            # let the stderr reader collect the reason
                        self._failed(inst)
                    return
                buf += chunk
            inst.frames += 1
            self.failures = 0
            if inst is self.pending and inst.frames >= WARM_FIELDS:
                with self.lock:
                    if inst is self.pending:       # promote: the new set has locked and its colour has faded up
                        old, self.active, self.pending = self.active, inst, None
                        self.good_knobs = dict(inst.knobs)
                        self.message = ""
                        self._retire(old)
            if inst is not self.active:
                continue
            if inst.frames == WARM_FIELDS:
                self.good_knobs = dict(inst.knobs)
                if not self.message.startswith("decoder refused"):
                    self.message = ""
            if self.viewer is not None:
                try:
                    self.viewer.stdin.write(buf)
                except (BrokenPipeError, OSError):
                    pass
            bio = BytesIO()
            Image.frombuffer(mode, (inst.width, inst.height), bytes(buf), "raw", mode, 0, 1).save(bio, "JPEG", quality=88)
            now = time.monotonic()
            with self.cond:
                self.jpeg = bio.getvalue()
                self.seq += 1
                self.out_times = [t for t in self.out_times if now - t < 2.0] + [now]
                self.cond.notify_all()

    def wait_frame(self, seq, timeout=2.0):
        with self.cond:
            if self.seq == seq:
                self.cond.wait(timeout)
            return self.jpeg, self.seq

    def cpu_percent(self):
        pids = [str(i.proc.pid) for i in (self.active, self.pending) if i is not None and i.alive()]
        if not pids:
            return 0.0
        try:
            out = subprocess.run(["ps", "-o", "%cpu=", "-p", ",".join(pids)], capture_output=True, text=True, timeout=2)
            return sum(float(x) for x in out.stdout.split())
        except (ValueError, subprocess.SubprocessError):
            return 0.0

    def shutdown(self):
        with self.lock:
            for inst in (self.active, self.pending):
                if inst is not None:
                    inst.proc.kill()
            if self.viewer is not None:
                self.viewer.kill()


# ---------------------------------------------------------------- the station (source + encoder + pacing)
class Station(threading.Thread):
    def __init__(self, tv, sources, first):
        super().__init__(daemon=True)
        self.tv = tv
        self.sources = sources
        self.source_name = first
        self.params = {k["name"]: k["default"] for k in SIGNAL}
        self.params["aerial_out"] = 0
        self.flash_until = 0.0
        self.encoder = PalEncoder()
        self.enc_ms = 0.0
        self.feed_times = []
        self.white = np.full((SRC_H, SRC_W, 3), 255, np.uint8)

    def run(self):
        period = 1 / 25
        next_t = time.monotonic()
        while True:
            t0 = time.monotonic()
            rgb = self.white if t0 < self.flash_until else self.sources[self.source_name].frame()
            pcm = self.encoder.encode(rgb, dict(self.params))
            self.enc_ms = 0.9 * self.enc_ms + 0.1 * (time.monotonic() - t0) * 1000
            if not self.tv.feed(pcm):
                time.sleep(0.2)
                active = self.tv.active
                if (active is None or not active.alive()) and self.tv.failures <= 4:
                    self.tv.launch(seamless=False)
            now = time.monotonic()
            self.feed_times = [t for t in self.feed_times if now - t < 2.0] + [now]
            next_t += period
            if next_t < now - 0.5:            # fell far behind (decoder too slow / machine busy): resync
                next_t = now
            time.sleep(max(0.0, next_t - now))


PAGE = r"""<!doctype html><meta charset=utf-8><title>PALindrome live</title>
<style>
 body{background:#15161a;color:#ddd;font:13px -apple-system,Helvetica,sans-serif;margin:0;display:flex;gap:22px;padding:22px;align-items:flex-start}
 #left{position:sticky;top:22px} #set{background:#2a2622;padding:24px 24px 30px;border-radius:26px;box-shadow:0 12px 40px #000a}
 #tube{width:768px;aspect-ratio:4/3;background:#000;border-radius:38px/30px;overflow:hidden;box-shadow:inset 0 0 40px #000}
 #tube img{width:100%;height:100%;display:block}
 #panel{flex:1;min-width:380px;max-width:560px} h3{margin:14px 0 6px;color:#f0c060;font-size:12px;text-transform:uppercase;letter-spacing:.08em}
 details{background:#1d1f25;border-radius:8px;margin:6px 0;padding:4px 10px} summary{cursor:pointer;color:#f0c060;padding:4px 0;font-weight:600}
 .row{display:grid;grid-template-columns:190px 1fr 74px;align-items:center;gap:8px;margin:3px 0}
 .row label{cursor:help;border-bottom:1px dotted #555;justify-self:start} .row span{text-align:right;font-variant-numeric:tabular-nums;color:#9ab}
 .row.changed label{color:#ffd27a}
 button,select{background:#3a3d46;color:#eee;border:0;border-radius:6px;padding:6px 10px;margin:2px 3px 2px 0;font:inherit;cursor:pointer}
 button.on{background:#b5452d} pre{background:#0d0e11;padding:10px;border-radius:8px;font-size:11.5px;white-space:pre-wrap;margin:6px 0}
 #msg{color:#ff9a7a;min-height:1.2em} small{color:#889} input[type=range]{width:100%}
</style>
<div id=left><div id=set><div id=tube><img src="/stream"></div></div>
 <pre id=stats>...</pre><div id=msg></div></div>
<div id=panel>
 <h3>Signal &mdash; what is transmitted (changes instantly)</h3>
 <button data-sig='{"noise":0,"ghost":0,"hum":0,"level":1,"pedestal":0,"chroma_gain":1,"chroma_phase":0,"burst_gain":1}'>Clean</button>
 <button data-sig='{"noise":0.011,"ghost":0.22,"ghost_us":2.2,"hum":0.01,"level":0.85}'>Weak signal</button>
 <button data-sig='{"noise":0.003,"ghost":0.3,"ghost_us":3.5}'>Ghosting</button>
 <button data-sig='{"chroma_phase":25}'>Phase error 25&deg;</button>
 <button id=aerial>Unplug aerial</button><button id=flash>White flash</button>
 <div id=srcs></div><div id=signal></div>
 <h3>The set &mdash; PALindrome's own knobs (each change = a new decoder)</h3>
 <button id=power>Power cycle</button><button id=reset>Reset set to defaults</button>
 <label style="margin-left:8px"><input type=checkbox id=seamless checked> seamless (warm up the new set, then swap)</label>
 <div id=looks></div><div id=setknobs></div>
 <small>Hover a label for what it does. Orange = moved from its default. With "seamless" off you see the honest behaviour:
 every knob change re-locks the set, because PALindrome's configuration is fixed at construction.</small>
</div>
<script>
const $=id=>document.getElementById(id), fmt=(k,v)=>k.choices||k.boolean?'':(Math.abs(v)>=1e5?(v/1e6).toFixed(2)+'M':String(parseFloat((+v).toPrecision(4))));
const seamless=()=>$('seamless').checked?1:0;
const send=(o,isSet)=>fetch('/set?'+new URLSearchParams(o)+(isSet?'&seamless='+seamless():''));
function row(k,value,isSet){
  const r=document.createElement('div'); r.className='row'; r.id='row_'+k.name;
  const l=document.createElement('label'); l.textContent=k.label; l.title=k.help; r.appendChild(l);
  let e; const s=document.createElement('span');
  if(k.boolean){e=document.createElement('input');e.type='checkbox';e.checked=!!value;e.style.justifySelf='start'}
  else if(k.choices){e=document.createElement('select');for(const c of k.choices){const o=document.createElement('option');o.textContent=c;e.appendChild(o)}e.value=value}
  else{e=document.createElement('input');e.type='range';e.min=k.min;e.max=k.max;e.step=k.step;e.value=value}
  e.id='k_'+k.name; const val=()=>k.boolean?(e.checked?1:0):e.value;
  const show=()=>{s.textContent=fmt(k,val());r.classList.toggle('changed',String(val())!=String(k.boolean?(k.default?1:0):k.default))};
  show(); e.oninput=()=>{show(); if(!isSet)send({[k.name]:val()},false)}; if(isSet)e.onchange=()=>{show();send({[k.name]:val()},true)};
  e._show=show; r.appendChild(e); r.appendChild(s); return r}
function build(host,list,values,isSet,openGroups){const groups={};
  for(const k of list){(groups[k.group]=groups[k.group]||[]).push(k)}
  for(const g in groups){const d=document.createElement('details'); if(openGroups.includes(g))d.open=true;
    const m=document.createElement('summary');m.textContent=g;d.appendChild(m);for(const k of groups[g])d.appendChild(row(k,values[k.name],isSet));host.appendChild(d)}}
function reflect(values){for(const n in values){const e=$('k_'+n);if(!e)continue;if(e.type=='checkbox')e.checked=!!values[n];else e.value=values[n];e._show()}}
let KN;
fetch('/knobs').then(r=>r.json()).then(k=>{KN=k;
  build($('signal'),k.signal,k.signal_values,false,['Reception','Transmitted picture']);
  build($('setknobs'),k.set,k.set_values,true,['Picture','Tube','Supply & limiters']);
  for(const name in k.looks){const b=document.createElement('button');b.textContent=name;
    b.onclick=()=>fetch('/look?name='+encodeURIComponent(name)+'&seamless='+seamless()).then(r=>r.json()).then(reflect);$('looks').appendChild(b)}
  if(k.sources.length>1){$('srcs').innerHTML='<small>source: </small>';for(const n of k.sources){const b=document.createElement('button');b.textContent=n;b.onclick=()=>send({source:n},false);$('srcs').appendChild(b)}}});
for(const b of document.querySelectorAll('button[data-sig]'))b.onclick=()=>{const o=JSON.parse(b.dataset.sig);reflect(o);send(o,false)};
let out=0;$('aerial').onclick=()=>{out=1-out;$('aerial').classList.toggle('on',!!out);$('aerial').textContent=out?'Plug aerial back in':'Unplug aerial';send({aerial_out:out},false)};
$('flash').onclick=()=>send({flash:1.5},false); $('power').onclick=()=>fetch('/set?power=1');
$('reset').onclick=()=>fetch('/look?name=__reset__&seamless='+seamless()).then(r=>r.json()).then(reflect);
setInterval(()=>fetch('/stats').then(r=>r.json()).then(s=>{$('msg').textContent=s.message;$('stats').textContent=
 `source ${s.source} | encoder ${s.enc_ms.toFixed(1)} ms/frame | fed ${s.feed_fps.toFixed(1)} frames/s (25 = real time)\n`+
 `fields decoded ${s.out_fps.toFixed(1)}/s | decoder CPU ${s.cpu.toFixed(0)} % of one core | up ${s.uptime.toFixed(0)} s${s.warming?' | WARMING NEW SET':''}\n\npalindrome ${s.cmd}`}),1000);
</script>"""


def make_handler(tv, station):
    signal_by_name = {k["name"]: k for k in SIGNAL}
    set_by_name = {k["name"]: k for k in SET}

    def coerce(k, value):
        if k["boolean"]:
            return int(value not in ("0", "false", ""))
        if k["choices"]:
            if value not in k["choices"]:
                raise ValueError(value)
            return value
        return min(k["max"], max(k["min"], float(value)))

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *a):
            pass

        def _send(self, body, ctype="application/json"):
            if not isinstance(body, bytes):
                body = json.dumps(body).encode()
            self.send_response(200)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            url = urlparse(self.path)
            query = dict(parse_qsl(url.query))
            seamless = query.pop("seamless", "1") != "0"
            if url.path == "/":
                return self._send(PAGE.encode(), "text/html; charset=utf-8")
            if url.path == "/snapshot.jpg":
                return self._send(tv.wait_frame(-1, 0)[0] or b"", "image/jpeg")
            if url.path == "/knobs":
                return self._send({"signal": SIGNAL, "set": SET, "signal_values": station.params, "set_values": tv.knobs,
                                   "looks": LOOKS, "sources": list(station.sources)})
            if url.path == "/stats":
                now = time.monotonic()
                active = tv.active
                return self._send({"source": station.source_name, "enc_ms": station.enc_ms, "message": tv.message,
                                   "feed_fps": len([t for t in station.feed_times if now - t < 2.0]) / 2.0,
                                   "out_fps": len([t for t in tv.out_times if now - t < 2.0]) / 2.0,
                                   "cpu": tv.cpu_percent(), "uptime": now - active.started if active else 0,
                                   "warming": tv.pending is not None, "params": station.params, "knobs": tv.knobs,
                                   "cmd": active.cmd if active else ""})
            if url.path == "/look":
                name = query.get("name", "")
                if name == "__reset__" or name in LOOKS:
                    tv.set_knobs({**tv.defaults, **LOOKS.get(name, {})}, seamless)
                return self._send(tv.knobs)
            if url.path == "/set":
                changes = {}
                for key, value in query.items():
                    try:
                        if key in signal_by_name:
                            station.params[key] = coerce(signal_by_name[key], value)
                        elif key in set_by_name:
                            changes[key] = coerce(set_by_name[key], value)
                        elif key == "aerial_out":
                            station.params[key] = int(value != "0")
                        elif key == "flash":
                            station.flash_until = time.monotonic() + min(5.0, float(value))
                        elif key == "source" and value in station.sources:
                            station.source_name = value
                        elif key == "power":
                            tv.launch(seamless=False)
                    except ValueError:
                        pass
                if changes:
                    tv.set_knobs(changes, seamless)
                return self._send(b"ok", "text/plain")
            if url.path == "/stream":
                self.send_response(200)
                self.send_header("Content-Type", "multipart/x-mixed-replace; boundary=frame")
                self.send_header("Cache-Control", "no-store")
                self.end_headers()
                seq = -1
                try:
                    while True:
                        jpeg, new_seq = tv.wait_frame(seq)
                        if jpeg is None or new_seq == seq:
                            continue
                        seq = new_seq
                        self.wfile.write(b"--frame\r\nContent-Type: image/jpeg\r\nContent-Length: %d\r\n\r\n" % len(jpeg))
                        self.wfile.write(jpeg)
                        self.wfile.write(b"\r\n")
                except (BrokenPipeError, ConnectionResetError, OSError):
                    return
            self.send_error(404)

    return Handler


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--palindrome", default=os.environ.get("PALINDROME_BIN", "palindrome"))
    ap.add_argument("--image", help="show this image instead of the generated test card")
    ap.add_argument("--video", help="video file, looped (needs ffmpeg)")
    ap.add_argument("--lavfi", help="ffmpeg lavfi generator, e.g. testsrc2, smptebars, mandelbrot, life")
    ap.add_argument("--camera", help="avfoundation camera index, e.g. 0 (macOS will ask for camera permission)")
    ap.add_argument("--no-overlays", action="store_true", help="no clock/ball/ticker on the card or image")
    ap.add_argument("--ffplay", action="store_true", help="also open a native ffplay window (disables seamless swaps)")
    ap.add_argument("--port", type=int, default=8080)
    ap.add_argument("--workdir", default=os.environ.get("DEMO_DIR", "/tmp/palindrome-crt-demo"))
    a = ap.parse_args()

    work = Path(a.workdir)
    work.mkdir(parents=True, exist_ok=True)
    card = work / "testcard.png"
    subprocess.run([sys.executable, str(HERE.parent / "encoder" / "make_testcard.py"), str(card)], check=True,
                   stdout=subprocess.DEVNULL)
    sources = {"test card": CardSource(Image.open(card).convert("RGB"), not a.no_overlays)}
    first = "test card"
    if a.image:
        sources["image"] = CardSource(Image.open(a.image).convert("RGB"), not a.no_overlays)
        first = "image"
    if a.lavfi:
        sources["lavfi"] = FfmpegSource(["-re", "-f", "lavfi", "-i", f"{a.lavfi}=size=768x576:rate=25"])
        first = "lavfi"
    if a.video:
        sources["video"] = FfmpegSource(["-re", "-stream_loop", "-1", "-i", a.video])
        first = "video"
    if a.camera is not None:
        sources["camera"] = FfmpegSource(["-f", "avfoundation", "-framerate", "30", "-i", f"{a.camera}:none"])
        first = "camera"

    tv = Television(a.palindrome, a.ffplay)
    print("live_tv: precomputing the PAL line structure ...", file=sys.stderr)
    station = Station(tv, sources, first)
    tv.launch(seamless=False)
    station.start()
    server = ThreadingHTTPServer(("127.0.0.1", a.port), make_handler(tv, station))
    server.daemon_threads = True
    print(f"live_tv: open http://127.0.0.1:{a.port}/   (Ctrl-C to stop)", file=sys.stderr)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        tv.shutdown()


if __name__ == "__main__":
    main()
