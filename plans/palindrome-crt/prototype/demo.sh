#!/bin/bash
# demo.sh - any image -> PAL composite -> PALindrome -> PNG, then open the result.
#
#   PALINDROME_BIN=/path/to/palindrome ./demo.sh [image] [pal_encode options...]
#
#   ./demo.sh                                   # generated test card, clean signal
#   ./demo.sh ~/Pictures/ksa.png                # your own image
#   ./demo.sh ~/Pictures/ksa.png --noise 0.012 --ghost 0.3 --ghost-us 1.6    # weak signal
#   SWITCH_ON=1 ./demo.sh                       # also write one PNG per field (the set warming up)
#
# Needs: a built `palindrome` CLI (RESEARCH.md Appendix A), `uv`. Works in a temp dir; writes
# nothing into the repository.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
BIN="${PALINDROME_BIN:-}"
[ -n "$BIN" ] || { [ -x "$HERE/native/.work/bin/palindrome" ] && BIN="$HERE/native/.work/bin/palindrome"; } || BIN="$(command -v palindrome || true)"
[ -x "$BIN" ] || { echo "no palindrome CLI: run native/build_cli.sh first (or set PALINDROME_BIN)" >&2; exit 2; }

WORK="${DEMO_DIR:-${TMPDIR:-/tmp}/palindrome-crt-demo}"
mkdir -p "$WORK"
if [ ! -x "$WORK/venv/bin/python" ]; then
  uv venv --quiet "$WORK/venv"
  uv pip install --quiet --python "$WORK/venv/bin/python" numpy pillow
fi
PY="$WORK/venv/bin/python"

IMAGE="${1:-}"
[ $# -gt 0 ] && shift
if [ -z "$IMAGE" ]; then
  IMAGE="$WORK/testcard.png"
  "$PY" "$HERE/encoder/make_testcard.py" "$IMAGE" > /dev/null
fi

STEM="$WORK/signal"          # no dots in the stem: the CLI treats them as an extension
"$PY" "$HERE/encoder/pal_encode.py" "$IMAGE" "$STEM.sigmf-data" --frames "${FRAMES:-12}" "$@"
cat > "$STEM.sigmf-meta" <<'EOF'
{ "global": { "core:datatype": "ri16_le", "core:sample_rate": 16000000, "core:version": "1.2.0" },
  "captures": [ { "core:sample_start": 0 } ], "annotations": [] }
EOF

TUNING=(--input composite --colour --saturation 0.214 --contrast 1.0
        --burst-lo 0.116 --burst-hi 0.151 --h-shift 0.03)
/usr/bin/time -p "$BIN" render "$STEM" "${TUNING[@]}" -o "$WORK/decoded.png"
if [ -n "${SWITCH_ON:-}" ]; then
  mkdir -p "$WORK/fields"
  "$BIN" render "$STEM" "${TUNING[@]}" --frame-stride 1 -o "$WORK/fields/f.png" > /dev/null
  echo "per-field frames: $WORK/fields/"
fi
echo "decoded: $WORK/decoded.png"
command -v open > /dev/null && open "$WORK/decoded.png"
