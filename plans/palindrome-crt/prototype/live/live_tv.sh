#!/bin/bash
# live_tv.sh - continuous PAL television demo in your browser, decoded by a native PALindrome.
#
#   PALINDROME_BIN=/path/to/palindrome ./live_tv.sh                    # animated test card
#   PALINDROME_BIN=… ./live_tv.sh --image ~/Pictures/ksa.png           # your picture (+ clock/ball/ticker)
#   PALINDROME_BIN=… ./live_tv.sh --video ~/Movies/clip.mp4            # looped video (ffmpeg)
#   PALINDROME_BIN=… ./live_tv.sh --lavfi testsrc2                     # ffmpeg generator (mandelbrot, life, smptebars…)
#   PALINDROME_BIN=… ./live_tv.sh --camera 0                           # webcam (macOS asks for camera permission)
#   PALINDROME_BIN=… ./live_tv.sh --ffplay                             # also a native window, no browser needed
#
# then open http://127.0.0.1:8080/ . On Apple Silicon the decoder must be built with the NEON
# measurement patch (native/bench/), otherwise it cannot keep up with real time; on x86 build
# with AVX2 enabled. Needs `uv`; ffmpeg only for --video/--lavfi/--camera/--ffplay.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
BIN="${PALINDROME_BIN:-}"
[ -n "$BIN" ] || { [ -x "$HERE/../native/.work/bin/palindrome" ] && BIN="$HERE/../native/.work/bin/palindrome"; } || BIN="$(command -v palindrome || true)"
[ -x "$BIN" ] || { echo "no palindrome CLI: run native/build_cli.sh first (or set PALINDROME_BIN)" >&2; exit 2; }
WORK="${DEMO_DIR:-${TMPDIR:-/tmp}/palindrome-crt-demo}"
mkdir -p "$WORK"
if [ ! -x "$WORK/venv/bin/python" ]; then
  uv venv --quiet "$WORK/venv"
  uv pip install --quiet --python "$WORK/venv/bin/python" numpy pillow
fi
exec "$WORK/venv/bin/python" "$HERE/live_tv.py" --palindrome "$BIN" --workdir "$WORK" "$@"
