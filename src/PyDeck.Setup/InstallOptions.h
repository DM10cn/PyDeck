#pragma once
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <algorithm>
#include <array>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string>

namespace pydeck::setup {
struct InstallOptions {
    std::wstring folder;
    bool desktop = false;
    bool startMenu = true;
};

namespace install_options_detail {
inline constexpr wchar_t RegistryKey[] = L"Software\\DM10cn\\PyDeck\\Installer";
// Leave space for file names in the native MSI's non-extended paths.
inline constexpr size_t MaximumFolderLength = 240;

inline bool DriveLetter(wchar_t value) {
    return (value >= L'A' && value <= L'Z') || (value >= L'a' && value <= L'z');
}
inline void CheckCharacters(const std::wstring& path) {
    if (path.empty()) throw std::invalid_argument("Choose an installation folder");
    for (wchar_t value : path) {
        if (value < L' ' || value == L'\"' || value == L'<' || value == L'>' ||
            value == L'|' || value == L'?' || value == L'*')
            throw std::invalid_argument("The path contains an invalid character");
    }
}
inline size_t RootLength(const std::wstring& path) {
    if (path.size() >= 3 && DriveLetter(path[0]) && path[1] == L':' && path[2] == L'\\') return 3;
    if (path.size() >= 5 && path[0] == L'\\' && path[1] == L'\\') {
        const auto serverEnd = path.find(L'\\', 2);
        if (serverEnd == std::wstring::npos || serverEnd == 2 || serverEnd + 1 >= path.size())
            throw std::invalid_argument("Use an absolute drive or network folder path");
        const auto shareEnd = path.find(L'\\', serverEnd + 1);
        if (shareEnd == serverEnd + 1) throw std::invalid_argument("The network share name is empty");
        return shareEnd == std::wstring::npos ? path.size() : shareEnd + 1;
    }
    throw std::invalid_argument("Use an absolute drive or network folder path");
}
inline bool ReservedComponent(const std::wstring& component) {
    auto stem = component.substr(0, component.find(L'.'));
    for (auto& value : stem) if (value >= L'a' && value <= L'z') value -= L'a' - L'A';
    if (stem == L"CON" || stem == L"PRN" || stem == L"AUX" || stem == L"NUL" ||
        stem == L"CLOCK$" || stem == L"CONIN$" || stem == L"CONOUT$") return true;
    return stem.size() == 4 && (stem.compare(0, 3, L"COM") == 0 || stem.compare(0, 3, L"LPT") == 0) &&
        ((stem[3] >= L'1' && stem[3] <= L'9') || stem[3] == L'\u00b9' || stem[3] == L'\u00b2' || stem[3] == L'\u00b3');
}
inline void CheckComponents(const std::wstring& path, size_t rootLength) {
    for (size_t start = rootLength; start < path.size();) {
        const auto end = path.find(L'\\', start);
        const auto component = path.substr(start, end == std::wstring::npos ? end : end - start);
        if (!component.empty() && component != L"." && component != L"..") {
            if (component.back() == L'.' || component.back() == L' ' || ReservedComponent(component))
                throw std::invalid_argument("The path contains an invalid folder name");
        }
        if (end == std::wstring::npos) break;
        start = end + 1;
    }
}
inline std::wstring QuoteArgument(const std::wstring& value) {
    // Windows command-line quoting: backslashes before quotes (including the
    // closing quote) must be doubled so they cannot escape the argument boundary.
    std::wstring result = L"\"";
    size_t slashes = 0;
    for (wchar_t character : value) {
        if (character == L'\\') { ++slashes; continue; }
        result.append(character == L'\"' ? slashes * 2 + 1 : slashes, L'\\');
        result += character;
        slashes = 0;
    }
    result.append(slashes * 2, L'\\');
    result += L'\"';
    return result;
}
struct ComRelease {
    void operator()(IUnknown* value) const noexcept { if (value) value->Release(); }
};
struct TaskMemoryFree {
    void operator()(wchar_t* value) const noexcept { CoTaskMemFree(value); }
};
} // namespace install_options_detail

inline std::wstring ValidateInstallFolder(std::wstring folder) {
    using namespace install_options_detail;
    CheckCharacters(folder);
    if (folder.size() > MaximumFolderLength) throw std::invalid_argument("The installation folder path is too long");
    std::replace(folder.begin(), folder.end(), L'/', L'\\');
    if (folder.starts_with(L"\\\\.\\") || folder.starts_with(L"\\\\?\\"))
        throw std::invalid_argument("Device paths cannot be used as the installation folder");
    const auto initialRoot = RootLength(folder);
    if (folder.find(L':', initialRoot == 3 ? 2 : 0) != std::wstring::npos)
        throw std::invalid_argument("The path contains an invalid colon");
    CheckComponents(folder, initialRoot);
    std::array<wchar_t, MAX_PATH> full{};
    const auto length = GetFullPathNameW(folder.c_str(), static_cast<DWORD>(full.size()), full.data(), nullptr);
    if (!length || length >= full.size()) throw std::invalid_argument("The installation folder path is invalid or too long");
    folder.assign(full.data(), length);
    const auto rootLength = RootLength(folder);
    while (folder.size() > rootLength && folder.back() == L'\\') folder.pop_back();
    if (folder.size() <= rootLength) throw std::invalid_argument("Choose an application folder instead of a drive or share root");
    if (folder.size() > MaximumFolderLength) throw std::invalid_argument("The installation folder path is too long");
    CheckComponents(folder, rootLength);

    // Inspect existing ancestors without creating a folder or writing a probe.
    // This catches paths below files and inaccessible/missing volumes early.
    auto ancestor = folder;
    for (;;) {
        const auto attributes = GetFileAttributesW(ancestor.c_str());
        if (attributes != INVALID_FILE_ATTRIBUTES) {
            if (!(attributes & FILE_ATTRIBUTE_DIRECTORY))
                throw std::invalid_argument("The installation path or an ancestor is a file");
            break;
        }
        const auto error = GetLastError();
        if ((error != ERROR_FILE_NOT_FOUND && error != ERROR_PATH_NOT_FOUND) || ancestor.size() <= rootLength)
            throw std::invalid_argument("The installation folder or its parent is not accessible");
        const auto separator = ancestor.find_last_of(L'\\');
        ancestor.resize(separator < rootLength ? rootLength : separator);
    }
    return folder;
}

inline InstallOptions LoadInstallOptions() {
    using namespace install_options_detail;
    PWSTR local = nullptr;
    if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, 0, nullptr, &local)))
        throw std::runtime_error("Cannot locate the local application data folder");
    const std::unique_ptr<wchar_t, TaskMemoryFree> localOwner(local);
    InstallOptions options{std::wstring(local) + L"\\Programs\\PyDeck", false, true};
    std::array<wchar_t, MAX_PATH> installed{};
    DWORD size = static_cast<DWORD>(sizeof(installed));
    if (RegGetValueW(HKEY_CURRENT_USER, RegistryKey, L"InstallFolder", RRF_RT_REG_SZ,
                    nullptr, installed.data(), &size) == ERROR_SUCCESS) {
        try {
            const auto characters = static_cast<size_t>(size) / sizeof(wchar_t);
            if (!characters || installed[characters - 1] != L'\0')
                throw std::invalid_argument("The saved installation folder is invalid");
            options.folder = ValidateInstallFolder(std::wstring(installed.data(), characters - 1));
        }
        catch (const std::invalid_argument&) { /* A stale installation location must not block Setup. */ }
    }
    const auto readFlag = [](const wchar_t* name, bool fallback) {
        // MSI persists these properties as REG_SZ values, not DWORDs.
        std::array<wchar_t, 3> text{};
        DWORD bytes = static_cast<DWORD>(sizeof(text));
        if (RegGetValueW(HKEY_CURRENT_USER, RegistryKey, name, RRF_RT_REG_SZ,
                         nullptr, text.data(), &bytes) == ERROR_SUCCESS) {
            if (bytes == 2 * sizeof(wchar_t) && text[1] == L'\0' &&
                (text[0] == L'0' || text[0] == L'1')) return text[0] == L'1';
            return fallback;
        }
        // Accept the boolean DWORD representation used by older installers.
        DWORD value = 0;
        bytes = sizeof(value);
        return RegGetValueW(HKEY_CURRENT_USER, RegistryKey, name, RRF_RT_REG_DWORD,
                           nullptr, &value, &bytes) == ERROR_SUCCESS && value <= 1 ? value == 1 : fallback;
    };
    options.desktop = readFlag(L"DesktopShortcutEnabled", options.desktop);
    options.startMenu = readFlag(L"StartMenuShortcutEnabled", options.startMenu);
    return options;
}

// The caller owns COM initialization on its STA UI thread.
inline std::optional<std::wstring> BrowseInstallFolder(HWND owner, const std::wstring& initial) {
    using namespace install_options_detail;
    IFileOpenDialog* rawDialog = nullptr;
    if (FAILED(CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&rawDialog))))
        throw std::runtime_error("Cannot open the folder picker");
    const std::unique_ptr<IFileOpenDialog, ComRelease> dialog(rawDialog);
    FILEOPENDIALOGOPTIONS flags{};
    if (FAILED(dialog->GetOptions(&flags)) ||
        FAILED(dialog->SetOptions(flags | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR)))
        throw std::runtime_error("Cannot configure the folder picker");
    // An uncreated destination is valid. Start browsing from its nearest
    // existing parent, without creating the requested destination prematurely.
    try {
        auto startingFolder = ValidateInstallFolder(initial);
        while (GetFileAttributesW(startingFolder.c_str()) == INVALID_FILE_ATTRIBUTES) {
            const auto rootLength = RootLength(startingFolder);
            const auto separator = startingFolder.find_last_of(L'\\');
            if (startingFolder.size() <= rootLength) break;
            startingFolder.resize(separator < rootLength ? rootLength : separator);
        }
        IShellItem* rawInitial = nullptr;
        if (SUCCEEDED(SHCreateItemFromParsingName(startingFolder.c_str(), nullptr, IID_PPV_ARGS(&rawInitial)))) {
            const std::unique_ptr<IShellItem, ComRelease> initialItem(rawInitial);
            dialog->SetFolder(initialItem.get());
        }
    } catch (const std::invalid_argument&) { /* Let the picker use its normal starting folder. */ }
    const auto shown = dialog->Show(owner);
    if (shown == HRESULT_FROM_WIN32(ERROR_CANCELLED)) return std::nullopt;
    if (FAILED(shown)) throw std::runtime_error("Cannot select an installation folder");
    IShellItem* rawResult = nullptr;
    if (FAILED(dialog->GetResult(&rawResult))) throw std::runtime_error("Cannot read the selected folder");
    const std::unique_ptr<IShellItem, ComRelease> result(rawResult);
    PWSTR path = nullptr;
    if (FAILED(result->GetDisplayName(SIGDN_FILESYSPATH, &path))) throw std::runtime_error("The selected folder has no filesystem path");
    const std::unique_ptr<wchar_t, TaskMemoryFree> pathOwner(path);
    return ValidateInstallFolder(path);
}

inline std::wstring BuildMsiArguments(const std::wstring& path, const std::wstring& logs,
                                      const std::wstring& styleName, const InstallOptions& options) {
    using namespace install_options_detail;
    if (styleName != L"Fluent" && styleName != L"Material")
        throw std::invalid_argument("Unknown initial interface style");
    CheckCharacters(path);
    CheckCharacters(logs);
    const auto folder = ValidateInstallFolder(options.folder);
    const auto logPath = logs + (logs.back() == L'\\' || logs.back() == L'/' ? L"" : L"\\") + L"msi.log";
    return L"/i " + QuoteArgument(path) + L" /qn /norestart /L*V " + QuoteArgument(logPath) +
        L" REBOOT=ReallySuppress INSTALLFOLDER=" + QuoteArgument(folder) +
        L" DESKTOPSHORTCUT=" + (options.desktop ? L"1" : L"0") +
        L" STARTMENUSHORTCUT=" + (options.startMenu ? L"1" : L"0") + L" PYDECKSTYLE=" + styleName;
}
} // namespace pydeck::setup
