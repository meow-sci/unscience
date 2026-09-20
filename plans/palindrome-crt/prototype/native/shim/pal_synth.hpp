#pragma once

// Synthetic 625/50 PAL CVBS (colour bars) for the smoke test: real line sync, broad and
// equalising pulses with the half-line field offset, swinging burst and a V-switched chroma,
// so the real separator / flywheels / APC / ident have something genuine to lock to.
// Test scaffolding only - nothing here is upstream PALindrome code.

#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <numbers>
#include <span>

namespace palsynth {

struct Yuv {
  double y;
  double u;
  double v;
};

// 75% bars: white, yellow, cyan, green, magenta, red, blue, black.
inline constexpr std::array<std::array<double, 3>, 8> kBarsRgb{{{0.75, 0.75, 0.75}, {0.75, 0.75, 0.0},
    {0.0, 0.75, 0.75}, {0.0, 0.75, 0.0}, {0.75, 0.0, 0.75}, {0.75, 0.0, 0.0}, {0.0, 0.0, 0.75}, {0.0, 0.0, 0.0}}};

[[nodiscard]] inline Yuv to_yuv(const std::array<double, 3> &rgb) {
  const double y = 0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2];
  return {y, 0.493 * (rgb[2] - y), 0.877 * (rgb[0] - y)};
}

class Generator {
public:
  Generator(double sample_rate_hz, bool colour) : fs_{sample_rate_hz}, colour_{colour} {
    for (std::size_t b = 0; b < kBarsRgb.size(); ++b)
      bars_[b] = to_yuv(kBarsRgb[b]);
  }

  // Fill `out` with the next out.size() samples, in input full-scale units (1.0 == 1 V).
  void fill(std::span<float> out) {
    for (auto &s: out)
      s = static_cast<float>(sample(n_++));
  }

private:
  static constexpr double kLine = 64e-6;
  static constexpr double kHalf = 32e-6;
  static constexpr double kFrame = 625.0 * kLine;
  static constexpr double kSync = 4.7e-6;
  static constexpr double kEq = 2.35e-6;
  static constexpr double kBroad = 27.3e-6;
  static constexpr double kBurstLo = 5.6e-6;
  static constexpr double kBurstHi = 7.85e-6;
  static constexpr double kActiveLo = 10.5e-6;
  static constexpr double kActiveHi = 62.5e-6;
  static constexpr double kFsc = 4433618.75;
  static constexpr double kSyncV = -0.3;
  static constexpr double kPictureV = 0.7;
  static constexpr double kBurstV = 0.15;

  [[nodiscard]] static bool broad(std::uint32_t h) { return h < 5 || (h >= 625 && h < 630); }
  [[nodiscard]] static bool equalising(std::uint32_t h) {
    return (h >= 5 && h < 10) || (h >= 620 && h < 625) || (h >= 630 && h < 635) || h >= 1245;
  }
  [[nodiscard]] static bool active_line(std::uint32_t line) {
    return (line >= 22 && line < 310) || (line >= 335 && line < 622);
  }

  [[nodiscard]] double sample(std::uint64_t n) const {
    const double t = static_cast<double>(n) / fs_;
    const double frames = std::floor(t / kFrame);
    const double tf = t - frames * kFrame;
    const auto h = static_cast<std::uint32_t>(tf / kHalf);
    const double th = tf - static_cast<double>(h) * kHalf;
    if (broad(h))
      return th < kBroad ? kSyncV : 0.0;
    if (equalising(h))
      return th < kEq ? kSyncV : 0.0;
    const std::uint32_t line = h / 2;
    const double tl = tf - static_cast<double>(line) * kLine;
    if (tl < kSync)
      return kSyncV;
    // The PAL switch runs continuously across frames (625 is odd), giving the 4-frame sequence.
    const auto global_line = static_cast<std::uint64_t>(t / kLine);
    const double v_sign = (global_line & 1u) != 0 ? -1.0 : 1.0;
    // Subcarrier phase from absolute time; frac() first so the double keeps its precision.
    const double cycles = kFsc * t;
    const double wt = 2.0 * std::numbers::pi * (cycles - std::floor(cycles));
    // Swinging burst: 180 +/- 45 degrees, i.e. on the -U axis with the V part following the switch.
    if (colour_ && tl >= kBurstLo && tl < kBurstHi)
      return kBurstV * std::sin(wt + v_sign * 0.75 * std::numbers::pi);
    if (!active_line(line) || tl < kActiveLo || tl >= kActiveHi)
      return 0.0;
    const double x = (tl - kActiveLo) / (kActiveHi - kActiveLo);
    const auto bar = static_cast<std::size_t>(x * 8.0) & 7u;
    const Yuv &c = bars_[bar];
    double volts = c.y;
    if (colour_)
      volts += c.u * std::sin(wt) + v_sign * c.v * std::cos(wt);
    return kPictureV * volts;
  }

  double fs_;
  bool colour_;
  std::uint64_t n_ = 0;
  std::array<Yuv, 8> bars_{};
};

} // namespace palsynth
