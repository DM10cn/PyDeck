#pragma once
#include "../PyDeck.Launcher/Prerequisites.h"
#include <bcrypt.h>
#include <sddl.h>
#include <shlobj.h>
#include <stdexcept>
#include <utility>

namespace pydeck::setup {
struct Handle {
    HANDLE value = INVALID_HANDLE_VALUE;
    Handle() = default;
    explicit Handle(HANDLE h) : value(h) {}
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
    Handle(Handle&& other) noexcept : value(std::exchange(other.value, INVALID_HANDLE_VALUE)) {}
};
inline std::wstring Sha256(const BYTE* data, ULONG size) {
    std::array<BYTE, 32> hash{};
    if (BCryptHash(BCRYPT_SHA256_ALG_HANDLE, nullptr, 0, const_cast<BYTE*>(data), size, hash.data(), static_cast<ULONG>(hash.size())) < 0)
        throw std::runtime_error("Cannot calculate payload SHA256");
    constexpr wchar_t hex[] = L"0123456789abcdef";
    std::wstring result;
    for (BYTE b : hash) { result += hex[b >> 4]; result += hex[b & 15]; }
    return result;
}
inline void WriteUtf8(HANDLE file, const std::wstring& text) {
    const auto size = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    if (!size) throw std::runtime_error("Cannot encode log");
    std::string bytes(size, '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()), bytes.data(), size, nullptr, nullptr);
    DWORD written = 0;
    if (!WriteFile(file, bytes.data(), static_cast<DWORD>(size), &written, nullptr) || written != static_cast<DWORD>(size))
        throw std::runtime_error("Cannot write log");
}
inline std::wstring PrivateDirectory(const std::wstring& parentDirectory = {}) {
    Handle token;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token.value)) throw std::runtime_error("Cannot read user identity");
    DWORD size = 0;
    GetTokenInformation(token.value, TokenUser, nullptr, 0, &size);
    std::vector<BYTE> buffer(size);
    if (!GetTokenInformation(token.value, TokenUser, buffer.data(), size, &size)) throw std::runtime_error("Cannot read user identity");
    LPWSTR sid = nullptr;
    if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(buffer.data())->User.Sid, &sid)) throw std::runtime_error("Cannot read user SID");
    const auto acl = L"D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FA;;;" + std::wstring(sid) + L")";
    LocalFree(sid);
    PSECURITY_DESCRIPTOR descriptor = nullptr;
    if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(acl.c_str(), SDDL_REVISION_1, &descriptor, nullptr)) throw std::runtime_error("Cannot protect setup directory");
    SECURITY_ATTRIBUTES attributes{sizeof(attributes), descriptor, FALSE};
    std::wstring parent = parentDirectory;
    if (parent.empty()) {
        PWSTR local = nullptr;
        const auto hr = SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local);
        if (FAILED(hr)) { LocalFree(descriptor); throw std::runtime_error("Cannot locate user directory"); }
        parent = local;
        CoTaskMemFree(local);
    }
    GUID guid{};
    const auto guidHr = CoCreateGuid(&guid);
    std::array<wchar_t, 40> name{};
    StringFromGUID2(guid, name.data(), static_cast<int>(name.size()));
    const auto path = parent + L"\\PyDeck-Setup-" + name.data();
    const bool made = SUCCEEDED(guidHr) && CreateDirectoryW(path.c_str(), &attributes);
    LocalFree(descriptor);
    if (!made) throw std::runtime_error("Cannot create private setup directory");
    return path;
}
// Validate embedded bytes before writing. Hold every extracted file read-only until its child exits.
inline Handle Extract(int id, const std::wstring& path, const wchar_t* expectedHash) {
    const auto resource = FindResourceW(nullptr, MAKEINTRESOURCEW(id), RT_RCDATA);
    if (!resource) throw std::runtime_error("Embedded installer missing");
    const auto loaded = LoadResource(nullptr, resource);
    const auto data = static_cast<const BYTE*>(LockResource(loaded));
    const auto size = SizeofResource(nullptr, resource);
    if (!data || !size || Sha256(data, size) != expectedHash) throw std::runtime_error("Embedded installer integrity check failed");
    {
        Handle file(CreateFileW(path.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr));
        if (file.value == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot extract installer");
        DWORD written = 0;
        if (!WriteFile(file.value, data, size, &written, nullptr) || written != size || !FlushFileBuffers(file.value)) throw std::runtime_error("Cannot finish extracting installer");
    }
    Handle file(CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_OPEN_REPARSE_POINT, nullptr));
    if (file.value == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot lock extracted installer");
    BY_HANDLE_FILE_INFORMATION info{};
    if (!GetFileInformationByHandle(file.value, &info) || (info.dwFileAttributes & FILE_ATTRIBUTE_REPARSE_POINT) || info.nFileSizeHigh || info.nFileSizeLow != size)
        throw std::runtime_error("Extracted installer changed");
    Handle mapping(CreateFileMappingW(file.value, nullptr, PAGE_READONLY, 0, 0, nullptr));
    const auto view = static_cast<const BYTE*>(MapViewOfFile(mapping.value, FILE_MAP_READ, 0, 0, 0));
    if (!view) throw std::runtime_error("Cannot verify extracted installer");
    std::wstring actual;
    try { actual = Sha256(view, size); } catch (...) { UnmapViewOfFile(view); throw; }
    UnmapViewOfFile(view);
    if (actual != expectedHash) throw std::runtime_error("Extracted installer integrity check failed");
    return file;
}
} // namespace pydeck::setup
