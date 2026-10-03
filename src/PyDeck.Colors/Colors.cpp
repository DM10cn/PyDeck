#include "Colors.h"
#include "AospSeed.h"
#include "PixelConversion.h"
#include "cpp/quantize/celebi.h"
#include "cpp/scheme/scheme_expressive.h"
#include "cpp/scheme/scheme_tonal_spot.h"
#include <algorithm>
#include <array>
#include <mutex>
#include <vector>
namespace mcu = material_color_utilities;
namespace {
std::mutex quantizerMutex; // Upstream WSMeans uses CRT srand/rand.
void WriteScheme(const mcu::DynamicScheme& scheme, uint32_t* roles) {
        const std::array<uint32_t, ColorRoleCount> result{
            scheme.GetPrimary(), scheme.GetOnPrimary(), scheme.GetPrimaryContainer(), scheme.GetOnPrimaryContainer(),
            scheme.GetSecondary(), scheme.GetOnSecondary(), scheme.GetSecondaryContainer(), scheme.GetOnSecondaryContainer(),
            scheme.GetTertiary(), scheme.GetOnTertiary(), scheme.GetTertiaryContainer(), scheme.GetOnTertiaryContainer(),
            scheme.GetSurface(), scheme.GetOnSurface(), scheme.GetSurfaceVariant(), scheme.GetOnSurfaceVariant(),
            scheme.GetSurfaceDim(), scheme.GetSurfaceBright(), scheme.GetSurfaceContainerLowest(), scheme.GetSurfaceContainerLow(),
            scheme.GetSurfaceContainer(), scheme.GetSurfaceContainerHigh(), scheme.GetSurfaceContainerHighest(),
            scheme.GetOutline(), scheme.GetOutlineVariant(), scheme.GetError(), scheme.GetOnError(), scheme.GetErrorContainer(), scheme.GetOnErrorContainer(),
            scheme.GetInverseSurface(), scheme.GetInverseOnSurface(), scheme.GetInversePrimary(), scheme.GetSurfaceTint(),
            scheme.GetBackground(), scheme.GetOnBackground(), scheme.GetShadow(), scheme.GetScrim(),
            scheme.GetPrimaryFixed(), scheme.GetPrimaryFixedDim(), scheme.GetOnPrimaryFixed(), scheme.GetOnPrimaryFixedVariant(),
            scheme.GetSecondaryFixed(), scheme.GetSecondaryFixedDim(), scheme.GetOnSecondaryFixed(), scheme.GetOnSecondaryFixedVariant(),
            scheme.GetTertiaryFixed(), scheme.GetTertiaryFixedDim(), scheme.GetOnTertiaryFixed(), scheme.GetOnTertiaryFixedVariant()
        };
        std::copy(result.begin(), result.end(), roles);
}
}
int __cdecl PyDeckColorsRgbaToArgb(const uint8_t* rgba, uint32_t byteCount, uint32_t* argb, uint32_t capacity) noexcept {
    return pydeck::colors::ConvertPixels(pydeck::colors::CurrentPixelKernel(), rgba, byteCount, argb, capacity);
}
int __cdecl PyDeckColorsSeeds(const uint32_t* pixels, uint32_t count, uint32_t* seeds, uint32_t capacity, uint32_t* written) noexcept {
    if (!seeds || !written || capacity < MaxWallpaperSeeds || count > MaxWallpaperPixels || (count && !pixels)) return 1;
    *written = 0;
    try {
        if (!count) { seeds[0] = AospFallbackSeed; *written = 1; return 0; }
        std::vector<uint32_t> input(pixels, pixels + count);
        const auto clusters = static_cast<uint16_t>(std::clamp(count / 16u, 5u, 128u));
        std::lock_guard lock(quantizerMutex);
        const auto quantized = mcu::QuantizeCelebi(input, clusters);
        const auto result = pydeck::colors::AospSeedColors(quantized.color_to_count);
        std::copy(result.begin(), result.end(), seeds);
        *written = static_cast<uint32_t>(result.size());
        return 0;
    } catch (...) { return 2; }
}
int __cdecl PyDeckColorsSeed(const uint32_t* pixels, uint32_t count, uint32_t* seed) noexcept {
    if (!seed) return 1;
    std::array<uint32_t, MaxWallpaperSeeds> seeds{}; uint32_t written{};
    const auto status = PyDeckColorsSeeds(pixels, count, seeds.data(), MaxWallpaperSeeds, &written);
    if (!status) *seed = seeds[0];
    return status;
}
int __cdecl PyDeckColorsScheme(uint32_t seed, int32_t dark, int32_t variant, uint32_t* roles, uint32_t count) noexcept {
    if (!roles || count < ColorRoleCount || (dark != 0 && dark != 1) || (variant != 0 && variant != 1)) return 1;
    try {
        const auto source = mcu::Hct(seed | 0xff000000u);
        const mcu::DynamicScheme scheme = variant == 1
            ? static_cast<mcu::DynamicScheme>(mcu::SchemeExpressive(source, dark != 0, 0.0))
            : static_cast<mcu::DynamicScheme>(mcu::SchemeTonalSpot(source, dark != 0, 0.0));
        WriteScheme(scheme, roles);
        return 0;
    } catch (...) { return 2; }
}
int __cdecl PyDeckColorsDualScheme(uint32_t seed, uint32_t secondSeed, int32_t dark, uint32_t* roles, uint32_t count) noexcept {
    if (!roles || count < ColorRoleCount || (dark != 0 && dark != 1)) return 1;
    try {
        const mcu::SchemeTonalSpot first(mcu::Hct(seed | 0xff000000u), dark != 0, 0.0);
        const mcu::SchemeTonalSpot second(mcu::Hct(secondSeed | 0xff000000u), dark != 0, 0.0);
        // Compose source palettes before resolving roles, keeping one official contrast/tone solver.
        const mcu::DynamicScheme scheme(first.source_color_hct, first.variant, 0.0, dark != 0,
            first.primary_palette, second.secondary_palette, second.tertiary_palette,
            first.neutral_palette, first.neutral_variant_palette, first.error_palette);
        WriteScheme(scheme, roles);
        return 0;
    } catch (...) { return 2; }
}
const char* __cdecl PyDeckColorsVersion() noexcept {
    return "PyDeck.Colors/2;MCU=5b3618b16fdc3825e21d5679bafd144662088ea1;AOSP=android-16.0.0_r1;spec=2021;contrast=0;extension=PyDeck-DualSource";
}
