/* smoke.c - load the shared library the way a P/Invoke host would (dlopen / LoadLibrary, no
 * import library), call the C ABI, print one line of results. Portable C99; build with zig cc
 * for any of the targets:  smoke <path-to-lib> [fields] [colour] [lanes] [threaded] */
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include "../shim/palindrome_c.h"

#if defined(_WIN32)
#include <windows.h>
typedef HMODULE lib_t;
static lib_t lib_open(const char *p) { return LoadLibraryA(p); }
static void *lib_sym(lib_t l, const char *n) { return (void *)GetProcAddress(l, n); }
static const char *lib_err(void) { static char b[64]; snprintf(b, sizeof b, "GetLastError=%lu", GetLastError()); return b; }
#else
#include <dlfcn.h>
typedef void *lib_t;
static lib_t lib_open(const char *p) { return dlopen(p, RTLD_NOW | RTLD_LOCAL); }
static void *lib_sym(lib_t l, const char *n) { return dlsym(l, n); }
static const char *lib_err(void) { return dlerror(); }
#endif

typedef uint32_t (PAL_CALL *abi_fn)(void);
typedef const char *(PAL_CALL *str_fn)(void);
typedef int32_t (PAL_CALL *i32_fn)(void);
typedef int32_t (PAL_CALL *decode_fn)(const pal_smoke_params *, pal_smoke_result *, uint8_t *, size_t);

int main(int argc, char **argv) {
  if (argc < 2) {
    fprintf(stderr, "usage: %s <lib> [fields] [colour] [lanes] [threaded]\n", argv[0]);
    return 2;
  }
  lib_t lib = lib_open(argv[1]);
  if (!lib) {
    fprintf(stderr, "load failed: %s\n", lib_err());
    return 3;
  }
  abi_fn abi = (abi_fn)lib_sym(lib, "pal_abi_version");
  str_fn info = (str_fn)lib_sym(lib, "pal_build_info");
  str_fn last_error = (str_fn)lib_sym(lib, "pal_last_error");
  i32_fn cpu = (i32_fn)lib_sym(lib, "pal_cpu_supported");
  i32_fn boom = (i32_fn)lib_sym(lib, "pal_smoke_exception");
  decode_fn decode = (decode_fn)lib_sym(lib, "pal_smoke_decode");
  if (!abi || !info || !last_error || !cpu || !boom || !decode) {
    fprintf(stderr, "missing export\n");
    return 4;
  }
  /* pal_cpu_supported() lives in a TU compiled at the baseline ISA, so it is the ONE call that
   * is safe on any x86-64. Everything else in a v3 build may use VEX/AVX2 anywhere (even in a
   * prologue), so a host must gate on it - observed: SIGILL in pal_smoke_exception on a
   * Nehalem-class CPU when this check was skipped. */
  if (cpu() == 0) {
    printf("cpu_supported=0 -> refusing to call into a library built for a newer ISA tier\n");
    return 5;
  }
  printf("abi=%u cpu_supported=%d\nbuild=%s\n", abi(), cpu(), info());
  int32_t rc = boom();
  printf("exception_rc=%d msg=\"%s\"\n", rc, last_error());

  pal_smoke_params p;
  memset(&p, 0, sizeof p);
  p.struct_size = sizeof p;
  p.width = 720;
  p.height = 576;
  p.fields = argc > 2 ? (uint32_t)atoi(argv[2]) : 8;
  p.colour = argc > 3 ? (uint32_t)atoi(argv[3]) : 1;
  p.deposit_lanes = argc > 4 ? (uint32_t)atoi(argv[4]) : 4;
  p.threaded_pipeline = argc > 5 ? (uint32_t)atoi(argv[5]) : 0;
  p.block_samples = 1u << 16;
  p.sample_rate_hz = 16e6;
  pal_smoke_result r;
  memset(&r, 0, sizeof r);
  r.struct_size = sizeof r;
  rc = decode(&p, &r, NULL, 0);
  if (rc != PAL_OK) {
    printf("decode_rc=%d err=\"%s\"\n", rc, last_error());
    return 1;
  }
  printf("decode_rc=0 locked=%d edges=%llu/%llu fields=%llu callbacks=%llu line_omega=%.12g "
         "subcarrier=%.3f burst=%.6f frame=%ux%ux%u sum=%llu fnv1a64=0x%016llx elapsed_ms=%.1f\n",
      r.hold_locked, (unsigned long long)r.accepted_edges, (unsigned long long)r.rejected_edges,
      (unsigned long long)r.detected_fields, (unsigned long long)r.field_callbacks, r.line_omega, r.subcarrier_hz,
      r.burst_amplitude, r.frame_width, r.frame_height, r.frame_channels, (unsigned long long)r.frame_sum,
      (unsigned long long)r.frame_fnv1a64, r.elapsed_ms);
  return 0;
}
