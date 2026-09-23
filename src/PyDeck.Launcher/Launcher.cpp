// This entry point must only import Windows system DLLs. Build with /MT.
// Never load .NET, WinUI, WebView2, PowerShell or a DLL beside this executable.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <appmodel.h>
#include <commctrl.h>
#include <shellapi.h>
#include <array>
#include <string>
#include <vector>
#include <cwctype>
#include "resource.h"

namespace {
constexpr const wchar_t* NetUrl = L"https://dotnet.microsoft.com/download/dotnet/10.0";
constexpr const wchar_t* RuntimeUrl = L"https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads";
constexpr const wchar_t* VcUrl = L"https://aka.ms/vc14/vc_redist.x64.exe";
struct State {
    bool windows = false, net = false, runtime = false, vc = false, packaged = false;
    bool Ready() const { return windows && net && runtime && (vc || packaged); }
    unsigned Missing() const { return (windows ? 0 : 1) | (net ? 0 : 2) | (runtime ? 0 : 4) | (vc || packaged ? 0 : 8); }
};
bool FileExists(const std::wstring& path) {
    const DWORD attrs = GetFileAttributesW(path.c_str());
    return attrs != INVALID_FILE_ATTRIBUTES && !(attrs & FILE_ATTRIBUTE_DIRECTORY);
}
bool Amd64Image(const std::wstring& path) {
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
std::wstring Environment(const wchar_t* name) {
    const DWORD size = GetEnvironmentVariableW(name, nullptr, 0);
    if (!size || size > 32768) return {};
    std::vector<wchar_t> buffer(size);
    const DWORD read = GetEnvironmentVariableW(name, buffer.data(), size);
    return read && read < size ? std::wstring(buffer.data(), read) : L"";
}
std::wstring RegistryString(const wchar_t* key, const wchar_t* name) {
    std::array<wchar_t, 32768> value{};
    DWORD size = static_cast<DWORD>(value.size() * sizeof(wchar_t));
    if (RegGetValueW(HKEY_LOCAL_MACHINE, key, name, RRF_RT_REG_SZ | RRF_SUBKEY_WOW6464KEY,
                    nullptr, value.data(), &size) != ERROR_SUCCESS) return {};
    return value.data();
}
bool RegistryDword(const wchar_t* key, const wchar_t* name, DWORD& value) {
    DWORD size = sizeof(value);
    return RegGetValueW(HKEY_LOCAL_MACHINE, key, name, RRF_RT_REG_DWORD | RRF_SUBKEY_WOW6464KEY,
                        nullptr, &value, &size) == ERROR_SUCCESS;
}
bool Version(const std::wstring& text, std::array<unsigned, 4>& parts, unsigned count) {
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
bool HasNet(const std::wstring& root) {
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
bool HasRuntime() {
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
State Inspect() {
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
struct Words { const wchar_t *title, *intro, *ready, *missing, *provided, *download, *recheck, *start, *close, *note, *standalone, *launchError, *browserError; };
constexpr Words Languages[] = {
    {L"PyDeck prerequisites", L"Install any missing prerequisites, then check again\nDownloads open in your browser", L"Ready", L"Missing or incomplete", L"Provided by package", L"Download", L"Check again", L"Open PyDeck", L"Close", L"Choose x64 installers\nPython Install Manager can be added inside PyDeck", L"Standalone check: return to your MSI or MSIX after installing prerequisites\nFor an installed app, use its Start menu shortcut", L"PyDeck could not start. Repair its installation and check the prerequisites again.", L"Could not open your browser. Copy this address into a browser:"},
    {L"PyDeck 运行依赖", L"安装缺少的依赖后，点击重新检查\n下载链接会在浏览器中打开", L"已就绪", L"缺失或不完整", L"由安装包提供", L"下载", L"重新检查", L"打开 PyDeck", L"关闭", L"请选择 x64 安装程序\n进入 PyDeck 后可补装 Python Install Manager", L"独立检查工具：补齐依赖后，返回 MSI 或 MSIX 安装程序\n已安装的应用请从开始菜单打开", L"PyDeck 无法启动，请修复应用安装并重新检查依赖", L"无法打开浏览器，请复制此地址到浏览器："},
    {L"PyDeck 執行依賴", L"安裝缺少的依賴後，點選重新檢查\n下載連結會在瀏覽器中開啟", L"已就緒", L"缺少或不完整", L"由套件提供", L"下載", L"重新檢查", L"開啟 PyDeck", L"關閉", L"請選擇 x64 安裝程式\n進入 PyDeck 後可補裝 Python Install Manager", L"獨立檢查工具：補齊依賴後，返回 MSI 或 MSIX 安裝程式\n已安裝的應用程式請從開始功能表開啟", L"PyDeck 無法啟動，請修復應用程式安裝並重新檢查依賴", L"無法開啟瀏覽器，請複製此網址到瀏覽器："},
    {L"PyDeck の実行環境", L"不足しているランタイムをインストールして再確認してください\nダウンロード先をブラウザーで開きます", L"準備完了", L"未導入または不完全", L"パッケージに付属", L"ダウンロード", L"再確認", L"PyDeck を開く", L"閉じる", L"x64 のインストーラーを選んでください\nPython Install Manager は PyDeck 内から追加できます", L"単体の確認ツール：準備ができたら MSI または MSIX に戻ってください\n導入済みのアプリはスタートメニューから開けます", L"PyDeck を起動できませんでした。アプリを修復して実行環境を再確認してください", L"ブラウザーを開けませんでした。このアドレスをコピーしてください："}
};
struct DialogState { State state; unsigned language = 0; bool appExists = false; };
void Render(HWND dialog, DialogState& model) {
    const auto& text = Languages[model.language];
    SetWindowTextW(dialog, text.title);
    SetDlgItemTextW(dialog, IDC_INTRO, text.intro);
    auto status = [&](int id, const wchar_t* name, bool ready, bool supplied = false) {
        const auto value = std::wstring(name) + L"  -  " + (supplied ? text.provided : ready ? text.ready : text.missing);
        SetDlgItemTextW(dialog, id, value.c_str());
    };
    status(IDC_WINDOWS, L"Windows 11 x64", model.state.windows);
    status(IDC_NET, L".NET Runtime 10.0 x64", model.state.net);
    status(IDC_APP_RUNTIME, L"Windows App Runtime 2.5.1+ (2.x) x64", model.state.runtime);
    status(IDC_VC, L"Visual C++ v14 x64", model.state.vc, model.state.packaged);
    for (int id : {IDC_DOWNLOAD_NET, IDC_DOWNLOAD_APP_RUNTIME, IDC_DOWNLOAD_VC}) SetDlgItemTextW(dialog, id, text.download);
    SetDlgItemTextW(dialog, IDC_RECHECK, text.recheck);
    SetDlgItemTextW(dialog, IDOK, text.start);
    SetDlgItemTextW(dialog, IDCANCEL, text.close);
    SetDlgItemTextW(dialog, IDC_NOTE, model.appExists ? text.note : text.standalone);
    EnableWindow(GetDlgItem(dialog, IDOK), model.appExists && model.state.Ready());
}
void OpenDownload(HWND dialog, unsigned language, const wchar_t* url) {
    // Fixed HTTPS allowlist only. Do not download, execute installers, or elevate.
    if (reinterpret_cast<INT_PTR>(ShellExecuteW(dialog, L"open", url, nullptr, nullptr, SW_SHOWNORMAL)) <= 32) {
        const auto message = std::wstring(Languages[language].browserError) + L"\n\n" + url;
        MessageBoxW(dialog, message.c_str(), L"PyDeck", MB_OK | MB_ICONINFORMATION);
    }
}
INT_PTR CALLBACK DialogProc(HWND dialog, UINT message, WPARAM wParam, LPARAM lParam) {
    auto model = reinterpret_cast<DialogState*>(GetWindowLongPtrW(dialog, DWLP_USER));
    if (message == WM_INITDIALOG) {
        model = reinterpret_cast<DialogState*>(lParam);
        SetWindowLongPtrW(dialog, DWLP_USER, lParam);
        SendMessageW(dialog, WM_SETICON, ICON_BIG, reinterpret_cast<LPARAM>(LoadIconW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDI_APP))));
        for (auto language : {L"English", L"简体中文", L"繁體中文（台灣）", L"日本語"})
            SendDlgItemMessageW(dialog, IDC_LANGUAGE, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(language));
        SendDlgItemMessageW(dialog, IDC_LANGUAGE, CB_SETCURSEL, model->language, 0);
        Render(dialog, *model);
        return TRUE;
    }
    if (!model) return FALSE;
    if (message == WM_COMMAND) {
        switch (LOWORD(wParam)) {
        case IDCANCEL: EndDialog(dialog, IDCANCEL); return TRUE;
        case IDC_LANGUAGE:
            if (HIWORD(wParam) == CBN_SELCHANGE) {
                const auto selected = SendDlgItemMessageW(dialog, IDC_LANGUAGE, CB_GETCURSEL, 0, 0);
                if (selected >= 0 && selected < 4) model->language = static_cast<unsigned>(selected);
                Render(dialog, *model);
            }
            return TRUE;
        case IDC_DOWNLOAD_NET: OpenDownload(dialog, model->language, NetUrl); return TRUE;
        case IDC_DOWNLOAD_APP_RUNTIME: OpenDownload(dialog, model->language, RuntimeUrl); return TRUE;
        case IDC_DOWNLOAD_VC: OpenDownload(dialog, model->language, VcUrl); return TRUE;
        case IDC_RECHECK:
        case IDOK:
            model->state = Inspect(); // Revalidate on Continue as well as Check again.
            Render(dialog, *model);
            if (LOWORD(wParam) == IDOK && model->appExists && model->state.Ready()) EndDialog(dialog, IDOK);
            return TRUE;
        }
    }
    return FALSE;
}
std::wstring AppPath() {
    std::array<wchar_t, 32768> path{};
    const DWORD length = GetModuleFileNameW(nullptr, path.data(), static_cast<DWORD>(path.size()));
    if (!length || length == path.size()) return {};
    std::wstring directory(path.data(), length);
    const auto separator = directory.find_last_of(L"\\/");
    return separator == std::wstring::npos ? L"" : directory.substr(0, separator + 1) + L"PyDeck.exe";
}
int Report(const State& state) {
    const auto boolean = [](bool value) { return value ? "true" : "false"; };
    const std::string report = std::string("{\"windows\":") + boolean(state.windows) + ",\"net\":" + boolean(state.net) +
        ",\"windowsAppRuntime\":" + boolean(state.runtime) + ",\"visualCpp\":" + boolean(state.vc) +
        ",\"packaged\":" + boolean(state.packaged) + ",\"ready\":" + boolean(state.Ready()) + "}\r\n";
    DWORD written = 0;
    WriteFile(GetStdHandle(STD_OUTPUT_HANDLE), report.data(), static_cast<DWORD>(report.size()), &written, nullptr);
    return static_cast<int>(state.Missing());
}
} // namespace

#ifndef PYDECK_LAUNCHER_TEST
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR arguments, int) {
    // Remove the current working directory from legacy DLL search paths.
    SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);
    SetDllDirectoryW(L"");
    const std::wstring args(arguments);
    auto state = Inspect();
    if (args == L"--check") return Report(state);
    const auto app = AppPath();
    // The separately distributed helper never launches a sibling app, even if one is present.
    std::array<wchar_t, 32768> module{};
    GetModuleFileNameW(nullptr, module.data(), static_cast<DWORD>(module.size()));
    const std::wstring modulePath(module.data());
    const bool standalone = _wcsicmp(modulePath.substr(modulePath.find_last_of(L"\\/") + 1).c_str(), L"PyDeck.Launcher.exe") != 0;
    DialogState model{state, 0, !standalone && FileExists(app)};
    const bool show = args == L"--dependencies" || !model.appExists || !state.Ready();
    if (show) {
        INITCOMMONCONTROLSEX controls{sizeof(controls), ICC_STANDARD_CLASSES};
        InitCommonControlsEx(&controls);
        const auto result = DialogBoxParamW(instance, MAKEINTRESOURCEW(IDD_DEPENDENCIES), nullptr, DialogProc, reinterpret_cast<LPARAM>(&model));
        if (result != IDOK) return result == -1 ? 20 : 0;
    }
    // Explicit executable path + no shell; no PATH lookup or inherited handles.
    auto command = L"\"" + app + L"\"" + (args.empty() || args == L"--dependencies" ? L"" : L" " + args);
    const auto directory = app.substr(0, app.find_last_of(L"\\/"));
    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(app.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr, directory.c_str(), &startup, &process)) {
        MessageBoxW(nullptr, Languages[model.language].launchError, L"PyDeck", MB_OK | MB_ICONERROR);
        return 21;
    }
    CloseHandle(process.hThread);
    // Smoke tests need to observe the actual GUI process exit; normal launches return immediately.
    DWORD exitCode = 0;
    if (args.starts_with(L"--smoke-test ")) { WaitForSingleObject(process.hProcess, INFINITE); GetExitCodeProcess(process.hProcess, &exitCode); }
    CloseHandle(process.hProcess);
    return static_cast<int>(exitCode);
}
#endif
