// palindrome_c.cpp - EXPERIMENTAL C ABI shim over PALindrome's lib/ (toolchain feasibility probe).
// Everything is hidden (-fvisibility=hidden) except the PAL_API functions.

#include "palindrome_c.h"

#include "pal_synth.hpp"

#include "palindrome/composite.hpp"
#include "palindrome/decoder.hpp"
#include "palindrome/denormals.hpp"
#include "palindrome/fir.hpp"
#if defined(PAL_WITH_STDEXEC)
#include "palindrome/pipeline_run.hpp"
#endif

#include <algorithm>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <exception>
#include <span>
#include <vector>

#ifndef PAL_TARGET
#define PAL_TARGET "unknown-target"
#endif

#define PAL_STR2(x) #x
#define PAL_STR(x) PAL_STR2(x)

namespace {

// Trivially-destructible TLS on purpose: no __cxa_thread_atexit dependency, safe in a DLL that
// a foreign runtime (CoreCLR) loads and whose threads we do not own.
thread_local char g_last_error[512] = "";

void set_error(const char *what) noexcept {
  std::strncpy(g_last_error, what != nullptr ? what : "", sizeof(g_last_error) - 1);
  g_last_error[sizeof(g_last_error) - 1] = '\0';
}

// Run `fn`, translating any C++ exception into an error code: nothing may unwind into C / .NET.
template<class Fn>
int32_t guarded(Fn &&fn) noexcept {
  try {
    g_last_error[0] = '\0';
    return fn();
  }
  catch (const std::exception &e) {
    set_error(e.what());
    return PAL_ERR_EXCEPTION;
  }
  catch (...) {
    set_error("unknown C++ exception");
    return PAL_ERR_EXCEPTION;
  }
}

[[nodiscard]] std::uint64_t fnv1a64(std::span<const std::uint8_t> bytes) noexcept {
  std::uint64_t h = 0xcbf29ce484222325ull;
  for (const auto b: bytes) {
    h ^= b;
    h *= 0x100000001b3ull;
  }
  return h;
}

[[nodiscard]] palindrome::video::DecoderConfig decoder_config(const pal_smoke_params &p) {
  palindrome::video::DecoderConfig dc{.sample_rate_hz = p.sample_rate_hz};
  dc.colour = p.colour != 0;
  auto &screen = dc.screen;
  screen.width = p.width;
  screen.height = p.height;
  // The CLI's period look, at broadcast-standard pots (the synthetic source is to the standard).
  screen.gamma = 2.6;
  screen.readout_gamma = 2.2;
  screen.contrast = 1.0;
  screen.saturation = 0.17;
  screen.eht_sag = 0.06;
  screen.line_pull = 0.003;
  screen.bcl_threshold = 0.7;
  screen.deposit_lanes = p.deposit_lanes == 0 ? 1 : p.deposit_lanes;
  return dc;
}

int32_t smoke_decode(const pal_smoke_params &p, pal_smoke_result &r, std::uint8_t *frame_out, std::size_t cap) {
  namespace video = palindrome::video;
  namespace dsp = palindrome::dsp;

  palindrome::flush_denormals_to_zero(); // per-thread MXCSR; the calling thread runs DSP below

  const std::size_t block = p.block_samples == 0 ? (std::size_t{1} << 16) : p.block_samples;
  const auto total = static_cast<std::size_t>(static_cast<double>(p.fields) * p.sample_rate_hz / 50.0);

  // Synthesise the whole input up front so the timed region is PALindrome code only.
  std::vector<float> cvbs(total);
  palsynth::Generator{p.sample_rate_hz, p.colour != 0}.fill(cvbs);

  // The CLI's composite front end: anti-alias/vision low-pass (dsp::Fir -> the AVX2 strip
  // kernels on a v3 build), then the affine CVBS -> detector-rail map.
  dsp::Fir vision_lp{dsp::lowpass_kernel(63, p.sample_rate_hz, 5.5e6)};
  vision_lp.prepare(block);
  video::CompositeInput composite{video::CompositeInputConfig{.sample_rate_hz = p.sample_rate_hz}};
  composite.prepare(block);

  video::Decoder decoder{decoder_config(p)};
  decoder.prepare(block);

  std::uint64_t callbacks = 0;
  const video::Screen::FieldCallback on_field = [&callbacks](const video::Screen::FieldEvent &e) {
    ++callbacks;
    e.latch();
  };

  const auto source = [&](const auto &emit) {
    for (std::size_t at = 0; at < total; at += block) {
      const auto n = std::min(block, total - at);
      emit(composite.process(vision_lp.process(std::span<const float>{cvbs}.subspan(at, n))));
    }
  };

  const auto t0 = std::chrono::steady_clock::now();
  if (p.threaded_pipeline != 0) {
#if defined(PAL_WITH_STDEXEC)
    // Upstream's threaded driver, verbatim shape: stdexec run_loop workers, async_scope, pools.
    constexpr std::ptrdiff_t kInFlight = 16;
    palindrome::pipe::run(
        true, kInFlight, source,
        palindrome::pipe::transform<video::DecodedBlock>(kInFlight,
            [&](std::span<const float> env, video::DecodedBlock &out) { decoder.decode_into(out, env); }),
        palindrome::pipe::sink([&](const video::DecodedBlock &b) { decoder.deposit(b, on_field); }));
#else
    return PAL_ERR_UNSUPPORTED;
#endif
  }
  else {
    video::DecodedBlock decoded;
    source([&](std::span<const float> env) {
      decoder.decode_into(decoded, env);
      decoder.deposit(decoded, on_field);
    });
  }
  const auto frame = decoder.latched_frame();
  const auto t1 = std::chrono::steady_clock::now();

  r.hold_locked = decoder.hold_locked() ? 1 : 0;
  r.samples_fed = total;
  r.accepted_edges = decoder.accepted_edges();
  r.rejected_edges = decoder.rejected_edges();
  r.detected_fields = decoder.detected_fields();
  r.field_callbacks = callbacks;
  r.line_omega = decoder.line_omega();
  r.field_omega = decoder.field_omega();
  r.agc_gain = decoder.agc_gain();
  r.subcarrier_hz = decoder.subcarrier_hz();
  r.burst_amplitude = decoder.burst_amplitude();
  r.killer_gain = decoder.killer_gain();
  r.elapsed_ms = std::chrono::duration<double, std::milli>(t1 - t0).count();
  r.frame_width = static_cast<std::uint32_t>(frame.width);
  r.frame_height = static_cast<std::uint32_t>(frame.height);
  r.frame_channels = static_cast<std::uint32_t>(frame.channels);
  r.frame_bytes = frame.pixels.size();
  r.frame_sum = 0;
  for (const auto px: frame.pixels)
    r.frame_sum += px;
  r.frame_fnv1a64 = fnv1a64(frame.pixels);
  if (frame_out != nullptr && cap > 0)
    std::memcpy(frame_out, frame.pixels.data(), std::min(cap, frame.pixels.size()));
  return PAL_OK;
}

} // namespace

extern "C" {

PAL_API uint32_t PAL_CALL pal_abi_version(void) { return PAL_ABI_VERSION; }

PAL_API const char *PAL_CALL pal_build_info(void) {
  return "palindrome-c shim abi " PAL_STR(PAL_ABI_VERSION) "; target " PAL_TARGET "; clang " __clang_version__
         "; libc++ " PAL_STR(_LIBCPP_VERSION) "; __cplusplus " PAL_STR(__cplusplus)
#if defined(__AVX2__) && defined(__FMA__)
             "; fir tier avx2+fma"
#else
             "; fir tier scalar"
#endif
#if defined(PAL_WITH_STDEXEC)
             "; stdexec pipeline yes"
#else
             "; stdexec pipeline no"
#endif
      ;
}

PAL_API const char *PAL_CALL pal_last_error(void) { return g_last_error; }

PAL_API int32_t PAL_CALL pal_smoke_decode(
    const pal_smoke_params *params, pal_smoke_result *result, uint8_t *frame_out, size_t frame_out_cap) {
  if (params == nullptr || result == nullptr || params->struct_size != sizeof(pal_smoke_params) ||
      result->struct_size != sizeof(pal_smoke_result) || params->width == 0 || params->height == 0 ||
      params->fields == 0 || !(params->sample_rate_hz > 0.0))
    return PAL_ERR_BAD_ARG;
  if (pal_cpu_supported() == 0)
    return PAL_ERR_CPU;
  return guarded([&] { return smoke_decode(*params, *result, frame_out, frame_out_cap); });
}

PAL_API int32_t PAL_CALL pal_smoke_exception(void) {
  return guarded([]() -> int32_t {
    // Empty taps: dsp::Fir's ctor throws std::invalid_argument from inside the library.
    const palindrome::dsp::Fir bad{std::vector<float>{}, 1};
    return static_cast<int32_t>(bad.size());
  });
}

} // extern "C"
