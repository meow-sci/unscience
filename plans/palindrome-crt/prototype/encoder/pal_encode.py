#!/usr/bin/env python3
"""Prototype RGB image -> PAL composite (CVBS) encoder, for feeding PALindrome's
`render --input composite`.  Feasibility probe only: numpy, not real time.

Signal convention (what PALindrome's CompositeInput expects, volts as float):
  sync tip -0.3 V, blanking/black 0 V, peak white +0.7 V, burst +/-0.15 V.
Written as raw little-endian int16 (CLI scales s16 by 1/32768 -> 1.0 == 1 V).

Timing: 625/50 interlaced, line-locked sample clock (default 16 MS/s = exactly
1024 samples per 64 us line, 640000 samples per frame).  Subcarrier is a free
running NCO at 4433618.75 Hz, phase continuous across lines/fields/frames; the
PAL V-switch toggles every line continuously (625 is odd, so it naturally
inverts frame to frame, as in the real 8-field sequence).

Colour math follows the (MIT-licensed) mattgodbolt/pal-decoder JS encoder:
  chroma = U sin(wt) + s V cos(wt), burst = A/sqrt2 (-sin(wt) + s cos(wt)).
"""
import argparse
import numpy as np
from PIL import Image

FSC = 4433618.75
LINE_HZ = 15625.0
LINES = 625

SYNC_V = -0.3
WHITE_V = 0.7
BURST_PEAK_V = 0.15

T_SYNC = 4.7e-6
T_EQ = 2.35e-6
T_BROAD_GAP = 4.7e-6          # serration: broad pulse is (half line - 4.7us) low
T_ACTIVE_START = 10.4e-6      # sync leading edge -> active video (4.7 + 5.7)
T_ACTIVE = 52.0e-6
T_BURST_START = 5.6e-6
BURST_CYCLES = 10


def lowpass(taps, cutoff_hz, fs):
    n = np.arange(taps) - (taps - 1) / 2
    h = np.sinc(2 * cutoff_hz / fs * n) * np.hamming(taps)
    return (h / h.sum()).astype(np.float64)


def fir_rows(x, h):
    """Filter each row of x (rows x samples) with symmetric FIR h, same length."""
    pad = len(h) // 2
    xp = np.pad(x, ((0, 0), (pad, pad)), mode="edge")
    out = np.empty_like(x)
    for r in range(x.shape[0]):
        out[r] = np.convolve(xp[r], h, mode="valid")
    return out


def build_line_plan():
    """Per line (1-based): list of (start_fraction_of_line, kind) pulses and
    whether the line carries burst / which picture row it shows."""
    plan = {}
    for line in range(1, LINES + 1):
        plan[line] = {"pulses": [(0.0, "S")], "burst": True, "row": None}

    def halfslots(slots, kind):
        for (line, half) in slots:
            p = plan[line]
            if p.get("_vbi") is None:
                p["pulses"] = []
                p["_vbi"] = True
                p["burst"] = False
            p["pulses"].append((0.0 if half == "a" else 0.5, kind))

    # Field 1 vertical interval: 5 broad, 5 equalising.
    halfslots([(1, "a"), (1, "b"), (2, "a"), (2, "b"), (3, "a")], "B")
    halfslots([(3, "b"), (4, "a"), (4, "b"), (5, "a"), (5, "b")], "E")
    # End of field 1 / field 2 vertical interval (half-line offset).
    halfslots([(311, "a"), (311, "b"), (312, "a"), (312, "b"), (313, "a")], "E")
    halfslots([(313, "b"), (314, "a"), (314, "b"), (315, "a"), (315, "b")], "B")
    halfslots([(316, "a"), (316, "b"), (317, "a"), (317, "b"), (318, "a")], "E")
    # End of field 2: pre-equalising for the next field 1.  Line 623 keeps its
    # normal sync at 'a' (it is a picture half line) and gains an eq pulse at 'b'.
    plan[623]["pulses"] = [(0.0, "S"), (0.5, "E")]
    halfslots([(624, "a"), (624, "b"), (625, "a"), (625, "b")], "E")

    # Picture rows: field 1 lines 23..310 -> even rows 0,2,..574 ;
    #               field 2 lines 336..623 -> odd rows 1,3,..575.
    for i, line in enumerate(range(23, 311)):
        plan[line]["row"] = 2 * i
    for i, line in enumerate(range(336, 624)):
        plan[line]["row"] = 2 * i + 1
    return plan


def encode(img_rgb, fs, n_frames, y_cut=5.0e6, uv_cut=1.3e6, edge_taps=5):
    spl = fs / LINE_HZ
    assert abs(spl - round(spl)) < 1e-9, "use a line-locked sample rate"
    spl = int(round(spl))
    n_active = int(round(T_ACTIVE * fs))
    a0 = int(round(T_ACTIVE_START * fs))

    img = img_rgb.resize((n_active, 576), Image.LANCZOS)
    rgb = np.asarray(img, dtype=np.float64) / 255.0            # gamma-encoded R'G'B'
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    y = 0.299 * r + 0.587 * g + 0.114 * b
    u = 0.493 * (b - y)
    v = 0.877 * (r - y)
    y = fir_rows(y, lowpass(31, y_cut, fs))
    u = fir_rows(u, lowpass(41, uv_cut, fs))
    v = fir_rows(v, lowpass(41, uv_cut, fs))

    plan = build_line_plan()
    frame_len = spl * LINES

    # --- static parts of one frame: sync train, burst envelope, luma, U, V masks
    sync = np.zeros(frame_len)
    burst_env = np.zeros(frame_len)
    luma = np.zeros(frame_len)
    uu = np.zeros(frame_len)
    vv = np.zeros(frame_len)
    n_sync, n_eq = int(round(T_SYNC * fs)), int(round(T_EQ * fs))
    n_broad = spl // 2 - int(round(T_BROAD_GAP * fs))
    b0 = int(round(T_BURST_START * fs))
    nb = int(round(BURST_CYCLES / FSC * fs))
    for line in range(1, LINES + 1):
        base = (line - 1) * spl
        p = plan[line]
        for frac, kind in p["pulses"]:
            s = base + int(round(frac * spl))
            width = {"S": n_sync, "E": n_eq, "B": n_broad}[kind]
            sync[s:s + width] = 1.0
        if p["burst"]:
            burst_env[base + b0: base + b0 + nb] = 1.0
        if p["row"] is not None:
            row = p["row"]
            lo, hi = 0, n_active
            if line == 23:                       # half lines, as broadcast
                lo = n_active // 2
            if line == 623:
                hi = n_active // 2
            luma[base + a0 + lo: base + a0 + hi] = y[row, lo:hi]
            uu[base + a0 + lo: base + a0 + hi] = u[row, lo:hi]
            vv[base + a0 + lo: base + a0 + hi] = v[row, lo:hi]

    # Monotonic (no overshoot) edges: smooth with a non-negative Hann kernel.
    k = np.hanning(edge_taps + 2)[1:-1]
    k /= k.sum()
    sync = np.convolve(sync, k, mode="same")
    burst_env = np.convolve(burst_env, np.hanning(9)[1:-1] / np.hanning(9)[1:-1].sum(), mode="same")

    out = np.empty(frame_len * n_frames, dtype=np.float32)
    line_idx = np.arange(frame_len) // spl
    for f in range(n_frames):
        n = np.arange(frame_len, dtype=np.float64) + f * frame_len
        theta = 2 * np.pi * np.mod(FSC / fs * n, 1.0)
        s, c = np.sin(theta), np.cos(theta)
        vsw = np.where(((line_idx + f * LINES) & 1) == 0, 1.0, -1.0)   # continuous PAL switch
        chroma = uu * s + vsw * vv * c
        burst = BURST_PEAK_V / np.sqrt(2.0) * burst_env * (-s + vsw * c)
        sig = SYNC_V * sync + WHITE_V * (luma + chroma) + burst
        out[f * frame_len:(f + 1) * frame_len] = sig
    return out


def degrade(sig, fs, noise_rms=0.0, ghost=0.0, ghost_us=1.2, hum=0.0, seed=1):
    out = sig.astype(np.float64)
    if ghost:
        d = int(round(ghost_us * 1e-6 * fs))
        g = np.zeros_like(out)
        g[d:] = out[:-d]
        out = out + ghost * g
    if hum:
        n = np.arange(len(out))
        out = out + hum * np.sin(2 * np.pi * 50.0 * n / fs)
    if noise_rms:
        rng = np.random.default_rng(seed)
        out = out + rng.normal(0.0, noise_rms, len(out))
    return out.astype(np.float32)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("image")
    ap.add_argument("output")
    ap.add_argument("--fs", type=float, default=16e6)
    ap.add_argument("--frames", type=int, default=12)
    ap.add_argument("--noise", type=float, default=0.0, help="additive gaussian noise, volts RMS")
    ap.add_argument("--ghost", type=float, default=0.0, help="ghost amplitude (0..1)")
    ap.add_argument("--ghost-us", type=float, default=1.2)
    ap.add_argument("--hum", type=float, default=0.0, help="50 Hz hum amplitude, volts")
    ap.add_argument("--dropout", type=float, default=0.0,
                    help="replace the first N seconds with pure noise (signal appears afterwards)")
    a = ap.parse_args()

    img = Image.open(a.image).convert("RGB")
    sig = encode(img, a.fs, a.frames)
    sig = degrade(sig, a.fs, a.noise, a.ghost, a.ghost_us, a.hum)
    if a.dropout:
        n = int(a.dropout * a.fs)
        rng = np.random.default_rng(7)
        sig[:n] = rng.normal(0.2, 0.25, n).astype(np.float32)
    pcm = np.clip(np.round(sig * 32768.0), -32768, 32767).astype("<i2")
    pcm.tofile(a.output)
    print(f"{a.output}: {len(pcm)} samples @ {a.fs/1e6:g} MS/s = {len(pcm)/a.fs:.3f} s, "
          f"min {sig.min():+.3f} V max {sig.max():+.3f} V")


if __name__ == "__main__":
    main()
