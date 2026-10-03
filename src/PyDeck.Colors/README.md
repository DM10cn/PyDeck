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

The numerical rules and constants remain those of the pinned upstream. Three files have
C-style compound literals changed to equivalent standard C++ aggregate syntax
for MSVC (`Vec3{...}` and `ViewingConditions{...}`). Each modified file has a
notice and a source-manifest entry. Two small `compat/absl` headers supply only
the APIs actually used: a `std::unordered_map` alias for lookup/update in
WSMeans, whose traversal vector is separate, and hexadecimal string formatting.
No Abseil library download or runtime is required.

`cpp/quantize/wsmeans.cc` additionally has a documented PyDeck performance patch:
for at least 16 centers on AVX2-capable CPU/OS combinations, nearest-center search
uses `QuantizerNearest.asm`. It evaluates four Lab distances in double precision,
retains the original `(dl*dl + da*da) + db*db` order without FMA, applies the same
separation cutoff, and reduces candidates in ascending index order with strict
comparisons. A component-array snapshot of centers is prepared per iteration.
The original C++ loop remains active for small palettes or unsupported CPUs.
Wu initialization, population accumulation, random seed, movement threshold,
iteration limit, alpha handling and AOSP seed scoring are unchanged. The vendor
manifest records the patch and updated file hash; the original upstream hash is
retained. This optimization is a PyDeck adaptation, not an upstream MCU feature.

The x64 DLL uses the static Microsoft C++ runtime and imports only Windows
`KERNEL32.dll`. This does not change the application's separate .NET / Windows
App Runtime prerequisites.

## Build and checks

Wallpaper RGBA samples enter through the additive `PyDeckColorsRgbaToArgb` ABI
before quantization. `RgbaToArgb.asm` contains two Windows x64 MASM leaf kernels:
AVX2 `vpshufb` processes eight pixels per iteration; SSSE3 `pshufb` processes four.
The old SSE2 kernel has been removed. Both preserve every alpha value, handle
unaligned SIMD loads/stores and convert the final 0–3 pixels with integer scalar
instructions. AVX2 handles a remaining four-pixel block with a 128-bit VEX shuffle
and executes `vzeroupper` on every exit. Neither kernel reads beyond the input.

`PixelConversion.h` caches CPU/OS detection once: AVX2 requires CPUID XSAVE,
OSXSAVE, AVX and AVX2 plus XCR0 XMM/YMM state bits; `XGETBV` is only executed
after checking its CPU/OS prerequisites. Otherwise SSSE3 is selected if present.
The dispatcher is compiled at the existing baseline, without `/arch:AVX2`.
If neither kernel is supported, the ABI returns status 3 without touching output
and the C# adapter performs the original exact conversion. That managed fallback
also applies when the DLL or export is unavailable. Palette generation still
requires the native engine. The C++ boundary validates byte count and output
capacity; source/destination buffers must be disjoint.
Pixel conversion is independent of the WSMeans optimization described above.

The assembly follows the [Windows x64 calling convention](https://learn.microsoft.com/en-us/cpp/build/x64-calling-convention)
and is built with the installed [MASM x64 assembler](https://learn.microsoft.com/en-us/cpp/assembler/masm/masm-for-x64-ml64-exe).
Assembly sources participate in the incremental-build hash. This is a bounded
assembly integration, not a claim of measurable UI or end-to-end speedup.

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
Pixel conversion checks call the actual DLL and cover channel-order goldens,
all 256 alpha values, input offsets 0–31, output SIMD alignment, vector tails,
maximum sample length, untouched source/canaries, invalid ABI arguments, and
buffers ending at inaccessible pages to catch out-of-bounds reads/writes.
Managed checks compare P/Invoke output against the retained C# conversion and
verify that transparent pixels still do not influence seed selection.
Native checks also exercise synthetic missing CPU/OS prerequisites, the
unsupported/no-write status and each supported kernel directly using the same
assembly object linked into the DLL. Unsupported kernels are explicitly skipped;
the actual selected DLL path is checked separately. This does not emulate an
older physical CPU or an OS with YMM state disabled.

Production WSMeans omits the unused sorted index matrix and its per-iteration
row copies/sorts; candidate pruning still reads the original distance matrix.
Quantization checks retain the upstream sorting and scalar WSMeans branch under
a separate reference symbol. They compare full population maps, every input-color cluster
assignment and ordered seeds, including small/large palettes, random and gradient
inputs, repeated colors and random initialization. AVX2 kernel checks require
bit-identical minimum distances and indices for counts 0–256, including ties,
pruning boundaries and guarded separation rows. Actual DLL mixed-alpha extraction
is also compared to the reference. These fixtures do not establish equivalence
for every possible image or measure live wallpaper/GUI latency.
