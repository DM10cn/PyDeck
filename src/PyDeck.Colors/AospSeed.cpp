// Derived from Android Open Source Project ColorScheme.java, Apache-2.0.
// Copyright (C) 2021 The Android Open Source Project
// See reference/AospColorScheme.java and LICENSE-AOSP.txt.
#include "AospSeed.h"
#include "Colors.h"
#include "cpp/cam/hct.h"
#include "cpp/utils/utils.h"
#include <algorithm>
#include <array>
#include <cmath>
namespace pydeck::colors {
namespace mcu = material_color_utilities;
std::vector<uint32_t> AospSeedColors(const std::map<uint32_t, uint32_t>& populations) {
    double total = 0;
    for (const auto& entry : populations) total += entry.second;
    if (total == 0) return {AospFallbackSeed};
    struct Candidate { uint32_t argb; mcu::Hct hct; double proportion; double score; };
    std::vector<Candidate> candidates;
    std::array<double, 360> huePopulation{};
    for (const auto& [argb, population] : populations) {
        mcu::Hct hct(argb);
        const auto hue = static_cast<int>(std::round(hct.get_hue())) % 360;
        // Low-chroma pixels remain in total, but not AOSP's hue histogram.
        if (hct.get_chroma() > 5.0) huePopulation[hue] += population / total;
        candidates.push_back({argb, hct, 0, 0});
    }
    for (auto& candidate : candidates) {
        const auto hue = static_cast<int>(std::round(candidate.hct.get_hue()));
        // Inclusive +/-15 degrees: exactly 31 bins, matching Android 16.
        for (int angle = hue - 15; angle <= hue + 15; ++angle)
            candidate.proportion += huePopulation[mcu::SanitizeDegreesInt(angle)];
        const auto chroma = candidate.hct.get_chroma();
        candidate.score = 70.0 * candidate.proportion + (chroma < 48.0 ? 0.1 : 0.3) * (chroma - 48.0);
    }
    std::erase_if(candidates, [](const auto& item) { return item.hct.get_chroma() < 5.0 || item.proportion <= 0.01; });
    std::stable_sort(candidates.begin(), candidates.end(), [](const auto& a, const auto& b) { return a.score > b.score; });
    std::vector<uint32_t> seeds;
    // Preserve pinned AOSP's first NONEMPTY set stopping condition. MCU Score
    // instead continues until a desired count, so it is not interchangeable.
    for (int distance = 90; distance >= 15; --distance) {
        seeds.clear();
        for (const auto& item : candidates) {
            const auto nearby = std::any_of(seeds.begin(), seeds.end(), [&](uint32_t seed) {
                return mcu::DiffDegrees(item.hct.get_hue(), mcu::Hct(seed).get_hue()) < distance;
            });
            if (nearby) continue;
            seeds.push_back(item.argb);
            if (seeds.size() >= 4) break;
        }
        if (!seeds.empty()) break;
    }
    if (seeds.empty()) seeds.push_back(AospFallbackSeed);
    return seeds;
}
}
