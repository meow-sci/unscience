// Single-thread, per-stage timing of libpalindrome driven the way a game would
// embed it: no CLI, no stdexec pipeline - just the library calls on one thread.
//
//   composite int16 file -> [s16->float] -> [127-tap vision LP] -> CompositeInput
//     -> Decoder::decode_into -> Decoder::deposit -> FieldEvent::frame() (8-bit RGB)
//
// Each stage's cumulative wall time (steady_clock) is reported as milliseconds
// per second of video, i.e. "% of one core" / 10.
//
// usage: stage_bench <file.sigmf-data> <rate_hz> <colour 0|1> <lanes> <width> <height>
//                    <frame_stride (0=no readout)> <vision_lp 0|1> <block_samples> [composite_sync_v]
#include "palindrome/composite.hpp"
#include "palindrome/decoder.hpp"
#include "palindrome/demod.hpp"
#include "palindrome/denormals.hpp"
#include "palindrome/fir.hpp"

#include <algorithm>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <fstream>
#include <optional>
#include <span>
#include <string>
#include <vector>

namespace video = palindrome::video;
namespace dsp = palindrome::dsp;
using Clock = std::chrono::steady_clock;

namespace {
double ms(Clock::duration d) { return std::chrono::duration<double, std::milli>(d).count(); }
} // namespace

int main(int argc, char **argv) {
  if (argc < 10) {
    std::fprintf(stderr, "usage: see header comment\n");
    return 2;
  }
  const std::string path = argv[1];
  const double rate = std::atof(argv[2]);
  const bool colour = std::atoi(argv[3]) != 0;
  const auto lanes = static_cast<std::size_t>(std::atoi(argv[4]));
  const auto width = static_cast<std::size_t>(std::atoi(argv[5]));
  const auto height = static_cast<std::size_t>(std::atoi(argv[6]));
  const auto frame_stride = static_cast<std::size_t>(std::atoi(argv[7]));
  const bool vision_lp = std::atoi(argv[8]) != 0;
  const auto block = static_cast<std::size_t>(std::atoi(argv[9]));
  const double sync_v = argc > 10 ? std::atof(argv[10]) : 0.247;

  palindrome::flush_denormals_to_zero();

  // Whole file in memory first, so file I/O is not in any stage's time.
  std::ifstream in{path, std::ios::binary | std::ios::ate};
  if (!in) {
    std::fprintf(stderr, "cannot open %s\n", path.c_str());
    return 1;
  }
  const auto bytes = static_cast<std::size_t>(in.tellg());
  in.seekg(0);
  std::vector<std::int16_t> raw(bytes / 2);
  in.read(reinterpret_cast<char *>(raw.data()), static_cast<std::streamsize>(raw.size() * 2));
  const double seconds = static_cast<double>(raw.size()) / rate;

  // The CLI's render defaults (render_command.hpp), not the library's neutral ones.
  video::DecoderConfig dc{.sample_rate_hz = rate};
  dc.colour = colour;
  auto &sc = dc.screen;
  sc.width = width;
  sc.height = height;
  sc.gamma = 2.6;
  sc.readout_gamma = 2.2;
  sc.saturation = 0.085;
  sc.contrast = 1.6;
  sc.deposit_lanes = lanes;
  sc.eht_sag = 0.06;
  sc.line_pull = 0.003;
  sc.bcl_threshold = 0.7;
  constexpr double overscan = 0.06;
  constexpr double kActiveHLo = 9.5 / 64.0;
  constexpr double kActiveHHi = 61.5 / 64.0;
  constexpr double kActiveVLo = 25.0 / 312.5;
  constexpr double kActiveVHi = 1.0;
  sc.h_window_lo = kActiveHLo + 0.5 * overscan * (kActiveHHi - kActiveHLo);
  sc.h_window_hi = kActiveHHi - 0.5 * overscan * (kActiveHHi - kActiveHLo);
  sc.v_window_lo = kActiveVLo + 0.5 * overscan * (kActiveVHi - kActiveVLo);
  sc.v_window_hi = kActiveVHi - 0.5 * overscan * (kActiveVHi - kActiveVLo);

  video::Decoder decoder{dc};
  decoder.prepare(block);

  std::optional<dsp::Fir> lp;
  if (vision_lp) {
    lp.emplace(dsp::lowpass_kernel(palindrome::demod::kDefaultVisionTaps, rate, 5.5e6), 1);
    lp->prepare(block);
  }
  video::CompositeInputConfig cc{};
  cc.sample_rate_hz = rate;
  cc.sync_amplitude_v = sync_v;
  video::CompositeInput composite{cc};
  composite.prepare(block);

  Clock::duration t_scale{};
  Clock::duration t_lp{};
  Clock::duration t_clamp{};
  Clock::duration t_decode{};
  Clock::duration t_deposit{};
  Clock::duration t_readout{};
  std::size_t fields = 0;
  std::size_t frames = 0;
  std::uint64_t checksum = 0;
  double worst_block_ms = 0.0;

  const video::Screen::FieldCallback on_field = [&](const video::Screen::FieldEvent &e) {
    if (frame_stride == 0 || fields++ % frame_stride != 0)
      return;
    const auto r0 = Clock::now();
    const auto frame = e.frame(); // quantise the phosphor to 8-bit: the texture upload payload
    t_readout += Clock::now() - r0;
    checksum += frame.pixels[frame.pixels.size() / 2];
    ++frames;
  };

  std::vector<float> fl(block);
  video::DecodedBlock decoded;
  const auto start = Clock::now();
  for (std::size_t pos = 0; pos < raw.size(); pos += block) {
    const auto n = std::min(block, raw.size() - pos);
    const auto b0 = Clock::now();
    for (std::size_t k = 0; k < n; ++k)
      fl[k] = static_cast<float>(raw[pos + k]) * (1.0f / 32768.0f);
    const auto b1 = Clock::now();
    std::span<const float> x{fl.data(), n};
    if (lp)
      x = lp->process(x);
    const auto b2 = Clock::now();
    const auto env = composite.process(x);
    const auto b3 = Clock::now();
    decoder.decode_into(decoded, env);
    const auto b4 = Clock::now();
    const auto readout_before = t_readout;
    decoder.deposit(decoded, on_field);
    const auto b5 = Clock::now();
    t_scale += b1 - b0;
    t_lp += b2 - b1;
    t_clamp += b3 - b2;
    t_decode += b4 - b3;
    t_deposit += (b5 - b4) - (t_readout - readout_before);
    worst_block_ms = std::max(worst_block_ms, ms(b5 - b0));
  }
  const auto total = Clock::now() - start;

  const auto per_s = [&](Clock::duration d) { return ms(d) / seconds; };
  std::printf("%s rate=%.4g MS/s colour=%d lanes=%zu %zux%zu stride=%zu lp=%d block=%zu  video=%.3fs fields=%zu "
              "frames=%zu locked=%d edges=%zu chk=%llu\n",
      path.substr(path.find_last_of('/') + 1).c_str(), rate / 1e6, colour ? 1 : 0, lanes, width, height, frame_stride,
      vision_lp ? 1 : 0, block, seconds, decoder.detected_fields(), frames, decoder.hold_locked() ? 1 : 0,
      decoder.accepted_edges(), static_cast<unsigned long long>(checksum));
  std::printf("  ms per second of video:  s16->float %.1f | vision LP %.1f | clamp %.1f | decode_into %.1f | "
              "deposit %.1f | readout %.1f | TOTAL %.1f  (RTx %.3f)  worst block %.2f ms (block = %.2f ms of video)\n",
      per_s(t_scale), per_s(t_lp), per_s(t_clamp), per_s(t_decode), per_s(t_deposit), per_s(t_readout), per_s(total),
      ms(total) / 1000.0 / seconds, worst_block_ms, 1000.0 * static_cast<double>(block) / rate);
  return 0;
}
