#pragma once
#include <cstdint>
#if defined(PYDECK_COLORS_BUILD)
#define PYDECK_COLOR_API extern "C" __declspec(dllexport)
#else
#define PYDECK_COLOR_API extern "C" __declspec(dllimport)
#endif
// Stable ABI v1. Opaque sRGB ARGB. Status 0 success / 1 invalid argument / 2 error.
enum class ColorRole : uint32_t {
    Primary, OnPrimary, PrimaryContainer, OnPrimaryContainer,
    Secondary, OnSecondary, SecondaryContainer, OnSecondaryContainer,
    Tertiary, OnTertiary, TertiaryContainer, OnTertiaryContainer,
    Surface, OnSurface, SurfaceVariant, OnSurfaceVariant, SurfaceDim, SurfaceBright,
    SurfaceContainerLowest, SurfaceContainerLow, SurfaceContainer,
    SurfaceContainerHigh, SurfaceContainerHighest, Outline, OutlineVariant,
    Error, OnError, ErrorContainer, OnErrorContainer,
    InverseSurface, InverseOnSurface, InversePrimary, SurfaceTint,
    Background, OnBackground, Shadow, Scrim,
    PrimaryFixed, PrimaryFixedDim, OnPrimaryFixed, OnPrimaryFixedVariant,
    SecondaryFixed, SecondaryFixedDim, OnSecondaryFixed, OnSecondaryFixedVariant,
    TertiaryFixed, TertiaryFixedDim, OnTertiaryFixed, OnTertiaryFixedVariant, Count
};
inline constexpr uint32_t ColorRoleCount = static_cast<uint32_t>(ColorRole::Count);
inline constexpr uint32_t MaxWallpaperPixels = 112 * 112;
inline constexpr uint32_t AospFallbackSeed = 0xff1b6ef3;
inline constexpr uint32_t MaxWallpaperSeeds = 4;
// Caller locally decodes/resamples to <=112*112 pixels. Non-opaque ignored.
PYDECK_COLOR_API int __cdecl PyDeckColorsSeed(const uint32_t* pixels, uint32_t count, uint32_t* seed) noexcept;
// variant 0 Tonal Spot / 1 Expressive; contrast 0.0, MCU legacy/2021 color spec.
PYDECK_COLOR_API int __cdecl PyDeckColorsScheme(uint32_t seed, int32_t dark, int32_t variant, uint32_t* roles, uint32_t count) noexcept;
// Additive ABI: capacity >=4, written receives 1..4 ranked AOSP candidates.
PYDECK_COLOR_API int __cdecl PyDeckColorsSeeds(const uint32_t* pixels, uint32_t count, uint32_t* seeds, uint32_t capacity, uint32_t* written) noexcept;
// PyDeck extension using one MCU DynamicScheme: first source owns primary/neutrals/error;
// second source owns secondary/tertiary Tonal Spot palettes. This is not MCU CMF/spec 2026.
PYDECK_COLOR_API int __cdecl PyDeckColorsDualScheme(uint32_t seed, uint32_t secondSeed, int32_t dark, uint32_t* roles, uint32_t count) noexcept;
PYDECK_COLOR_API const char* __cdecl PyDeckColorsVersion() noexcept;
