#!/bin/bash
# demo.sh - the whole Rabbit Ears demo in one go: six PAL channels multiplexed into one wideband signal,
# a software tuner, the native PALindrome television decoder, and a web page to watch and tune it.
#
#   ./demo.sh                                    # six animated test cards
#   ./demo.sh --video ~/clip.mp4 --video b.mkv   # videos take CH1, CH2, ...; cards fill the rest (needs ffmpeg)
#   ./demo.sh --port 8095 --no-browser           # anything else is passed on to `RabbitEars live`
#
#   PALINDROME_BIN=<path>   use this decoder instead of building the prototype CLI
#   PALINDROME_SRC=<dir>    PALindrome checkout for the build (default ~/repos/github/PALindrome)
#
# PALindrome is a separate, unlicensed project: it is never bundled. The decoder is built from YOUR checkout by
# plans/palindrome-crt/prototype/native/build_cli.sh (needs zig; macOS / Linux).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/.." && pwd)"
NATIVE="$REPO/plans/palindrome-crt/prototype/native"
PALINDROME="${PALINDROME_BIN:-$NATIVE/.work/bin/palindrome}"
CHANNELS=6
CARDS=("card:bars:BARS ONE" "card:grid:GRID TWO" "card:hue:HUE THREE" "card:checker:CHECK FOUR" "card:steps:STEPS FIVE" "card:rays:RAYS SIX")

videos=()
passthrough=()
while [ $# -gt 0 ]; do
  case "$1" in
    --video) [ $# -ge 2 ] || { echo "demo.sh: --video needs a path" >&2; exit 2; }; videos+=("$2"); shift 2 ;;
    -h|--help) sed -n '2,13p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) passthrough+=("$1"); shift ;;
  esac
done
[ ${#videos[@]} -le $CHANNELS ] || { echo "demo.sh: at most $CHANNELS videos (one per channel)" >&2; exit 2; }

if [ ! -x "$PALINDROME" ]; then
  echo "demo.sh: building the PALindrome CLI (first run only) ..."
  "$NATIVE/build_cli.sh"
fi
[ -x "$PALINDROME" ] || { echo "demo.sh: no decoder at $PALINDROME" >&2; exit 1; }

echo "demo.sh: building rabbit-ears (Release) ..."
dotnet build "$HERE/rabbit-ears.csproj" -c Release --nologo -v quiet

channel_args=()
for ((i = 0; i < CHANNELS; i++)); do
  if [ $i -lt ${#videos[@]} ]; then
    [ -f "${videos[$i]}" ] || { echo "demo.sh: no such video: ${videos[$i]}" >&2; exit 2; }
    channel_args+=(--channel "video:${videos[$i]}")
  else
    channel_args+=(--channel "${CARDS[$i]}")
  fi
done

echo "demo.sh: starting - the page opens at http://127.0.0.1:8090/ unless --port / --no-browser say otherwise (Ctrl-C stops everything)"
exec "$HERE/bin/Release/net10.0/RabbitEars" live "${channel_args[@]}" --palindrome "$PALINDROME" ${passthrough[@]+"${passthrough[@]}"}
