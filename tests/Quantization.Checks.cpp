#include "Quantization.h"
#include "AospSeed.h"
#include "cpp/quantize/wsmeans.h"
#include "cpp/quantize/wu.h"
#include <algorithm>
#include <array>
#include <bit>
#include <cmath>
#include <iostream>
#include <random>
#include <stdexcept>
#include <vector>
#include <windows.h>
namespace mcu = material_color_utilities;
namespace material_color_utilities {
QuantizerResult QuantizeWsmeansReference(const std::vector<Argb>&, const std::vector<Argb>&, uint16_t);
}
namespace {
void Require(bool value, const char* text) { if (!value) throw std::runtime_error(text); }
struct Separation { double distance; int index = 0; };
static_assert(sizeof(Separation) == 16);
int ReferenceSearch(pydeck::colors::QuantizerSearch& search) {
    double threshold = 4 * search.minimumDistance; int best = -1;
    auto* row = static_cast<const Separation*>(search.separation);
    for (uint32_t i = 0; i < search.count; ++i) {
        if (row[i].distance >= threshold) continue;
        double l = search.l - search.centers->l[i], a = search.a - search.centers->a[i], b = search.b - search.centers->b[i];
        double distance = l*l + a*a + b*b;
        if (distance < search.minimumDistance) { search.minimumDistance = distance; best = static_cast<int>(i); }
    }
    return best;
}
void CompareSearch(pydeck::colors::QuantizerSearch search) {
    auto expected = search; int index = ReferenceSearch(expected);
    Require(PyDeckQuantizerNearestAvx2(&search) == index, "AVX2 nearest index differs from scalar");
    Require(std::bit_cast<uint64_t>(search.minimumDistance) == std::bit_cast<uint64_t>(expected.minimumDistance), "AVX2 distance is not bit-exact");
}
void KernelChecks() {
    using namespace pydeck::colors;
    if (!QuantizerAvx2Available()) { std::cout << "SKIP nearest AVX2 kernel: CPU/OS unavailable\n"; return; }
    std::mt19937 random(42688);
    std::uniform_real_distribution<double> component(-128,128), distance(0,100000);
    QuantizerCenters centers{}; std::array<Separation,256> row{};
    for (uint32_t count = 0; count <= 256; ++count) for (int sample = 0; sample < 64; ++sample) {
        for (uint32_t i = 0; i < count; ++i) {
            centers.l[i] = component(random); centers.a[i] = component(random); centers.b[i] = component(random);
            row[i].distance = distance(random);
        }
        CompareSearch({component(random),component(random),component(random),distance(random),&centers,row.data(),count});
    }
    centers = {}; row = {};
    for (uint32_t count = 1; count <= 256; ++count) {
        CompareSearch({1,1,1,4,&centers,row.data(),count}); // equal centers: first wins across lanes/tails
        CompareSearch({1,1,1,3,&centers,row.data(),count}); // equal current distance: no move
        CompareSearch({0,0,0,0,&centers,row.data(),count}); // zero threshold: all pruned
    }
    for (auto& entry : row) entry.distance = 16;
    CompareSearch({1,1,1,4,&centers,row.data(),256}); // exactly on pruning cutoff
    row[255].distance = std::nextafter(16.0, 0.0);
    CompareSearch({1,1,1,4,&centers,row.data(),256}); // strict boundary, last lane

    // Final row byte borders PAGE_NOACCESS. The packed row reads must not overrun
    // at any 1-3 element tail; count zero also must not dereference the row.
    SYSTEM_INFO info{}; GetSystemInfo(&info);
    const size_t readable = std::max<size_t>(info.dwPageSize, sizeof(row));
    auto* memory = static_cast<uint8_t*>(VirtualAlloc(nullptr, readable + info.dwPageSize, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
    Require(memory != nullptr, "nearest guard allocation");
    DWORD old{};
    if (!VirtualProtect(memory + readable, info.dwPageSize, PAGE_NOACCESS, &old)) { VirtualFree(memory,0,MEM_RELEASE); throw std::runtime_error("nearest guard protection"); }
    for (uint32_t count = 0; count <= 256; ++count) {
        auto* guarded = reinterpret_cast<Separation*>(memory + readable - count*sizeof(Separation));
        std::fill_n(guarded, count, Separation{});
        CompareSearch({1,1,1,4,&centers,guarded,count});
    }
    VirtualFree(memory,0,MEM_RELEASE);
    std::cout << "PASS nearest AVX2: 16448 randomized bit-exact searches, ties, cutoff, counts 0..256 and guarded rows\n";
}
void CompareQuantizer(const std::vector<uint32_t>& pixels, uint16_t colors, bool initialized) {
    const auto start = initialized ? mcu::QuantizeWu(pixels,colors) : std::vector<uint32_t>{};
    const auto expected = mcu::QuantizeWsmeansReference(pixels,start,colors);
    const auto actual = mcu::QuantizeWsmeans(pixels,start,colors);
    Require(actual.color_to_count == expected.color_to_count, "quantizer population map differs from original");
    Require(actual.input_pixel_to_cluster_pixel == expected.input_pixel_to_cluster_pixel, "quantizer pixel-to-cluster map differs from original");
    Require(pydeck::colors::AospSeedColors(actual.color_to_count) == pydeck::colors::AospSeedColors(expected.color_to_count), "quantizer ordered seeds differ from original");
}
}
void CheckQuantization() {
    KernelChecks();
    std::vector<std::vector<uint32_t>> fixtures{{},{0xffff0000},{0xffff0000,0xff00ff00,0xff0000ff},std::vector<uint32_t>(12544,0xff777777)};
    std::mt19937 random(123456789);
    for (uint32_t size : {7u,31u,257u,2048u,12543u,12544u}) {
        std::vector<uint32_t> pixels(size);
        for (auto& pixel : pixels) pixel = 0xff000000u | (random() & 0xffffffu);
        fixtures.push_back(pixels);
    }
    std::vector<uint32_t> gradient(12544), repeated(12544);
    for (uint32_t i=0;i<12544;++i) {
        gradient[i] = 0xff000000u | ((i%112)*255/111 << 16) | ((i/112)*255/111 << 8) | ((i%112+i/112)*255/222);
        repeated[i] = std::array<uint32_t,5>{0xffff0000,0xff00ff00,0xff0000ff,0xff777777,0xff778899}[i%5];
    }
    fixtures.push_back(gradient); fixtures.push_back(repeated);
    int count = 0;
    for (const auto& pixels : fixtures) for (uint16_t colors : {uint16_t{1},uint16_t{3},uint16_t{5},uint16_t{127},uint16_t{128},uint16_t{256}}) {
        CompareQuantizer(pixels,colors,true); ++count;
    }
    for (int sample=0;sample<24;++sample) {
        std::vector<uint32_t> pixels(50 + sample*17);
        for (auto& pixel : pixels) pixel = 0xff000000u | (random() & 0xffffffu);
        CompareQuantizer(pixels,static_cast<uint16_t>(1 + sample*7),false); ++count;
    }
    // Verify the actual DLL still performs opaque filtering and seeds match the reference.
    std::vector<uint32_t> mixed(gradient);
    for (size_t i=0;i<mixed.size();i+=3) mixed[i] &= 0x7fffffff;
    std::vector<uint32_t> opaque; for (auto pixel : mixed) if ((pixel>>24)==255) opaque.push_back(pixel);
    const auto wu = mcu::QuantizeWu(opaque,128);
    const auto expected = pydeck::colors::AospSeedColors(mcu::QuantizeWsmeansReference(opaque,wu,128).color_to_count);
    uint32_t seeds[4]{}, written{};
    Require(PyDeckColorsSeeds(mixed.data(),static_cast<uint32_t>(mixed.size()),seeds,4,&written)==0, "quantizer DLL ABI");
    Require(std::vector<uint32_t>(seeds,seeds+written)==expected, "actual quantizer DLL seed parity");
    std::cout << "PASS quantizer: " << count << " full population/pixel-map/seed comparisons against scalar original, plus mixed-alpha DLL parity\n";
}
