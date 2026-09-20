/* palindrome_c.h - EXPERIMENTAL C ABI over PALindrome's lib/ (toolchain feasibility probe).
 *
 * Not a designed API: pal_abi_version() + pal_build_info() + pal_cpu_supported() plus smoke
 * entry points that construct the real video::Decoder, feed it synthetic PAL CVBS and report
 * numbers. Everything else in the shared library has hidden visibility.
 *
 * ABI rules followed so a P/Invoke caller is safe:
 *  - plain C types, fixed-width ints, caller-allocated structs carrying struct_size
 *  - no C++ exception ever crosses the boundary (caught -> negative return + pal_last_error())
 *  - no ownership transfer of C++ objects
 */
#ifndef PALINDROME_C_H
#define PALINDROME_C_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#  if defined(PAL_BUILDING_DLL)
#    define PAL_API __declspec(dllexport)
#  else
#    define PAL_API __declspec(dllimport)
#  endif
#  define PAL_CALL __cdecl
#else
#  define PAL_API __attribute__((visibility("default")))
#  define PAL_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

#define PAL_ABI_VERSION 1u

enum {
  PAL_OK = 0,
  PAL_ERR_BAD_ARG = -1,
  PAL_ERR_EXCEPTION = -2,     /* a C++ exception was caught; see pal_last_error() */
  PAL_ERR_UNSUPPORTED = -3,   /* feature not compiled in (e.g. stdexec pipeline) */
  PAL_ERR_CPU = -4            /* built for a CPU tier this machine lacks (would SIGILL) */
};

typedef struct pal_smoke_params {
  uint32_t struct_size;       /* = sizeof(pal_smoke_params) */
  uint32_t width;             /* output raster, e.g. 720 */
  uint32_t height;            /* e.g. 576 */
  uint32_t fields;            /* 20 ms fields of synthetic CVBS to feed, e.g. 6 */
  uint32_t colour;            /* 0 = grey rail, 1 = PAL colour decode */
  uint32_t deposit_lanes;     /* Screen deposit threads (std::jthread pool); 1 = serial */
  uint32_t block_samples;     /* streaming block size, e.g. 65536 */
  uint32_t threaded_pipeline; /* 0 = serial driver, 1 = pipe::run threaded (stdexec) */
  double sample_rate_hz;      /* e.g. 16e6 */
} pal_smoke_params;

typedef struct pal_smoke_result {
  uint32_t struct_size;       /* = sizeof(pal_smoke_result) */
  int32_t hold_locked;        /* horizontal flywheel locked at end of stream */
  uint64_t samples_fed;
  uint64_t accepted_edges;
  uint64_t rejected_edges;
  uint64_t detected_fields;
  uint64_t field_callbacks;
  double line_omega;
  double field_omega;
  double agc_gain;
  double subcarrier_hz;
  double burst_amplitude;
  double killer_gain;
  double elapsed_ms;          /* decode+deposit wall time (excludes synthesis) */
  uint32_t frame_width;
  uint32_t frame_height;
  uint32_t frame_channels;
  uint32_t reserved;
  uint64_t frame_bytes;
  uint64_t frame_sum;         /* sum of all 8-bit pixel values */
  uint64_t frame_fnv1a64;     /* FNV-1a 64 over the pixel bytes */
} pal_smoke_result;

PAL_API uint32_t PAL_CALL pal_abi_version(void);
/* Static string: compiler, libc++ version, target, SIMD tier the FIR kernels were built for. */
PAL_API const char *PAL_CALL pal_build_info(void);
/* 1 if this CPU can run the ISA tier the library was compiled for, else 0. Compiled at the
 * baseline ISA in its own TU so it is always safe to call first. */
PAL_API int32_t PAL_CALL pal_cpu_supported(void);
/* Thread-local message for the last PAL_ERR_EXCEPTION on this thread ("" if none). */
PAL_API const char *PAL_CALL pal_last_error(void);

/* Construct CompositeInput + video::Decoder, stream `fields` fields of synthetic 625/50 PAL
 * colour bars through them, read the latched frame. If frame_out != NULL, up to frame_out_cap
 * bytes of the 8-bit frame (row-major, interleaved RGB when colour) are copied out. */
PAL_API int32_t PAL_CALL pal_smoke_decode(
    const pal_smoke_params *params, pal_smoke_result *result, uint8_t *frame_out, size_t frame_out_cap);

/* Deliberately throws inside the library and catches at the boundary: proves C++ EH/unwind
 * works in the produced binary (SEH on win-gnu, DWARF on linux, compact unwind on macOS).
 * Returns PAL_ERR_EXCEPTION and sets pal_last_error() when healthy. */
PAL_API int32_t PAL_CALL pal_smoke_exception(void);

#ifdef __cplusplus
}
#endif

#endif /* PALINDROME_C_H */
