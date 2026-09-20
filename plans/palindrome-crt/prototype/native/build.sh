#!/bin/bash
# build.sh - cross-build PALindrome's lib/ + the C ABI shim as a shared library with `zig c++`.
#
#   ./build.sh <variant> [embed|full] [extra compile flags...]
#
# variants:  macos-arm64 | linux-x64-v3 | linux-x64-base | win-x64-v3 | win-x64-base
# TU sets:   embed = decoder/CRT path only (default)   full = every lib/*.cpp + lodepng
#
# (bash 3.2-safe: macOS /bin/bash treats empty arrays as unbound under `set -u`.)
#
# No CMake: upstream's CMake only contributes (a) the TU list, (b) -std=c++26, (c) -march,
# (d) include dirs for three header/single-file deps. All four are reproduced below.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="$HERE/src"
DEPS="$HERE/deps"
SHIM="$HERE/shim"
export ZIG_GLOBAL_CACHE_DIR="$HERE/zig-cache/global"
export ZIG_LOCAL_CACHE_DIR="$HERE/zig-cache/local"
ZIG="${ZIG:-zig}"

VARIANT="${1:?variant}"
SET="${2:-embed}"
shift $(( $# >= 2 ? 2 : 1 ))
EXTRA=("$@")

case "$VARIANT" in
  macos-arm64)    TARGET=(-target aarch64-macos);                         BASE_CPU=();               OUTNAME=libpalindrome.dylib; TIER=0 ;;
  linux-x64-v3)   TARGET=(-target x86_64-linux-gnu.2.28 -mcpu=x86_64_v3); BASE_CPU=(-mcpu=x86_64);   OUTNAME=libpalindrome.so;    TIER=3 ;;
  linux-x64-base) TARGET=(-target x86_64-linux-gnu.2.28 -mcpu=x86_64);    BASE_CPU=(-mcpu=x86_64);   OUTNAME=libpalindrome.so;    TIER=1 ;;
  win-x64-v3)     TARGET=(-target x86_64-windows-gnu -mcpu=x86_64_v3);    BASE_CPU=(-mcpu=x86_64);   OUTNAME=palindrome.dll;      TIER=3 ;;
  win-x64-base)   TARGET=(-target x86_64-windows-gnu -mcpu=x86_64);       BASE_CPU=(-mcpu=x86_64);   OUTNAME=palindrome.dll;      TIER=1 ;;
  *) echo "unknown variant $VARIANT" >&2; exit 2 ;;
esac

# The decoder/CRT path (link closure of video::Decoder + CompositeInput, verified with nm).
EMBED_TUS=(decoder agc fir sync_separator horizontal_sweep vertical_sync chroma_decoder screen
           cpu_deposit splat gaussian composite)
# Everything upstream's lib/CMakeLists.txt lists: adds the RF front end (demod mixer biquad
# dc_blocker fft) and the file-format helpers (sigmf wav image -> nlohmann_json, lodepng).
FULL_TUS=("${EMBED_TUS[@]}" demod mixer biquad dc_blocker fft sigmf wav image)

if [ "$SET" = full ]; then TUS=("${FULL_TUS[@]}"); else TUS=("${EMBED_TUS[@]}"); fi

OUT="$HERE/out/$VARIANT-$SET${OUTSUFFIX:-}"   # OUTSUFFIX=-lto etc. keeps experiment outputs apart
OBJ="$OUT/obj"
rm -rf "$OUT"; mkdir -p "$OBJ"

COMMON=(-std=c++26 -O2 -fPIC -fvisibility=hidden -fvisibility-inlines-hidden
        -ffunction-sections -fdata-sections
        -I"$SRC/lib/include" -isystem "$DEPS/stdexec/include"
        -isystem "$DEPS/json/include" -isystem "$DEPS/lodepng")

# Upstream's opt::pedantic set (cmake/pedantic.cmake) minus the three GCC-only flags
# (-Wduplicated-cond -Wduplicated-branches -Wlogical-op), as errors, on upstream TUs only:
# the tripwire that tells us when a new upstream commit stops being clang-clean.
PEDANTIC=(-Wall -Wextra -pedantic -Wshadow -Wconversion -Werror -Wnull-dereference
          -Wnon-virtual-dtor -Wsuggest-override -Wdouble-promotion)

cc() { # cc <out.o> <src> <flags...>
  local o="$1" s="$2"; shift 2
  "$ZIG" c++ "${TARGET[@]}" "${COMMON[@]}" ${EXTRA[@]+"${EXTRA[@]}"} "$@" -c "$s" -o "$o"
}

pids=()
for tu in "${TUS[@]}"; do
  cc "$OBJ/$tu.o" "$SRC/lib/$tu.cpp" "${PEDANTIC[@]}" &
  pids+=($!)
done
if [ "$SET" = full ]; then
  "$ZIG" c++ "${TARGET[@]}" -std=c++26 -O2 -fPIC -fvisibility=hidden -ffunction-sections -fdata-sections \
      -c "$DEPS/lodepng/lodepng.cpp" -o "$OBJ/lodepng.o" & pids+=($!)
fi
cc "$OBJ/palindrome_c.o" "$SHIM/palindrome_c.cpp" -I"$SHIM" -DPAL_BUILDING_DLL -DPAL_WITH_STDEXEC \
   "-DPAL_TARGET=\"$VARIANT\"" & pids+=($!)
# The CPU guard is compiled at the BASELINE ISA whatever the variant (last -mcpu wins).
cc "$OBJ/pal_cpu.o" "$SHIM/pal_cpu.cpp" -I"$SHIM" -DPAL_BUILDING_DLL -DPAL_ISA_TIER=$TIER ${BASE_CPU[@]+"${BASE_CPU[@]}"} & pids+=($!)
for p in "${pids[@]}"; do wait "$p"; done

LINK=(-shared)
case "$VARIANT" in
  # zig's Mach-O linker ignores -exported_symbols_list and rejects -exported_symbol, so the
  # static libc++'s operator new/delete stay exported next to pal_* (dev-machine build only).
  macos-*) LINK+=(-Wl,-install_name,@rpath/$OUTNAME -Wl,-dead_strip) ;;
  linux-*) printf 'PALINDROME_1 {\n  global: pal_*;\n  local: *;\n};\n' > "$OUT/exports.map"
           LINK+=(-Wl,-soname,$OUTNAME -Wl,--version-script="$OUT/exports.map" -Wl,--gc-sections
                  -Wl,-z,relro -Wl,-z,now -Wl,--as-needed) ;;
  # Exports come from __declspec(dllexport) alone (that also switches off MinGW's export-all).
  # Without --out-implib zig names the import library after the first object (agc.lib).
  win-*)   LINK+=(-Wl,--gc-sections -Wl,--out-implib,"$OUT/palindrome.lib") ;;
esac

# Link twice: an unstripped copy for disassembly/analysis, and the release artifact stripped
# AT LINK TIME (-s). `zig objcopy --strip-all` is not usable here: zig 0.16.0 answers
# "error: unimplemented" for ELF and "invalid elf file: InvalidElfMagic" for PE.
"$ZIG" c++ "${TARGET[@]}" -O2 "${LINK[@]}" "$OBJ"/*.o -o "$OUT/unstripped-$OUTNAME"
case "$VARIANT" in
  macos-*) cp "$OUT/unstripped-$OUTNAME" "$OUT/$OUTNAME"; strip -x "$OUT/$OUTNAME" ;;
  *)       "$ZIG" c++ "${TARGET[@]}" -O2 "${LINK[@]}" -s "$OBJ"/*.o -o "$OUT/$OUTNAME" ;;
esac
find "$OUT" -maxdepth 1 -type f \( -name '*.so' -o -name '*.dll' -o -name '*.dylib' -o -name '*.lib' -o -name '*.pdb' \) \
  -exec stat -f '%10z  %N' {} + 2>/dev/null | sed "s#$OUT/##"
