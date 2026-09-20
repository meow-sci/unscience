# PALindrome CRT — research prototypes

Throwaway probes that back the claims in [`../RESEARCH.md`](../RESEARCH.md). Nothing here is
built by the Unscience solution, shipped in the mod, or referenced by any `.csproj` in the
repository. **No PALindrome source code is stored here** — PALindrome currently has no license
(see RESEARCH §3), so the probes expect you to supply your own checkout.

## Start here

```sh
native/build_cli.sh          # once, ~15 s: builds a fast `palindrome` CLI from your PALindrome checkout into native/.work/ (git-ignored)
live/live_tv.sh              # a continuously running television: open http://127.0.0.1:8080/
./demo.sh some.png           # single still: any image -> PAL -> PALindrome -> PNG
```

## `live/` — a continuously running PAL television (browser, real time)

`live_tv.sh` starts `live_tv.py`: a moving source (test card with clock, bouncing ball and ticker;
or `--image`, `--video FILE`, `--lavfi testsrc2`, `--camera 0`) → a **real-time numpy PAL encoder**
(625/50 interlaced, 16 MS/s, ~12 ms per frame) → `palindrome render --live --input composite` in
its own process (the real decoder and CRT model) → raw RGB fields on a pipe → MJPEG in the browser
(`--ffplay` adds a native window). Measured on an M4 Pro: 25 frames/s fed, **50 fields/s decoded**,
decoder at 80–100 % of one core.

Two kinds of control on the page (hover any label for what it does):

- **Signal** (9 controls, applied instantly by the encoder): noise, ghost and delay, hum, level,
  black level/"brightness" (PALindrome has no brightness pot, so this lifts the picture above blanking
  at the source), source saturation, **chroma phase error** (with comb mode *off* it becomes Hanover
  bars; with a comb it cancels into slight desaturation — PAL's whole trick, live), burst amplitude
  (0 = the colour killer takes the picture to monochrome), plus *unplug the aerial* (monochrome snow,
  then re-lock with colour fading back) and *white flash* (limiters and raster breathing).
- **The set** (47 of PALindrome's own `render` flags, grouped): picture (contrast, saturation, gun and
  readout gamma, overscan, centring), tube (raster, persistence, beam spot), supply and limiters
  (**blooming** = EHT sag and defocus, line pull, beam-current and peak-white limiters), colour
  decoder (comb mode, killer, U/V bandwidth, crystal offset, APC, ident, burst gate), sync and
  timebases (AGC scheme, slice depth, H/V hold gains), input stage. "Looks" presets: studio monitor,
  visible scanlines, tired old set, contrast cranked, PAL-S.

PALindrome's configuration is fixed at construction, so every set control means a new decoder
process. With **seamless** ticked the new set is warmed up beside the old one for 30 fields and
swapped in once it has locked; untick it (or press *Power cycle*) to watch the honest re-lock. A
combination the decoder rejects is reported on the page and reverted. Binds to 127.0.0.1 only.
Needs `uv`; ffmpeg only for the ffmpeg sources.

## `demo.sh` — one command: any image → PAL → PALindrome → PNG

```sh
./demo.sh                                                          # generated test card
./demo.sh ~/Pictures/ksa.png                                       # your own image
./demo.sh ~/Pictures/ksa.png --noise 0.012 --ghost 0.3 --ghost-us 1.6   # weak signal
SWITCH_ON=1 ./demo.sh                                              # plus one PNG per field (set warming up)
```

Creates its Python venv and all outputs in a temp directory (`DEMO_DIR` overrides), writes nothing
into the repository, and opens the decoded PNG. Needs `uv` and the CLI from `native/build_cli.sh`
(or `PALINDROME_BIN=/path/to/palindrome`).

## `encoder/` — RGB image → PAL composite, decoded by PALindrome

| File | Purpose |
|---|---|
| `make_testcard.py` | Draws a 768×576 test card (75 % bars, 100 % primaries, ramp, staircase, multiburst, circle, text). |
| `pal_encode.py` | Encodes any image into 625/50 interlaced PAL CVBS at a line-locked 16 MS/s (1024 samples/line) as raw `int16`; optional noise, ghost, hum and no-signal lead-in. numpy, not real time — a correctness probe, not the production encoder. |

Colour math follows Matt Godbolt's MIT-licensed JavaScript encoder
([mattgodbolt/pal-decoder](https://github.com/mattgodbolt/pal-decoder), `src/encoder.js`);
timing follows ITU-R BT.470 System I.

```sh
uv venv .venv && uv pip install --python .venv/bin/python numpy pillow
.venv/bin/python make_testcard.py testcard.png
.venv/bin/python pal_encode.py testcard.png testcard.sigmf-data --frames 12
cat > testcard.sigmf-meta <<'EOF'
{ "global": { "core:datatype": "ri16_le", "core:sample_rate": 16000000, "core:version": "1.2.0" },
  "captures": [ { "core:sample_start": 0 } ], "annotations": [] }
EOF
# `render` only accepts SigMF pairs for files (headerless raw is live/stdin only), and its
# argument parser treats a dot in the stem as an extension: keep stems dot-free.
palindrome render testcard --input composite --colour \
    --saturation 0.214 --contrast 1.0 --burst-lo 0.116 --burst-hi 0.151 --h-shift 0.03 \
    -o decoded.png
# add --frame-stride 1 for one PNG per field (the switch-on sequence)
```

## `native/` — shared library + C ABI + P/Invoke, cross-compiled with `zig c++`

| File | Purpose |
|---|---|
| `build_cli.sh` | Builds the upstream **CLI** natively for the demos (`zig c++`, no CMake, no `-Werror`), from a copy of your checkout with two pure-addition edits applied by line number: the `WorkQueue` teardown fix and, on arm64, the NEON FIR patch. Output `native/.work/bin/palindrome`; as fast as the CMake/LTO build and pixel-identical to it. |
| `build.sh` | Hand-written replacement for upstream CMake: compiles the 12 decoder-path translation units plus the shim into `libpalindrome.so` / `palindrome.dll` / `libpalindrome.dylib`. Variants: `linux-x64-v3`, `linux-x64-base`, `win-x64-v3`, `win-x64-base`, `macos-arm64`. |
| `build_tests.sh` | Builds upstream's Catch2 unit tests with the same toolchain so behaviour, not just compilation, is checked. |
| `shim/palindrome_c.h`, `palindrome_c.cpp` | Experimental C ABI: version, build info, last-error, and a smoke entry point that runs the real `video::Decoder`. Catches every C++ exception at the boundary. Not the production API (see RESEARCH Appendix B). |
| `shim/pal_cpu.cpp` | `pal_cpu_supported()` — CPUID/XGETBV check for x86-64-v3, always compiled at the baseline ISA so it is safe to call first. |
| `shim/pal_synth.hpp` | Minimal C++ colour-bar CVBS generator used by the smoke entry point. |
| `test/smoke.c`, `test/smoke.py` | `dlopen` / `ctypes` loaders. |
| `test/dotnet/` | .NET 10 P/Invoke loader (`dotnet run -c Release -- <path to native lib>`). Carries an empty `Directory.Build.props` so the repository-wide build settings do not apply. |

### `native/bench/` — the numbers in RESEARCH §5.2

| File | Purpose |
|---|---|
| `stage_bench.cpp` | Single-thread, per-stage timing of the library driven the way the mod would embed it (no CLI, no stdexec): int16 CVBS file → s16→float → optional 127-tap low-pass → `CompositeInput` → `decode_into` → `deposit` → `FieldEvent::frame()`. Prints milliseconds of CPU per second of video per stage and the worst block time. |
| `neon-fir-experiment.U0.patch` | **Measurement aid for Apple Silicon only, not for shipping.** Adds NEON tiers beside upstream's AVX2-only FIR kernels in `lib/fir.cpp` (bit-identical output; upstream's unit tests pass). Zero-context diff of pure additions against commit `03da0f5` (`patch -p1 < …` inside the PALindrome copy). Without it an ARM build runs the scalar tier, 5–7× too slow to say anything about x86. |

```sh
# after a CMake build of the (optionally NEON-patched) copy, see RESEARCH Appendix A
clang++ -std=gnu++26 -O3 -mcpu=native -flto=thin -I src/lib/include \
    bench/stage_bench.cpp build/lib/libpalindrome.a -o stage_bench      # x86: -march=x86-64-v3
./stage_bench capture.sigmf-data 16e6 <colour 0|1> <lanes> 720 576 <frame stride> <vision LP 0|1> 65536 [sync volts]
# e.g. the prototype encoder's output (sync amplitude 0.3 V):
./stage_bench testcard.sigmf-data 16e6 1 1 720 576 2 1 65536 0.3
# 16 MS/s colour, 1 lane, 25 frames/s on an M4 Pro: TOTAL ≈ 715–770 ms per second of video
```

**This is the measurement Phase 1 needs on a real x86 gaming PC** (build with AVX2 enabled; any
int16 CVBS file works, including the output of `encoder/pal_encode.py`).

Layout `build.sh` expects next to it (none of it is checked in):

```text
src/    rsync -a --exclude .git --exclude corpus --exclude build <PALindrome checkout>/ src/
deps/   stdexec  @ 02d671da624daafc63dc42f60bfba40f97161400   (NVIDIA/stdexec, headers only)
        json     @ v3.11.3                                    (nlohmann/json, headers only)
        lodepng  @ 22561883dd63fd1850f18e1f6adac321e4f609b0   (only for the `full` TU set)
```

Closing the biggest open verification item — the Windows DLL has never been executed:

```sh
./build.sh win-x64-v3
zig cc -target x86_64-windows-gnu -O2 test/smoke.c -o out/test/smoke-win-x64.exe
# copy out/win-x64-v3-embed/palindrome.dll + out/test/smoke-win-x64.exe to a Windows PC, then:
#   smoke-win-x64.exe palindrome.dll 10 1 4 1      (fields, colour, deposit lanes, threaded pipeline)
# expected: locked=1 ... fnv1a64=0x7f74d47edba8b595 (the value every other platform produced)
```

`build.sh` compiles upstream sources with upstream's own `-Werror` warning set. Under clang that
needs the four small edits listed in RESEARCH §6.4; without `-Werror` no source edit is needed
to compile. Tested with zig 0.16.0 (clang 21.1.8, libc++ 21.1) on macOS arm64.
