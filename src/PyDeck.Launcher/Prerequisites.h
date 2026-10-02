#pragma once
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <appmodel.h>
#include <array>
#include <string>
#include <vector>

// Shared by the installed launcher and the offline setup; never loads runtime DLLs.
namespace pydeck::prerequisites {
struct State {
    bool windows = false, net = false, runtime = false, vc = false, packaged = false;
    bool Ready() const { return windows && net && runtime && (vc || packaged); }
    unsigned Missing() const { return (windows ? 0 : 1) | (net ? 0 : 2) | (runtime ? 0 : 4) | (vc || packaged ? 0 : 8); }
};
inline bool FileExists(const std::wstring& path) {
    const DWORD attrs = GetFileAttributesW(path.c_str());
    return attrs != INVALID_FILE_ATTRIBUTES && !(attrs & FILE_ATTRIBUTE_DIRECTORY);
}
inline bool Amd64Image(const std::wstring& path) {
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
        nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    IMAGE_DOS_HEADER dos{};
    DWORD read = 0;
    bool valid = ReadFile(file, &dos, sizeof(dos), &read, nullptr) && read == sizeof(dos) &&
        dos.e_magic == IMAGE_DOS_SIGNATURE && dos.e_lfanew >= sizeof(dos) && dos.e_lfanew <= 1048576;
    if (valid) {
        SetFilePointer(file, dos.e_lfanew, nullptr, FILE_BEGIN);
        struct { DWORD signature; IMAGE_FILE_HEADER header; } pe{};
        valid = ReadFile(file, &pe, sizeof(pe), &read, nullptr) && read == sizeof(pe) &&
            pe.signature == IMAGE_NT_SIGNATURE && pe.header.Machine == IMAGE_FILE_MACHINE_AMD64;
    }
    CloseHandle(file);
    return valid;
}
inline std::wstring Environment(const wchar_t* name) {
    const DWORD size = GetEnvironmentVariableW(name, nullptr, 0);
    if (!size || size > 32768) return {};
    std::vector<wchar_t> buffer(size);
    const DWORD read = GetEnvironmentVariableW(name, buffer.data(), size);
    return read && read < size ? std::wstring(buffer.data(), read) : L"";
}
inline std::wstring RegistryString(const wchar_t* key, const wchar_t* name) {
    std::array<wchar_t, 32768> value{};
    DWORD size = static_cast<DWORD>(value.size() * sizeof(wchar_t));
    if (RegGetValueW(HKEY_LOCAL_MACHINE, key, name, RRF_RT_REG_SZ | RRF_SUBKEY_WOW6464KEY,
                    nullptr, value.data(), &size) != ERROR_SUCCESS) return {};
    return value.data();
}
inline bool RegistryDword(const wchar_t* key, const wchar_t* name, DWORD& value) {
    DWORD size = sizeof(value);
    return RegGetValueW(HKEY_LOCAL_MACHINE, key, name, RRF_RT_REG_DWORD | RRF_SUBKEY_WOW6464KEY,
                        nullptr, &value, &size) == ERROR_SUCCESS;
}
inline bool Version(const std::wstring& text, std::array<unsigned, 4>& parts, unsigned count) {
    parts = {};
    size_t at = 0;
    for (unsigned i = 0; i < count; ++i) {
        const size_t start = at;
        while (at < text.size() && text[at] >= L'0' && text[at] <= L'9') {
            parts[i] = parts[i] * 10 + (text[at++] - L'0');
            if (parts[i] > 65535) return false;
        }
        if (at == start) return false;
        if (i + 1 < count && (at == text.size() || text[at++] != L'.')) return false;
    }
    return at == text.size(); // Do not accept previews or malformed version suffixes.
}
inline bool HasNet(const std::wstring& root) {
    const auto shared = root + L"\\shared\\Microsoft.NETCore.App\\";
    WIN32_FIND_DATAW entry{};
    HANDLE find = FindFirstFileW((shared + L"*").c_str(), &entry);
    if (find == INVALID_HANDLE_VALUE) return false;
    bool found = false;
    do {
        std::array<unsigned, 4> version{};
        if ((entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && Version(entry.cFileName, version, 3) &&
            version[0] == 10 && version[1] == 0 &&
            Amd64Image(shared + entry.cFileName + L"\\coreclr.dll") &&
            FileExists(shared + entry.cFileName + L"\\hostpolicy.dll") &&
            FileExists(shared + entry.cFileName + L"\\System.Private.CoreLib.dll")) { found = true; break; }
    } while (FindNextFileW(find, &entry));
    FindClose(find);
    if (!found || !FileExists(root + L"\\dotnet.exe")) return false;
    const auto fxr = root + L"\\host\\fxr\\";
    find = FindFirstFileW((fxr + L"*").c_str(), &entry);
    if (find == INVALID_HANDLE_VALUE) return false;
    found = false;
    do {
        std::array<unsigned, 4> version{};
        if ((entry.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) && Version(entry.cFileName, version, 3) &&
            version[0] >= 10 && Amd64Image(fxr + entry.cFileName + L"\\hostfxr.dll")) { found = true; break; }
    } while (FindNextFileW(find, &entry));
    FindClose(find);
    return found;
}
inline bool HasRuntime() {
    // Enumerate registration for this user, not provisioning for a different account.
    constexpr auto family = L"Microsoft.WindowsAppRuntime.2_8wekyb3d8bbwe";
    for (int attempt = 0; attempt < 3; ++attempt) {
        UINT32 count = 0, length = 0;
        const LONG query = GetPackagesByPackageFamily(family, &count, nullptr, &length, nullptr);
        if (query != ERROR_INSUFFICIENT_BUFFER || !count || count > 4096 || length > 1048576) return false;
        std::vector<PWSTR> names(count);
        std::vector<wchar_t> buffer(length);
        const LONG read = GetPackagesByPackageFamily(family, &count, names.data(), &length, buffer.data());
        if (read == ERROR_INSUFFICIENT_BUFFER) continue;
        if (read != ERROR_SUCCESS) return false;
        for (UINT32 i = 0; i < count; ++i) {
            UINT32 bytes = 0;
            if (PackageIdFromFullName(names[i], 0, &bytes, nullptr) != ERROR_INSUFFICIENT_BUFFER || bytes > 65536) continue;
            std::vector<BYTE> idBuffer(bytes);
            if (PackageIdFromFullName(names[i], 0, &bytes, idBuffer.data()) != ERROR_SUCCESS) continue;
            const auto id = reinterpret_cast<const PACKAGE_ID*>(idBuffer.data());
            if (id->processorArchitecture != PROCESSOR_ARCHITECTURE_AMD64 || id->version.Major != 2 ||
                id->version.Version < (2ull << 48 | 5ull << 32 | 1ull << 16)) continue;
            UINT32 pathSize = 0;
            if (GetPackagePathByFullName(names[i], &pathSize, nullptr) != ERROR_INSUFFICIENT_BUFFER || pathSize > 32768) continue;
            std::vector<wchar_t> path(pathSize);
            if (GetPackagePathByFullName(names[i], &pathSize, path.data()) == ERROR_SUCCESS &&
                FileExists(std::wstring(path.data()) + L"\\Microsoft.UI.Xaml.dll")) return true;
        }
        return false;
    }
    return false;
}
inline State Inspect() {
    State state;
    SYSTEM_INFO info{};
    GetNativeSystemInfo(&info);
    const auto build = RegistryString(L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", L"CurrentBuildNumber");
    std::array<unsigned, 4> version{};
    state.windows = info.wProcessorArchitecture == PROCESSOR_ARCHITECTURE_AMD64 &&
        Version(build, version, 1) && version[0] >= 22000;
    UINT32 packageSize = 0;
    state.packaged = GetCurrentPackageFullName(&packageSize, nullptr) == ERROR_INSUFFICIENT_BUFFER;
    // Match the x64 apphost's environment / registry / default root precedence.
    auto root = Environment(L"DOTNET_ROOT_X64");
    if (root.empty()) root = Environment(L"DOTNET_ROOT");
    if (root.empty()) root = RegistryString(L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64", L"InstallLocation");
    if (root.empty()) root = Environment(L"ProgramFiles") + L"\\dotnet";
    state.net = HasNet(root);
    state.runtime = HasRuntime();
    DWORD installed = 0, major = 0;
    constexpr auto vcKey = L"SOFTWARE\\Microsoft\\VisualStudio\\14.0\\VC\\Runtimes\\x64";
    std::array<wchar_t, MAX_PATH> system{};
    GetSystemDirectoryW(system.data(), static_cast<UINT>(system.size()));
    state.vc = RegistryDword(vcKey, L"Installed", installed) && installed == 1 &&
        RegistryDword(vcKey, L"Major", major) && major == 14 &&
        FileExists(std::wstring(system.data()) + L"\\msvcp140.dll") &&
        FileExists(std::wstring(system.data()) + L"\\vcruntime140.dll") &&
        FileExists(std::wstring(system.data()) + L"\\vcruntime140_1.dll");
    return state;
}
} // namespace pydeck::prerequisites
