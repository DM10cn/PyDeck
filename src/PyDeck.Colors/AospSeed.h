#pragma once
#include <cstdint>
#include <map>
#include <vector>
namespace pydeck::colors {
// Android16 getSeedColors: quantized population, filter=true. Windows bitmap
// input does not use the zero-population live-wallpaper main-colors branch.
std::vector<uint32_t> AospSeedColors(const std::map<uint32_t, uint32_t>& populations);
}
