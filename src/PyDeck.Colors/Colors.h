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
// Additive ABI: RGBA bytes -> 0xAARRGGBB words, preserving all alpha values.
// byteCount must be divisible by 4 and <= MaxWallpaperPixels * 4; capacity is
// measured in uint32_t pixels. Buffers must be disjoint, with no SIMD alignment
// requirement. Empty input succeeds (null buffers allowed); invalid args write nothing.
// AVX2 (CPU + OS state) preferred, then SSSE3. Status 3 means neither is available
// and writes nothing; the managed caller must use its exact scalar conversion.
PYDECK_COLOR_API int __cdecl PyDeckColorsRgbaToArgb(const uint8_t* rgba, uint32_t byteCount, uint32_t* argb, uint32_t capacity) noexcept;
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
