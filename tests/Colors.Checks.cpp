#include "Colors.h"
#include "AospSeed.h"
#include "cpp/cam/hct.h"
#include "cpp/contrast/contrast.h"
#include "cpp/palettes/tones.h"
#include "cpp/quantize/celebi.h"
#include "cpp/utils/utils.h"
#include <array>
#include <chrono>
#include <cmath>
#include <cstring>
#include <iostream>
#include <stdexcept>
#include <vector>
namespace mcu = material_color_utilities;
void Require(bool condition, const char* reason) { if (!condition) throw std::runtime_error(reason); }
void Equal(uint32_t actual, uint32_t expected, const char* reason) {
    if (actual != expected) { std::cerr << reason << ": actual=" << std::hex << actual << " expected=" << expected << std::dec << '\n'; throw std::runtime_error(reason); }
}
std::array<uint32_t, ColorRoleCount> Scheme(uint32_t seed, bool dark, int variant) {
    std::array<uint32_t, ColorRoleCount> roles{};
    Require(PyDeckColorsScheme(seed, dark ? 1 : 0, variant, roles.data(), ColorRoleCount) == 0, "Scheme ABI failed");
    return roles;
}
uint32_t Seed(const std::vector<uint32_t>& pixels) {
    uint32_t seed{}; Require(PyDeckColorsSeed(pixels.data(), static_cast<uint32_t>(pixels.size()), &seed) == 0, "Seed ABI failed"); return seed;
}
std::vector<uint32_t> Seeds(const std::vector<uint32_t>& pixels) {
    std::array<uint32_t, MaxWallpaperSeeds> seeds{}; uint32_t written{};
    Require(PyDeckColorsSeeds(pixels.data(), static_cast<uint32_t>(pixels.size()), seeds.data(), MaxWallpaperSeeds, &written) == 0, "Seeds ABI failed");
    Require(written >= 1 && written <= MaxWallpaperSeeds, "AOSP candidate count bound");
    return {seeds.begin(), seeds.begin() + written};
}
std::array<uint32_t, ColorRoleCount> DualScheme(uint32_t seed, uint32_t second, bool dark) {
    std::array<uint32_t, ColorRoleCount> roles{};
    Require(PyDeckColorsDualScheme(seed, second, dark ? 1 : 0, roles.data(), ColorRoleCount) == 0, "Dual scheme ABI failed");
    return roles;
}
int main() {
    try {
        uint32_t seed{}; std::array<uint32_t, ColorRoleCount> roles{};
        Require(PyDeckColorsSeed(nullptr, 1, &seed) == 1, "null pixels rejected");
        Require(PyDeckColorsSeed(&seed, MaxWallpaperPixels + 1, &seed) == 1, "oversize rejected");
        Require(PyDeckColorsSeed(nullptr, 0, nullptr) == 1, "null seed rejected");
        Require(PyDeckColorsScheme(0, 2, 0, roles.data(), ColorRoleCount) == 1, "invalid dark rejected");
        Require(PyDeckColorsScheme(0, 0, 2, roles.data(), ColorRoleCount) == 1, "invalid variant rejected");
        Require(PyDeckColorsScheme(0, 0, 0, roles.data(), ColorRoleCount - 1) == 1, "short roles rejected");
        Require(PyDeckColorsScheme(0, 0, 0, nullptr, ColorRoleCount) == 1, "null roles rejected");
        std::array<uint32_t, MaxWallpaperSeeds> seeds{}; uint32_t written{};
        Require(PyDeckColorsSeeds(nullptr, 1, seeds.data(), MaxWallpaperSeeds, &written) == 1, "candidate null pixels rejected");
        Require(PyDeckColorsSeeds(&seed, MaxWallpaperPixels + 1, seeds.data(), MaxWallpaperSeeds, &written) == 1, "candidate oversize input rejected");
        Require(PyDeckColorsSeeds(nullptr, 0, seeds.data(), MaxWallpaperSeeds - 1, &written) == 1, "candidate short output rejected");
        Require(PyDeckColorsSeeds(nullptr, 0, nullptr, MaxWallpaperSeeds, &written) == 1, "candidate null output rejected");
        Require(PyDeckColorsSeeds(nullptr, 0, seeds.data(), MaxWallpaperSeeds, nullptr) == 1, "candidate null count rejected");
        Require(PyDeckColorsDualScheme(0, 0, 2, roles.data(), ColorRoleCount) == 1, "dual invalid dark rejected");
        Require(PyDeckColorsDualScheme(0, 0, 0, nullptr, ColorRoleCount) == 1, "dual null roles rejected");
        Require(PyDeckColorsDualScheme(0, 0, 0, roles.data(), ColorRoleCount - 1) == 1, "dual short roles rejected");
        Require(std::strstr(PyDeckColorsVersion(), "spec=2021") != nullptr, "version declares spec");
        Equal(Seed({}), AospFallbackSeed, "empty fallback");
        std::cout << "PASS ABI validation and version\n";

        // Golden CAM16 hue/chroma values from upstream cpp/cam/cam_test.cc.
        const mcu::Hct red(0xffff0000), green(0xff00ff00), blue(0xff0000ff);
        Require(std::abs(red.get_hue() - 27.408) < .001 && std::abs(red.get_chroma() - 113.357) < .001, "red HCT golden");
        Require(std::abs(green.get_hue() - 142.139) < .001 && std::abs(green.get_chroma() - 108.410) < .001, "green HCT golden");
        Require(std::abs(blue.get_hue() - 282.788) < .001 && std::abs(blue.get_chroma() - 87.230) < .001, "blue HCT golden");
        for (const uint32_t color : {0xffff0000u,0xff00ff00u,0xff0000ffu,0xff000000u,0xffffffffu}) {
            const mcu::Hct hct(color); Equal(mcu::Hct(hct.get_hue(),hct.get_chroma(),hct.get_tone()).ToInt(), color, "HCT roundtrip");
        }
        // Exact tonal palette values from upstream cpp/palettes/tones_test.cc.
        const mcu::TonalPalette palette(0xff0000ff);
        const std::array<int,12> tones{100,95,90,80,70,60,50,40,30,20,10,0};
        const std::array<uint32_t,12> expected{0xffffffff,0xfff1efff,0xffe0e0ff,0xffbec2ff,0xff9da3ff,0xff7c84ff,0xff5a64ff,0xff343dff,0xff0000ef,0xff0001ac,0xff00006e,0xff000000};
        for (size_t i=0;i<tones.size();++i) Equal(palette.get(tones[i]), expected[i], "blue tonal palette golden");
        std::cout << "PASS official HCT and tonal palette golden values\n";

        const auto quantized = mcu::QuantizeCelebi({0xffff0000,0xffff0000,0xff00ff00,0xff00ff00,0xff00ff00}, 128);
        Require(quantized.color_to_count.size() == 2 && quantized.color_to_count.at(0xffff0000) == 2 && quantized.color_to_count.at(0xff00ff00) == 3, "official Celebi RGB populations");
        Equal(Seed({0x00ff0000,0x7fff0000}), AospFallbackSeed, "transparent fallback");
        Equal(Seed({0xffff0000,0x00ff0000}), 0xffff0000, "transparent ignored");
        Equal(Seed({0xff777777,0xffeeeeee,0xff111111}), AospFallbackSeed, "low chroma fallback");
        std::cout << "PASS Celebi populations and opaque pixel policy\n";

        using pydeck::colors::AospSeedColors;
        Equal(AospSeedColors({{0xffff0000,1},{0xff777777,99}}).front(), AospFallbackSeed, "AOSP <=1 percent filtered");
        Equal(AospSeedColors({{0xffff0000,2},{0xff777777,98}}).front(), 0xffff0000, "AOSP >1 percent accepted");
        Equal(AospSeedColors({{0xff0000ff,90},{0xffff0000,10}}).front(), 0xff0000ff, "population affects score");
        const auto a=mcu::Hct(50,40,50).ToInt(), b=mcu::Hct(55,40,50).ToInt(), c=mcu::Hct(220,40,50).ToInt();
        const auto grouped=AospSeedColors({{a,20},{b,25},{c,40},{0xff777777,15}});
        Require(grouped.front()==a || grouped.front()==b, "neighbor hues combine population");
        const auto x=mcu::Hct(359,40,50).ToInt(), y=mcu::Hct(1,40,50).ToInt();
        const auto wrapped=AospSeedColors({{x,25},{y,25},{c,40},{0xff777777,10}});
        Require(wrapped.front()==x || wrapped.front()==y, "hue histogram wraps at 360");
        for(size_t i=0;i<grouped.size();++i) for(size_t j=i+1;j<grouped.size();++j)
            Require(mcu::DiffDegrees(mcu::Hct(grouped[i]).get_hue(),mcu::Hct(grouped[j]).get_hue())>=90, "pinned AOSP hue distance");
        std::cout << "PASS AOSP population, cutoff, hue aggregation and fallback\n";

        // Independent official Swift golden tests at the same MCU source pin:
        // reference/SchemeTonalSpotTests.swift and SchemeExpressiveTests.swift.
        const auto light=Scheme(0xff6750a4,false,0), dark=Scheme(0xff6750a4,true,0);
        Equal(light[0],0xff65558f,"Tonal Spot light primary"); Equal(dark[0],0xffcfbdfe,"Tonal Spot dark primary");
        Equal(light[4],0xff625b71,"Tonal Spot light secondary"); Equal(dark[4],0xffcbc2db,"Tonal Spot dark secondary");
        Equal(light[8],0xff7e5260,"Tonal Spot light tertiary"); Equal(dark[8],0xffefb8c8,"Tonal Spot dark tertiary");
        Equal(light[12],0xfffdf7ff,"Tonal Spot light surface"); Equal(dark[12],0xff141218,"Tonal Spot dark surface");
        Equal(light[13],0xff1d1b20,"Tonal Spot light on surface"); Equal(dark[13],0xffe6e0e9,"Tonal Spot dark on surface");
        const auto expressiveLight=Scheme(0xff0000ff,false,1), expressiveDark=Scheme(0xff0000ff,true,1);
        Equal(expressiveLight[0],0xff146c48,"Expressive light primary"); Equal(expressiveDark[0],0xff87d7ab,"Expressive dark primary");
        Equal(expressiveLight[2],0xffa2f4c6,"Expressive light container"); Equal(expressiveDark[2],0xff005234,"Expressive dark container");
        Equal(expressiveLight[3],0xff005234,"Expressive light on container"); Equal(expressiveDark[3],0xffa2f4c6,"Expressive dark on container");
        for (const auto& scheme : {light,dark,expressiveLight,expressiveDark}) for(const auto color:scheme) Require((color>>24)==255, "all 49 roles opaque");
        Require(Scheme(0x006750a4,false,0)==light,"manual seed alpha normalization");
        std::cout << "PASS official Tonal Spot and Expressive light/dark golden schemes, 49 opaque roles\n";

        std::vector<uint32_t> twoColors(90, 0xff0000ff); twoColors.insert(twoColors.end(), 10, 0xffff0000);
        Require(Seeds(twoColors) == std::vector<uint32_t>{0xff0000ff, 0xffff0000}, "ordered wallpaper candidates");
        Equal(Seeds(twoColors).front(), Seed(twoColors), "old single seed ABI remains first candidate");
        Require(Seeds({0xffff0000, 0xffff0000}) == std::vector<uint32_t>{0xffff0000}, "one wallpaper candidate remains single");
        Require(Seeds({0x00777777}) == std::vector<uint32_t>{AospFallbackSeed}, "candidate transparent fallback");
        for (const bool night : {false, true}) for (const uint32_t first : {0xff6750a4u, 0xffff0000u, 0xff777777u, 0xff0000ffu}) {
            const auto base = Scheme(first, night, 0);
            Require(DualScheme(first, first, night) == base, "same-source dual scheme equals official Tonal Spot");
            Require(DualScheme(first & 0xffffffu, first & 0xffffffu, night) == base, "dual seed alpha normalization");
            const auto dual = DualScheme(first, 0xff00ff00, night), second = Scheme(0xff00ff00, night, 0);
            for (uint32_t index = 0; index < ColorRoleCount; ++index) {
                const bool secondRole = (index >= 4 && index <= 11) || index >= 41;
                Equal(dual[index], secondRole ? second[index] : base[index], "dual sources own their specified palettes");
                Require((dual[index] >> 24) == 255, "dual roles opaque");
            }
            Require(dual[4] != base[4] && dual[8] != base[8], "second source visibly changes secondary and tertiary roles");
            for (const auto pair : {std::pair{0,1}, {2,3}, {4,5}, {6,7}, {8,9}, {10,11}, {12,13}, {14,15}, {25,26}, {27,28}})
                Require(mcu::RatioOfTones(mcu::LstarFromArgb(dual[pair.first]), mcu::LstarFromArgb(dual[pair.second])) >= 4.45, "dual paired-role text contrast");
        }
        std::cout << "PASS PyDeck dual-source ABI, ordered candidates, same-seed parity, palette ownership and paired-role contrast\n";

        std::vector<uint32_t> colorful(MaxWallpaperPixels); uint32_t random=123456789;
        for(auto& pixel:colorful) { random=random*1664525u+1013904223u; pixel=0xff000000u|(random&0xffffffu); }
        const auto begin=std::chrono::steady_clock::now(); const auto colorfulSeed=Seed(colorful);
        const auto elapsed=std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now()-begin).count();
        Equal(Seed(colorful),colorfulSeed,"Celebi deterministic repeat");
        Require((colorfulSeed>>24)==255,"colorful wallpaper opaque seed");
        std::cout << "PASS 12544 colorful pixels: " << elapsed << " ms; seed=" << std::hex << colorfulSeed << std::dec << "\n";
        std::cout << "PASS 7 native color check groups\n";
        return 0;
    } catch(const std::exception& error) { std::cerr << "FAIL " << error.what() << '\n'; return 1; }
}
