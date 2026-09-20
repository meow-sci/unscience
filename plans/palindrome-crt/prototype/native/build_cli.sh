#!/bin/bash
# build_cli.sh - build a fast native `palindrome` CLI for the demos, from YOUR PALindrome checkout.
#
#   ./build_cli.sh            # -> .work/bin/palindrome   (demo.sh and live/live_tv.sh pick it up)
#
#   PALINDROME_SRC=~/repos/github/PALindrome   the checkout to copy (never modified)
#   WORK=<dir>                                 where the copy, deps and objects go (default ./.work, git-ignored)
#
# No CMake: `zig c++` compiles lib/*.cpp + cli/*.cpp + lodepng directly, without upstream's
# -Werror, so the portability edits of RESEARCH 6.4 are unnecessary. Two pure additions are
# applied to the COPY, by line number, which is why the commit is pinned:
#   * `workers_.clear();` in ~WorkQueue  - upstream's teardown otherwise hangs on macOS
#   * bench/neon-fir-experiment.U0.patch - on arm64 only; without it the FIRs run scalar and the
#     decoder is ~5x too slow for live use. x86-64 uses upstream's own AVX2 kernels instead.
# Nothing from PALindrome is stored in this repository; it has no license yet (RESEARCH 3).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
SRC_REPO="${PALINDROME_SRC:-$HOME/repos/github/PALindrome}"
WORK="${WORK:-$HERE/.work}"
PIN=03da0f5d901874b4cd4ac85b48ffbd46e543a46a
ZIG="${ZIG:-zig}"
export ZIG_GLOBAL_CACHE_DIR="$WORK/zig-cache/global" ZIG_LOCAL_CACHE_DIR="$WORK/zig-cache/local"

[ -d "$SRC_REPO/lib" ] || { echo "PALINDROME_SRC=$SRC_REPO is not a PALindrome checkout" >&2; exit 2; }
HEAD_SHA="$(git -C "$SRC_REPO" rev-parse HEAD 2>/dev/null || echo unknown)"
if [ "$HEAD_SHA" != "$PIN" ]; then
  echo "warning: checkout is at $HEAD_SHA, the line-number patches were made for $PIN" >&2
  echo "         (git -C $SRC_REPO checkout $PIN) - continuing, but verify the result" >&2
fi

mkdir -p "$WORK/obj" "$WORK/bin" "$WORK/deps"
rsync -a --delete --exclude .git --exclude corpus --exclude build "$SRC_REPO/" "$WORK/src/"

# --- the two pure-addition edits, on the copy only
WQ="$WORK/src/lib/include/palindrome/work_queue.hpp"
grep -q 'workers_.clear();' "$WQ" || sed -i.orig '43a\
    workers_.clear(); // join before the mutex/condvars are destroyed (members are declared in the wrong order)
' "$WQ"
ARCH="$(uname -m)"
if [ "$ARCH" = arm64 ] || [ "$ARCH" = aarch64 ]; then
  (cd "$WORK/src" && patch -p1 --forward --silent < "$HERE/bench/neon-fir-experiment.U0.patch")
  CPU=(-mcpu=native)
else
  CPU=(-mcpu=x86_64_v3)            # AVX2 + FMA: upstream's hand-vectorised FIR tiers
fi

# --- pinned dependencies (headers + lodepng.cpp), shallow
fetch() { # fetch <dir> <url> <rev>
  [ -d "$WORK/deps/$1/.git" ] && return 0
  git init -q "$WORK/deps/$1"
  git -C "$WORK/deps/$1" remote add origin "$2"
  git -C "$WORK/deps/$1" fetch -q --depth 1 origin "$3"
  git -C "$WORK/deps/$1" checkout -q FETCH_HEAD
}
fetch stdexec https://github.com/NVIDIA/stdexec.git 02d671da624daafc63dc42f60bfba40f97161400
fetch json    https://github.com/nlohmann/json.git  v3.11.3
fetch lodepng https://github.com/lvandeve/lodepng.git 22561883dd63fd1850f18e1f6adac321e4f609b0
fetch lyra    https://github.com/bfgroup/Lyra.git   5a4ef1abaf7139ef00204992a2627127ad394499

FLAGS=(-std=c++26 -O3 "${CPU[@]}" -I"$WORK/src/lib/include" -isystem "$WORK/deps/stdexec/include"
       -isystem "$WORK/deps/json/include" -isystem "$WORK/deps/lodepng" -isystem "$WORK/deps/lyra/include" -w)
export ZIG WORK
compile() { # compile <src>
  local obj="$WORK/obj/$(echo "$1" | sed "s#$WORK/##; s#/#_#g; s#\.cpp\$##").o"
  [ "$obj" -nt "$1" ] && return 0
  "$ZIG" c++ "${@:2}" -c "$1" -o "$obj" || { echo "FAILED: $1" >&2; exit 1; }
}
export -f compile
printf '%s\n' "$WORK"/src/lib/*.cpp "$WORK"/src/cli/*.cpp "$WORK/deps/lodepng/lodepng.cpp" |
  xargs -P "$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)" -I{} bash -c 'compile "$@"' _ {} "${FLAGS[@]}"
"$ZIG" c++ -O3 "${CPU[@]}" "$WORK"/obj/*.o -o "$WORK/bin/palindrome"
echo "built $WORK/bin/palindrome"
"$WORK/bin/palindrome" info "$SRC_REPO/corpus/wb3_airspy" 2>/dev/null | head -3 || true
