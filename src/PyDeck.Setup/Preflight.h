#pragma once
#include "../PyDeck.Launcher/Prerequisites.h"
#include <msi.h>
#include <tlhelp32.h>
#include <algorithm>
#include <stdexcept>
#include <utility>

namespace pydeck::setup {
enum class InstallRelation { Fresh, Upgrade, Repair, SameVersionConflict, NewerInstalled };
struct ProductState {
    InstallRelation relation = InstallRelation::Fresh;
    std::wstring productCode, version, folder;
};

namespace preflight_detail {
inline bool Equal(const std::wstring& left, const std::wstring& right) {
    return CompareStringOrdinal(left.c_str(), -1, right.c_str(), -1, TRUE) == CSTR_EQUAL;
}

inline std::wstring ProductProperty(const wchar_t* code, MSIINSTALLCONTEXT context,
                                    const wchar_t* property, bool optional = false) {
    DWORD count = 0;
    auto result = MsiGetProductInfoExW(code, nullptr, context, property, nullptr, &count);
    if (result == ERROR_UNKNOWN_PRODUCT || (optional && result == ERROR_UNKNOWN_PROPERTY)) return {};
    if ((result != ERROR_SUCCESS && result != ERROR_MORE_DATA) || count > 32767)
        throw std::runtime_error("Cannot inspect installed PyDeck product");
    std::vector<wchar_t> value(static_cast<size_t>(count) + 1);
    ++count;
    result = MsiGetProductInfoExW(code, nullptr, context, property, value.data(), &count);
    // A product can be removed between the size query and the value query.
    if (result == ERROR_UNKNOWN_PRODUCT || (optional && result == ERROR_UNKNOWN_PROPERTY)) return {};
    if (result != ERROR_SUCCESS) throw std::runtime_error("Cannot read installed PyDeck product");
    return std::wstring(value.data(), count);
}

inline std::wstring SavedFolder() {
    std::array<wchar_t, 32768> value{};
    DWORD size = static_cast<DWORD>(value.size() * sizeof(wchar_t));
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\DM10cn\\PyDeck\\Installer", L"InstallFolder",
                    RRF_RT_REG_SZ, nullptr, value.data(), &size) != ERROR_SUCCESS) return {};
    return value.data();
}

inline std::array<unsigned, 4> ProductVersion(const std::wstring& value) {
    std::array<unsigned, 4> version{};
    if (!prerequisites::Version(value, version, 3) && !prerequisites::Version(value, version, 4))
        throw std::runtime_error("Cannot compare PyDeck installer versions");
    version[3] = 0; // Windows Installer compares the first three version fields.
    return version;
}

struct ScopedHandle {
    HANDLE value;
    explicit ScopedHandle(HANDLE handle) : value(handle) {}
    ~ScopedHandle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    ScopedHandle(const ScopedHandle&) = delete;
    ScopedHandle& operator=(const ScopedHandle&) = delete;
};

inline std::wstring NormalizedPath(std::wstring path) {
    if (path.empty()) return {};
    std::replace(path.begin(), path.end(), L'/', L'\\');
    if (!((path.size() >= 3 && path[1] == L':' && path[2] == L'\\') || path.starts_with(L"\\\\")))
        return {};
    std::array<wchar_t, 32768> buffer{};
    const DWORD count = GetFullPathNameW(path.c_str(), static_cast<DWORD>(buffer.size()), buffer.data(), nullptr);
    if (!count || count >= buffer.size()) return {};
    path.assign(buffer.data(), count);
    // Resolve existing junctions and short names; absent installation folders still
    // retain a normalized absolute path for a precise comparison.
    ScopedHandle file(CreateFileW(path.c_str(), 0, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                                  nullptr, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, nullptr));
    if (file.value != INVALID_HANDLE_VALUE) {
        const DWORD finalCount = GetFinalPathNameByHandleW(file.value, buffer.data(),
            static_cast<DWORD>(buffer.size()), FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
        if (finalCount && finalCount < buffer.size()) path.assign(buffer.data(), finalCount);
    }
    if (path.starts_with(L"\\\\?\\UNC\\")) path = L"\\\\" + path.substr(8);
    else if (path.starts_with(L"\\\\?\\")) path.erase(0, 4);
    while (path.size() > 3 && path.back() == L'\\') path.pop_back();
    return path;
}
} // namespace preflight_detail

// packageCode is the embedded MSI's ProductCode, not its PackageCode.
// Inspection errors throw rather than incorrectly allowing a fresh installation.
inline ProductState InspectProduct(const wchar_t* packageCode, const wchar_t* packageVersion) {
    using namespace preflight_detail;
    if (!packageCode || !*packageCode || !packageVersion || !*packageVersion)
        throw std::runtime_error("Missing PyDeck MSI identity");
    const auto targetVersion = ProductVersion(packageVersion);
    ProductState found;
    std::array<unsigned, 4> foundVersion{};
    for (DWORD index = 0; ; ++index) {
        std::array<wchar_t, 39> code{};
        const auto result = MsiEnumRelatedProductsW(L"{D43AFAF7-DFE0-4AC1-A0A3-8F73AD6F89CA}", 0, index, code.data());
        if (result == ERROR_NO_MORE_ITEMS) break;
        if (result != ERROR_SUCCESS) throw std::runtime_error("Cannot enumerate installed PyDeck products");
        for (const auto context : {MSIINSTALLCONTEXT_USERUNMANAGED, MSIINSTALLCONTEXT_USERMANAGED}) {
            // NULL SID plus an explicit per-user context excludes machine installs
            // and other accounts even when the UpgradeCode enumeration finds them.
            if (ProductProperty(code.data(), context, INSTALLPROPERTY_PRODUCTSTATE) != L"5") continue;
            const auto versionText = ProductProperty(code.data(), context, INSTALLPROPERTY_VERSIONSTRING);
            if (versionText.empty()) continue; // Removed concurrently.
            const auto version = ProductVersion(versionText);
            const auto relation = version > targetVersion ? InstallRelation::NewerInstalled :
                Equal(code.data(), packageCode) ? InstallRelation::Repair :
                version == targetVersion ? InstallRelation::SameVersionConflict : InstallRelation::Upgrade;
            // An existing conflict must not be hidden by another older registration.
            if (relation < found.relation || (relation == found.relation && version <= foundVersion)) continue;
            auto folder = ProductProperty(code.data(), context, INSTALLPROPERTY_INSTALLLOCATION, true);
            if (folder.empty()) folder = SavedFolder();
            found = {relation, code.data(), versionText, std::move(folder)};
            foundVersion = version;
        }
    }
    return found;
}

inline bool IsPyDeckRunning(const std::vector<std::wstring>& folders) {
    using namespace preflight_detail;
    std::vector<std::wstring> executables;
    for (const auto& folder : folders) {
        auto path = NormalizedPath(folder);
        if (path.empty()) continue;
        if (path.back() != L'\\') path += L'\\';
        for (const auto name : {L"PyDeck.exe", L"PyDeck.Launcher.exe"})
            executables.push_back(NormalizedPath(path + name));
    }
    if (executables.empty()) return false;
    ScopedHandle snapshot(CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0));
    if (snapshot.value == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot check running PyDeck processes");
    PROCESSENTRY32W entry{};
    entry.dwSize = sizeof(entry);
    if (!Process32FirstW(snapshot.value, &entry)) {
        if (GetLastError() == ERROR_NO_MORE_FILES) return false;
        throw std::runtime_error("Cannot enumerate running PyDeck processes");
    }
    do {
        if (!Equal(entry.szExeFile, L"PyDeck.exe") && !Equal(entry.szExeFile, L"PyDeck.Launcher.exe")) continue;
        ScopedHandle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, entry.th32ProcessID));
        if (!process.value) continue; // It may have exited or belong to an inaccessible account.
        std::array<wchar_t, 32768> image{};
        DWORD count = static_cast<DWORD>(image.size());
        if (!QueryFullProcessImageNameW(process.value, 0, image.data(), &count)) continue;
        const auto executable = NormalizedPath(std::wstring(image.data(), count));
        if (!executable.empty() && std::any_of(executables.begin(), executables.end(),
            [&](const auto& candidate) { return Equal(candidate, executable); })) return true;
    } while (Process32NextW(snapshot.value, &entry));
    if (GetLastError() != ERROR_NO_MORE_FILES) throw std::runtime_error("Cannot finish checking PyDeck processes");
    return false;
}
} // namespace pydeck::setup
