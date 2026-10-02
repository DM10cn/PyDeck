#pragma once
// Formatting-only compatibility for MCU HexFromArgb; no color math.
#include <cstdint>
#include <iomanip>
#include <sstream>
#include <string>
namespace absl {
struct Hex { explicit Hex(uint32_t number) : value(number) {} uint32_t value; };
inline std::string StrCat(Hex hex) {
    std::ostringstream stream;
    stream << std::hex << hex.value;
    return stream.str();
}
}
