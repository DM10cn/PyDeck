# Native Monet color engine

PyDeck extracts a seed from the local wallpaper by running the official Material
Color Utilities C++ Celebi quantizer (Wu followed by weighted square-means), then
an explicit C++ adaptation of AOSP Android 16 `ColorScheme.getSeedColors`.
Manual seed selection enters the same HCT / dynamic-scheme pipeline after the
quantization step. There is no HSV hue rotation substitute or OS accent fallback
in this engine.

## Reproducible sources

- Material Color Utilities: commit
  [`5b3618b16fdc3825e21d5679bafd144662088ea1`](https://github.com/material-foundation/material-color-utilities/tree/5b3618b16fdc3825e21d5679bafd144662088ea1).
  `vendor/material-color-utilities/SOURCES.json` records original and local SHA256
  for every included file, plus each portability patch. The upstream Apache 2.0
  license is at `vendor/material-color-utilities/LICENSE`.
- Android 16 release tag `android-16.0.0_r1`:
  [`WallpaperColors.java`](https://android.googlesource.com/platform/frameworks/base/+/99b01a65cc4c104933788b3143285ab6bae65827/core/java/android/app/WallpaperColors.java)
  is pinned to frameworks/base `99b01a65cc4c104933788b3143285ab6bae65827`.
  [`ColorScheme.java`](https://android.googlesource.com/platform/frameworks/libs/systemui/+/9aacbcb77aa9353e75bc7c4ebc51d20b8b241b62/monet/src/com/android/systemui/monet/ColorScheme.java)
  is pinned to frameworks/libs/systemui `9aacbcb77aa9353e75bc7c4ebc51d20b8b241b62`.
  Unmodified reference copies and SHA256 records are in `reference/`; the Apache
  2.0 license also appears at `LICENSE-AOSP.txt`.
- Official C++ HCT / tonal palette / quantizer tests and official Swift Tonal
  Spot / Expressive tests at the same MCU commit are retained in `reference/`.
  Their independently published expected values are used by the native checks.

## Explicit color specification

The vendored official C++ dynamic schemes implement the legacy **2021 color
specification**, at standard contrast `0.0`. ABI variant 0 selects Tonal Spot;
variant 1 selects Expressive. Both expose 49 opaque sRGB ARGB roles, in the order
of `ColorRole` in `Colors.h`. This is not a claim of Material color spec 2025
parity: newer MCU language implementations have an explicit 2025 implementation
which is not present in this pinned C++ implementation. UI motion, shape and
component treatment can use Material Expressive independently of that color
specification.

## Wallpaper seed policy and desktop adaptations

The caller decodes locally, keeps image aspect ratio and uses nearest-neighbor
sampling to fit within 12,544 pixels (112 squared). AOSP uses a square-root area
scale with integer-truncated dimensions. The ABI rejects larger input, and
upstream Celebi ignores pixels whose alpha is not 255.

AOSP chooses `clamp(original bitmap area / 16, 5, 128)` clusters. The ABI receives
sampled pixels without the original dimensions, so it uses
`clamp(sampled pixel count / 16, 5, 128)`. Ordinary wallpaper images use 128 in
both cases. An unusually narrow image that samples to fewer than 2,048 pixels
can differ. PyDeck does not implement Android's low-RAM palette fallback or the
zero-population live-wallpaper branch that trusts Android `mainColors`.

`AospSeed.cpp` deliberately uses AOSP's rules instead of MCU `Score`:

- Total population includes low-chroma colors. Only chroma above 5 enters the
  histogram, using rounded HCT hue and 360 wrapped bins.
- Each candidate sums an inclusive +/-15 degree neighborhood (31 bins). Candidates
  need chroma at least 5 and neighborhood population above 1 percent.
- Score is `70 * population + (chroma < 48 ? 0.1 : 0.3) * (chroma - 48)`.
- Ranked seeds are separated initially by 90 degrees. The pinned AOSP source
  breaks on the first nonempty result, with at most four seeds; the ABI exposes
  the first seed. The additive Seeds ABI exposes all 1–4 ordered candidates. Empty or low-chroma input uses AOSP Google blue `0xFF1B6EF3`.
- Equal scores retain ascending ARGB input order for deterministic desktop
  behavior. AOSP HashMap tie ordering is not an API guarantee.

The MCU C++ WSMeans implementation seeds the C runtime RNG with `42688`. Native
seed extraction is serialized because that RNG is global. The source algorithm
is retained; cross-platform C runtime RNG differences, bitmap resampling and
Android's quantizer implementation mean this is not a promise of bit-for-bit
wallpaper output for every Android device.

## Portability changes

PyDeck's additive DualScheme ABI composes a Tonal Spot first source's primary,
neutral and error palettes with a second source's secondary and tertiary palettes,
then resolves roles through one unchanged MCU DynamicScheme. This application
extension is named DualSource, not CMF or spec 2026. Both existing official scheme
variants and the old ABI remain unchanged; equal sources reproduce Tonal Spot.
No vendored MCU source is changed for this extension. See `docs/MONET.md` for
provenance and the distinction from Google's newer official two-source CMF.

The numerical algorithms and constants remain upstream code. Three files have
C-style compound literals changed to equivalent standard C++ aggregate syntax
for MSVC (`Vec3{...}` and `ViewingConditions{...}`). Each modified file has a
notice and a source-manifest entry. Two small `compat/absl` headers supply only
the APIs actually used: a `std::unordered_map` alias for lookup/update in
WSMeans, whose traversal vector is separate, and hexadecimal string formatting.
No Abseil library download or runtime is required.

The x64 DLL uses the static Microsoft C++ runtime and imports only Windows
`KERNEL32.dll`. This does not change the application's separate .NET / Windows
App Runtime prerequisites.

## Build and checks

From the repository root, with the installed MSVC x64 toolset and Windows SDK:

```powershell
pwsh -NoProfile -File scripts/Build-Colors.ps1 -OutputDirectory artifacts/native-colors -Checks
```

The default output is `artifacts/native-colors/PyDeck.Colors.dll`. Inputs and the
DLL are SHA256-checked for incremental reuse; a file lock serializes builds.
`build.json` identifies the exact build inputs, DLL hash, source version and
evidence directory. `-Checks` additionally builds and runs `Colors.Checks.exe`
against the actual DLL, checking ABI bounds, upstream HCT/tonal palette goldens,
quantizer populations and alpha handling, AOSP cutoff/population/hue cases,
official light/dark Tonal Spot and Expressive scheme goldens, all 49 opaque
roles, and deterministic extraction of 12,544 colorful pixels with elapsed time.

The checks exercise the native engine only. They do not validate Windows
wallpaper decoding, the live WinUI appearance, or Android runtime equivalence.
