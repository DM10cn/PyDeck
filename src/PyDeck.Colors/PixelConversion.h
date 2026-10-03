#pragma once
#include "Colors.h"
#include <intrin.h>
#include <immintrin.h>

extern "C" void PyDeckRgbaToArgbAvx2(const uint8_t*, uint32_t*, uint32_t) noexcept;
extern "C" void PyDeckRgbaToArgbSsse3(const uint8_t*, uint32_t*, uint32_t) noexcept;

namespace pydeck::colors {
enum class PixelKernel { Unavailable, Ssse3, Avx2 };
inline constexpr uint32_t Ssse3Bit = 1u << 9;
inline constexpr uint32_t AvxStateBits = (1u << 26) | (1u << 27) | (1u << 28); // XSAVE, OSXSAVE, AVX
inline constexpr uint32_t Avx2Bit = 1u << 5;

// Pure selection is also exercised with synthetic CPU/OS feature combinations.
constexpr PixelKernel SelectPixelKernel(uint32_t leaf1Ecx, uint32_t leaf7Ebx, uint64_t xcr0) noexcept {
    if ((leaf1Ecx & AvxStateBits) == AvxStateBits && (leaf7Ebx & Avx2Bit) && (xcr0 & 6) == 6)
        return PixelKernel::Avx2;
    return (leaf1Ecx & Ssse3Bit) ? PixelKernel::Ssse3 : PixelKernel::Unavailable;
}

struct PixelCpuFeatures { uint32_t leaf1Ecx, leaf7Ebx; uint64_t xcr0; };
inline PixelCpuFeatures ReadPixelCpuFeatures() noexcept {
    int registers[4]{};
    __cpuid(registers, 0);
    const int maxLeaf = registers[0];
    if (maxLeaf < 1) return {};
    __cpuidex(registers, 1, 0);
    const auto ecx = static_cast<uint32_t>(registers[2]);
    // Never execute XGETBV unless CPU and OS both enable extended-state access.
    const uint64_t xcr0 = (ecx & AvxStateBits) == AvxStateBits ? _xgetbv(0) : 0;
    uint32_t ebx = 0;
    if (maxLeaf >= 7) { __cpuidex(registers, 7, 0); ebx = static_cast<uint32_t>(registers[1]); }
    return {ecx, ebx, xcr0};
}
inline PixelKernel CurrentPixelKernel() noexcept {
    static const auto kernel = [] {
        const auto features = ReadPixelCpuFeatures();
        return SelectPixelKernel(features.leaf1Ecx, features.leaf7Ebx, features.xcr0);
    }();
    return kernel;
}

// Internal entry used by the DLL and kernel checks. The caller must only select
// kernels supported by its actual CPU/OS; production always uses cached detection.
inline int ConvertPixels(PixelKernel kernel, const uint8_t* rgba, uint32_t byteCount, uint32_t* argb, uint32_t capacity) noexcept {
    if (byteCount % 4 || byteCount > MaxWallpaperPixels * 4 || capacity < byteCount / 4 || (byteCount && (!rgba || !argb))) return 1;
    if (!byteCount) return 0;
    switch (kernel) {
    case PixelKernel::Avx2: PyDeckRgbaToArgbAvx2(rgba, argb, byteCount / 4); return 0;
    case PixelKernel::Ssse3: PyDeckRgbaToArgbSsse3(rgba, argb, byteCount / 4); return 0;
    default: return 3; // Caller can perform the exact managed conversion; no writes.
    }
}
}
