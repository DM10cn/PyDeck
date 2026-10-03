#pragma once
#include "PixelConversion.h"
#include <cstddef>
#include <type_traits>

namespace pydeck::colors {
// Separate component arrays let one AVX2 instruction operate on four centers.
// Capacity matches the pinned MCU quantizer's maximum, including non-wallpaper tests.
struct QuantizerCenters { double l[256], a[256], b[256]; };
struct QuantizerSearch {
    double l, a, b, minimumDistance;
    const QuantizerCenters* centers;
    const void* separation; // MCU DistanceToIndex row: {double, int, padding}, stride 16.
    uint32_t count;
};
static_assert(std::is_standard_layout_v<QuantizerSearch>);
static_assert(offsetof(QuantizerSearch, minimumDistance) == 24 && offsetof(QuantizerSearch, centers) == 32
    && offsetof(QuantizerSearch, separation) == 40 && offsetof(QuantizerSearch, count) == 48);
static_assert(offsetof(QuantizerCenters, a) == 2048 && offsetof(QuantizerCenters, b) == 4096);
inline bool QuantizerAvx2Available() noexcept { return CurrentPixelKernel() == PixelKernel::Avx2; }
}
// Updates minimumDistance and returns first strictly closer eligible center, or -1.
// Requires finite Lab values/distances, count <= 256 and CPU/OS AVX2 support.
extern "C" int PyDeckQuantizerNearestAvx2(pydeck::colors::QuantizerSearch* search) noexcept;
