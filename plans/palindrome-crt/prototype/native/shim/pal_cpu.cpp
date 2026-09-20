// pal_cpu.cpp - ISA-tier guard. ALWAYS compiled at the baseline ISA (-mcpu=x86_64), even in a
// v3 build, so a P/Invoke caller can ask "will this library SIGILL here?" before touching DSP.
// Raw CPUID/XGETBV rather than __builtin_cpu_supports: that builtin needs compiler-rt's
// __cpu_model, which zig's compiler_rt is not guaranteed to provide on every target.

#include "palindrome_c.h"

#include <cstdint>

#if defined(__x86_64__) || defined(_M_X64)
#include <cpuid.h>

namespace {
[[nodiscard]] std::uint64_t xcr0() noexcept {
  std::uint32_t eax = 0;
  std::uint32_t edx = 0;
  __asm__ volatile("xgetbv" : "=a"(eax), "=d"(edx) : "c"(0));
  return (static_cast<std::uint64_t>(edx) << 32) | eax;
}

// x86-64-v3 = AVX, AVX2, BMI1, BMI2, F16C, FMA, LZCNT, MOVBE, OSXSAVE (+ OS-enabled YMM state).
[[nodiscard]] bool has_x86_64_v3() noexcept {
  unsigned a = 0;
  unsigned b = 0;
  unsigned c = 0;
  unsigned d = 0;
  if (__get_cpuid(1, &a, &b, &c, &d) == 0)
    return false;
  constexpr unsigned kFma = 1u << 12;
  constexpr unsigned kMovbe = 1u << 22;
  constexpr unsigned kOsxsave = 1u << 27;
  constexpr unsigned kAvx = 1u << 28;
  constexpr unsigned kF16c = 1u << 29;
  constexpr unsigned kLeaf1 = kFma | kMovbe | kOsxsave | kAvx | kF16c;
  if ((c & kLeaf1) != kLeaf1)
    return false;
  if ((xcr0() & 0x6u) != 0x6u) // XMM + YMM state enabled by the OS
    return false;
  if (__get_cpuid_count(7, 0, &a, &b, &c, &d) == 0)
    return false;
  constexpr unsigned kBmi1 = 1u << 3;
  constexpr unsigned kAvx2 = 1u << 5;
  constexpr unsigned kBmi2 = 1u << 8;
  constexpr unsigned kLeaf7 = kBmi1 | kAvx2 | kBmi2;
  if ((b & kLeaf7) != kLeaf7)
    return false;
  if (__get_cpuid(0x80000001u, &a, &b, &c, &d) == 0)
    return false;
  constexpr unsigned kLzcnt = 1u << 5;
  return (c & kLzcnt) != 0;
}
} // namespace
#endif

extern "C" PAL_API int32_t PAL_CALL pal_cpu_supported(void) {
#if (defined(__x86_64__) || defined(_M_X64)) && defined(PAL_ISA_TIER) && PAL_ISA_TIER >= 3
  return has_x86_64_v3() ? 1 : 0;
#else
  return 1; // baseline x86-64 or non-x86: nothing beyond the ABI floor is required
#endif
}
