#!/bin/bash
# build_tests.sh - build upstream's Catch2 unit-test executable (lib/test/*Test.cpp + every
# lib/*.cpp + lodepng + Catch2 3.8.0 from source) with zig c++, to check that the clang/libc++
# build BEHAVES like upstream's GCC/libstdc++ one, not merely that it compiles.
#   ./build_tests.sh <variant> [corpus-dir-as-seen-at-runtime]
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
export ZIG_GLOBAL_CACHE_DIR="$HERE/zig-cache/global" ZIG_LOCAL_CACHE_DIR="$HERE/zig-cache/local"
VARIANT="${1:?variant}"; CORPUS="${2:-}"
SRCROOT="${SRCROOT:-$HERE/src}"   # SRCROOT=$HERE/orig builds the UNPATCHED upstream tree
TAG="${TAG:-}"
case "$VARIANT" in
  macos-arm64)    TARGET="-target aarch64-macos"; EXE=palindrome_test ;;
  linux-x64-v3)   TARGET="-target x86_64-linux-gnu.2.28 -mcpu=x86_64_v3"; EXE=palindrome_test ;;
  linux-x64-base) TARGET="-target x86_64-linux-gnu.2.28 -mcpu=x86_64"; EXE=palindrome_test ;;
  win-x64-v3)     TARGET="-target x86_64-windows-gnu -mcpu=x86_64_v3"; EXE=palindrome_test.exe ;;
  *) echo "unknown variant" >&2; exit 2 ;;
esac
OUT="$HERE/out/tests-$VARIANT$TAG"; rm -rf "$OUT"; mkdir -p "$OUT/obj/catch" "$OUT/obj/lib" "$OUT/obj/test"
STD="-std=c++26 -O2"
INC="-I$SRCROOT/lib/include -isystem $HERE/deps/stdexec/include -isystem $HERE/deps/json/include -isystem $HERE/deps/lodepng -isystem $HERE/deps/Catch2/src -isystem $HERE/deps/catch2-gen"
DEFS="-DCATCH_CONFIG_ENABLE_ALL_STRINGMAKERS"
[ -n "$CORPUS" ] && DEFS="$DEFS -DPALINDROME_CORPUS_DIR=\"$CORPUS\""
export TARGET STD INC DEFS OUT
one() { # one <kind> <src>
  local kind="$1" src="$2" obj
  obj="$OUT/obj/$kind/$(echo "$src" | sed "s#.*/src/catch2/##; s#/#_#g; s#\.cpp\$##").o"
  # shellcheck disable=SC2086
  if ! zig c++ $TARGET $STD $INC $DEFS -c "$src" -o "$obj" > "$obj.log" 2>&1; then echo "FAIL $kind $(basename "$src")"; fi
}
export -f one
{ find "$HERE/deps/Catch2/src/catch2" -name '*.cpp' | sed 's/^/catch /'
  for f in "$SRCROOT"/lib/*.cpp "$HERE/deps/lodepng/lodepng.cpp"; do echo "lib $f"; done
  for f in "$SRCROOT"/lib/test/*Test.cpp; do echo "test $f"; done
} | xargs -P 10 -L 1 bash -c 'one "$@"' _
# shellcheck disable=SC2086
zig c++ $TARGET -O2 "$OUT"/obj/catch/*.o "$OUT"/obj/lib/*.o "$OUT"/obj/test/*.o -o "$OUT/$EXE"
echo "built $OUT/$EXE ($(stat -f %z "$OUT/$EXE") bytes); failed TUs: $(find "$OUT/obj" -name '*.log' -size +0 -exec grep -l 'error:' {} + 2>/dev/null | wc -l | tr -d ' ')"
