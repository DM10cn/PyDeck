#pragma once
#include <array>
#include <functional>

namespace pydeck::setup {
enum class Package { Net, AppRuntime, VisualCpp, App };
enum class Outcome { Success, Cancelled, Restart, Failed, Unsupported };
struct Snapshot { bool supported; std::array<bool, 3> ready; };
struct Result { Outcome outcome; Package package; unsigned long code; };
struct Operations {
    std::function<Snapshot()> inspect;
    std::function<bool()> cancelled;
    std::function<unsigned long(Package)> install;
    std::function<void(Package, bool)> progress; // false = skipped, true = installing
};
inline Result Run(Operations& ops, bool dependenciesOnly) {
    if (!ops.inspect().supported) return {Outcome::Unsupported, Package::App, 1150};
    for (unsigned i = 0; i < 3; ++i) {
        const auto package = static_cast<Package>(i);
        if (ops.cancelled()) return {Outcome::Cancelled, package, 1602};
        if (dependenciesOnly && package == Package::VisualCpp) continue; // MSIX resolves its framework dependencies.
        if (ops.inspect().ready[i]) { ops.progress(package, false); continue; }
        ops.progress(package, true);
        const auto code = ops.install(package);
        if (code == 3010 || code == 1641) return {Outcome::Restart, package, code};
        if (code == 1602 || code == 1223) return {Outcome::Cancelled, package, code};
        if (code) return {Outcome::Failed, package, code};
        if (!ops.inspect().ready[i]) return {Outcome::Failed, package, 126};
    }
    if (ops.cancelled()) return {Outcome::Cancelled, Package::App, 1602};
    const auto final = ops.inspect();
    if (!final.supported) return {Outcome::Unsupported, Package::App, 1150};
    for (unsigned i = 0; i < (dependenciesOnly ? 2u : 3u); ++i)
        if (!final.ready[i]) return {Outcome::Failed, static_cast<Package>(i), 126};
    if (dependenciesOnly) return {Outcome::Success, Package::App, 0};
    ops.progress(Package::App, true);
    const auto code = ops.install(Package::App);
    if (code == 3010 || code == 1641) return {Outcome::Restart, Package::App, code};
    if (code == 1602 || code == 1223) return {Outcome::Cancelled, Package::App, code};
    return {code ? Outcome::Failed : Outcome::Success, Package::App, code};
}
} // namespace pydeck::setup
