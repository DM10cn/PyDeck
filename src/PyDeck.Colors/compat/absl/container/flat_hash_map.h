#pragma once
// MCU only uses lookup/update; WSMeans traversal order lives in a separate vector.
#include <unordered_map>
namespace absl {
template<class Key, class Value>
using flat_hash_map = std::unordered_map<Key, Value>;
}
