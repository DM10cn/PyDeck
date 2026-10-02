// Offline setup orchestration. This executable imports only Windows system DLLs.
#include "Storage.h"
#include "Workflow.h"
#include "Payloads.h" // Generated from hash-pinned, Microsoft-signed runtime installers and our MSI.
#include "resource.h"
#include <commctrl.h>
#include <shellapi.h>
#include <atomic>
#include <thread>
#include <cstdio>

namespace {
using namespace pydeck::setup;
namespace prereq = pydeck::prerequisites;
constexpr UINT ProgressMessage = WM_APP + 1, FinishedMessage = WM_APP + 2;
struct Words {
    const wchar_t *intro, *skip, *needed, *note, *only, *begin, *cancel, *close, *check, *logs,
        *installing, *verifying, *waiting, *success, *prepared, *failed, *cancelled, *restart, *unsupported,
        *styleTitle, *styleHint, *materialStyle;
};
constexpr Words Text[] = {
    {L"Setup checks the required runtimes before opening the PyDeck MSI wizard",
     L"Ready - skip", L"Missing or incomplete - install",
     L"Continue installs only missing Microsoft runtimes from this offline package\nAdministrator approval may be requested\nShared runtimes remain installed when you remove PyDeck",
     L"Prepare dependencies only (for a separate MSIX installation)", L"Continue", L"Cancel", L"Close", L"Check again", L"Open logs",
     L"Installing: ", L"Verifying offline installers", L"Stopping after the current installer finishes",
     L"PyDeck installation completed", L"Dependencies are ready - you can now install the MSIX separately",
     L"Installation stopped - check the logs, resolve the issue, then try again",
     L"Cancelled - completed runtime installations have been kept",
     L"A restart is required - restart Windows, then run Setup again; Setup will not restart your PC",
     L"This build requires Windows 11 x64", L"Initial interface style",
     L"Choose the first-run appearance. You can change it in Settings. Existing app preferences are kept.",
     L"Material 3 Expressive (recommended)"},
    {L"先检查运行依赖，再进入 PyDeck MSI 安装向导",
     L"已满足，将跳过", L"缺失或不完整，需要安装",
     L"继续后仅安装缺少的微软运行时，安装程序已包含在此离线包中\n可能需要管理员确认\n卸载 PyDeck 时会保留共享运行时",
     L"仅准备依赖（用于另行安装 MSIX）", L"继续", L"取消", L"关闭", L"重新检查", L"打开日志",
     L"正在安装：", L"正在校验离线安装程序", L"等待当前安装程序结束后停止",
     L"PyDeck 安装完成", L"依赖已就绪，可另行安装 MSIX",
     L"安装已停止，请查看日志，处理后重试", L"已取消，已完成的运行时安装会保留",
     L"需要重启 Windows，重启后再次运行 Setup；不会自动重启电脑", L"此版本需要 Windows 11 x64",
     L"首次使用的界面风格", L"选择首次打开时的外观，之后可在设置中切换。已有应用偏好会保留。",
     L"Material 3 Expressive（推荐）"},
    {L"先檢查執行環境，再開啟 PyDeck MSI 安裝精靈",
     L"已具備，將略過", L"缺少或不完整，需要安裝",
     L"繼續後僅安裝缺少的 Microsoft 執行階段，安裝程式已包含在此離線套件中\n可能需要系統管理員確認\n解除安裝 PyDeck 時會保留共用執行階段",
     L"僅準備相依套件（供另外安裝 MSIX）", L"繼續", L"取消", L"關閉", L"重新檢查", L"開啟記錄",
     L"正在安裝：", L"正在驗證離線安裝程式", L"等待目前的安裝程式結束後停止",
     L"PyDeck 安裝完成", L"相依套件已就緒，可另外安裝 MSIX",
     L"安裝已停止，請查看記錄並處理後重試", L"已取消，已完成的執行階段安裝會保留",
     L"需要重新啟動 Windows，之後再次執行 Setup；不會自動重新啟動電腦", L"此版本需要 Windows 11 x64",
     L"首次使用的介面風格", L"選擇首次開啟時的外觀，之後可在設定中切換。既有應用程式偏好會保留。",
     L"Material 3 Expressive（建議）"},
    {L"ランタイムを確認してから PyDeck の MSI セットアップを開きます",
     L"導入済み - スキップ", L"未導入または不完全 - インストール",
     L"不足する Microsoft ランタイムのみ、このオフラインパッケージから導入します\n管理者の承認が必要になる場合があります\nPyDeck を削除しても共有ランタイムは残ります",
     L"ランタイムのみ準備する（MSIX を別途インストール）", L"続行", L"キャンセル", L"閉じる", L"再確認", L"ログを開く",
     L"インストール中: ", L"オフラインインストーラーを検証中", L"現在のインストーラーが終了したら停止します",
     L"PyDeck のインストールが完了しました", L"準備ができました - MSIX を別途インストールできます",
     L"インストールを停止しました - ログを確認してから再試行してください", L"キャンセルしました - 導入済みのランタイムは残ります",
     L"Windows の再起動後に Setup を再実行してください。自動では再起動しません", L"このビルドには Windows 11 x64 が必要です",
     L"初回起動時の画面スタイル", L"後から設定で変更できます。既存のアプリ設定は維持されます。",
     L"Material 3 Expressive（推奨）"}
};
enum class InterfaceStyle { Material, Fluent };
const wchar_t* StyleName(InterfaceStyle style) {
    return style == InterfaceStyle::Fluent ? L"Fluent" : L"Material";
}
InterfaceStyle InstalledStyle() {
    wchar_t value[32]{};
    DWORD size = sizeof(value);
    if (RegGetValueW(HKEY_CURRENT_USER, L"Software\\DM10cn\\PyDeck\\Installer", L"InterfaceStyle",
                    RRF_RT_REG_SZ, nullptr, value, &size) == ERROR_SUCCESS && std::wstring(value) == L"Fluent")
        return InterfaceStyle::Fluent;
    return InterfaceStyle::Material;
}
std::wstring MsiArguments(const std::wstring& path, const std::wstring& directory, InterfaceStyle style) {
    return L"/i \"" + path + L"\" /L*V \"" + directory + L"\\msi.log\" REBOOT=ReallySuppress PYDECKSTYLE=" + StyleName(style);
}
Snapshot Inspect() {
    const auto s = prereq::Inspect();
    return {s.windows, {s.net, s.runtime, s.vc}};
}
struct Model {
    unsigned language = 0;
    bool active = false, complete = false, dependenciesOnly = false;
    InterfaceStyle interfaceStyle = InterfaceStyle::Material;
    std::atomic<bool> stop{false};
    std::thread worker;
    std::wstring directory;
    Result result{Outcome::Cancelled, Package::App, 1602};
};
void Render(HWND dialog, const Model& model, Snapshot state = Inspect()) {
    const auto& words = Text[model.language];
    SetWindowTextW(dialog, L"PyDeck Setup");
    SetDlgItemTextW(dialog, IDC_INTRO, words.intro);
    SetDlgItemTextW(dialog, IDC_NOTE, words.note);
    SetDlgItemTextW(dialog, IDC_ONLY, words.only);
    SetDlgItemTextW(dialog, IDC_STYLE_LABEL, words.styleTitle);
    SetDlgItemTextW(dialog, IDC_STYLE_HINT, words.styleHint);
    SendDlgItemMessageW(dialog, IDC_STYLE, CB_RESETCONTENT, 0, 0);
    SendDlgItemMessageW(dialog, IDC_STYLE, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(words.materialStyle));
    SendDlgItemMessageW(dialog, IDC_STYLE, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"Windows Fluent"));
    SendDlgItemMessageW(dialog, IDC_STYLE, CB_SETCURSEL, static_cast<WPARAM>(model.interfaceStyle), 0);
    SetDlgItemTextW(dialog, IDC_RECHECK, words.check);
    SetDlgItemTextW(dialog, IDC_LOGS, words.logs);
    SetDlgItemTextW(dialog, IDOK, words.begin);
    SetDlgItemTextW(dialog, IDCANCEL, model.complete ? words.close : words.cancel);
    const int rows[]{IDC_NET, IDC_RUNTIME, IDC_VC};
    constexpr const wchar_t* msixProvided[]{L"Handled by MSIX", L"由 MSIX 处理", L"由 MSIX 處理", L"MSIX が処理"};
    for (unsigned i = 0; i < 3; ++i) {
        const auto line = std::wstring(Payloads[i].label) + L"  -  " +
            (i == 2 && model.dependenciesOnly ? msixProvided[model.language] : state.ready[i] ? words.skip : words.needed);
        SetDlgItemTextW(dialog, rows[i], line.c_str());
    }
    EnableWindow(GetDlgItem(dialog, IDOK), !model.active && !model.complete && state.supported);
    EnableWindow(GetDlgItem(dialog, IDC_RECHECK), !model.active);
    EnableWindow(GetDlgItem(dialog, IDC_LANGUAGE), !model.active);
    EnableWindow(GetDlgItem(dialog, IDC_ONLY), !model.active && !model.complete);
    EnableWindow(GetDlgItem(dialog, IDC_STYLE), !model.active && !model.complete && !model.dependenciesOnly);
    EnableWindow(GetDlgItem(dialog, IDC_LOGS), !model.active && !model.directory.empty());
    if (!state.supported) SetDlgItemTextW(dialog, IDC_STATUS, words.unsupported);
}
void Log(HANDLE file, const std::wstring& value) {
    SYSTEMTIME now{}; GetSystemTime(&now);
    wchar_t prefix[40]{};
    swprintf_s(prefix, L"%04u-%02u-%02uT%02u:%02u:%02uZ ", now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond);
    WriteUtf8(file, prefix + value + L"\r\n");
    FlushFileBuffers(file);
}
DWORD StartInstaller(HWND owner, const std::wstring& executable, const std::wstring& arguments,
                     bool elevated, bool visible, const std::wstring& directory) {
    SHELLEXECUTEINFOW launch{sizeof(launch)};
    launch.fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI;
    launch.hwnd = owner;
    launch.lpVerb = elevated ? L"runas" : L"open";
    launch.lpFile = executable.c_str();
    launch.lpParameters = arguments.c_str();
    launch.lpDirectory = directory.c_str();
    launch.nShow = visible ? SW_SHOWNORMAL : SW_HIDE;
    if (!ShellExecuteExW(&launch)) return GetLastError();
    Handle process(launch.hProcess);
    if (!process.value) return ERROR_INVALID_HANDLE;
    if (WaitForSingleObject(process.value, INFINITE) != WAIT_OBJECT_0) return GetLastError();
    DWORD code = ERROR_GEN_FAILURE;
    if (!GetExitCodeProcess(process.value, &code)) return GetLastError();
    return code;
}
const wchar_t* OutcomeName(Outcome outcome) {
    switch (outcome) {
    case Outcome::Success: return L"success";
    case Outcome::Cancelled: return L"cancelled";
    case Outcome::Restart: return L"restart-required";
    case Outcome::Unsupported: return L"unsupported";
    default: return L"failed";
    }
}
void SaveResult(const Model& model, bool verificationOnly) {
    const auto state = Inspect();
    Handle file(CreateFileW((model.directory + L"\\result.json").c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr));
    const auto boolean = [](bool value) { return value ? L"true" : L"false"; };
    auto json = std::wstring(L"{\"schemaVersion\":1,\"setupVersion\":\"") + SetupVersion + L"\",\"outcome\":\"" + OutcomeName(model.result.outcome) +
        L"\",\"package\":" + std::to_wstring(static_cast<unsigned>(model.result.package)) + L",\"exitCode\":" + std::to_wstring(model.result.code) +
        L",\"verificationOnly\":" + boolean(verificationOnly) + L",\"dependenciesOnly\":" + boolean(model.dependenciesOnly) +
        L",\"interfaceStyle\":\"" + StyleName(model.interfaceStyle) + L"\"" +
        L",\"windows\":" + boolean(state.supported) + L",\"net\":" + boolean(state.ready[0]) + L",\"windowsAppRuntime\":" + boolean(state.ready[1]) +
        L",\"visualCpp\":" + boolean(state.ready[2]) + L",\"payloads\":[";
    for (size_t i = 0; i < Payloads.size(); ++i) {
        if (i) json += L",";
        json += std::wstring(L"{\"file\":\"") + Payloads[i].file + L"\",\"sha256\":\"" + Payloads[i].sha256 + L"\"}";
    }
    json += L"]}\r\n";
    WriteUtf8(file.value, json);
}
void Work(HWND dialog, Model& model, bool verificationOnly = false) {
    const auto com = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    std::vector<std::wstring> extracted;
    Handle log;
    try {
        model.directory = PrivateDirectory();
        log.value = CreateFileW((model.directory + L"\\setup.log").c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (log.value == INVALID_HANDLE_VALUE) throw std::runtime_error("Cannot create setup log");
        Log(log.value, std::wstring(L"PyDeck Setup ") + SetupVersion + L"; no installers run before Continue; dependenciesOnly=" + (model.dependenciesOnly ? L"true" : L"false"));
        if (verificationOnly) {
            for (const auto& payload : Payloads) {
                const auto path = model.directory + L"\\" + payload.file;
                extracted.push_back(path);
                const auto locked = Extract(payload.id, path, payload.sha256);
                Log(log.value, std::wstring(L"VERIFY ") + payload.label + L" SHA256=" + payload.sha256);
            }
            model.result = {Outcome::Success, Package::App, 0};
        } else {
            Operations operations{Inspect, [&] { return model.stop.load(); },
                [&](Package package) -> unsigned long {
                    const auto index = static_cast<unsigned>(package);
                    const auto& payload = Payloads[index];
                    const auto path = model.directory + L"\\" + payload.file;
                    extracted.push_back(path);
                    const auto locked = Extract(payload.id, path, payload.sha256);
                    if (model.stop.load()) return ERROR_INSTALL_USEREXIT;
                    Log(log.value, std::wstring(L"VERIFIED ") + payload.label + L" SHA256=" + payload.sha256);
                    DWORD code = 0;
                    if (package == Package::App) {
                        std::array<wchar_t, MAX_PATH> system{};
                        GetSystemDirectoryW(system.data(), static_cast<UINT>(system.size()));
                        code = StartInstaller(dialog, std::wstring(system.data()) + L"\\msiexec.exe",
                            MsiArguments(path, model.directory, model.interfaceStyle), false, true, model.directory);
                    } else {
                        const auto arguments = package == Package::AppRuntime ? std::wstring(L"--quiet") :
                            L"/install /quiet /norestart /log \"" + model.directory + L"\\" + (package == Package::Net ? L"dotnet.log" : L"vc.log") + L"\"";
                        // App Runtime registration must run as the desktop user, even if UAC uses another administrator.
                        code = StartInstaller(dialog, path, arguments, package != Package::AppRuntime, false, model.directory);
                    }
                    Log(log.value, std::wstring(L"EXIT ") + payload.label + L" code=" + std::to_wstring(code));
                    return code;
                }, [&](Package package, bool install) {
                    Log(log.value, std::wstring(install ? L"INSTALL " : L"SKIP ") + Payloads[static_cast<unsigned>(package)].label);
                    if (dialog) PostMessageW(dialog, ProgressMessage, static_cast<WPARAM>(package), install);
                }};
            model.result = Run(operations, model.dependenciesOnly);
        }
        Log(log.value, std::wstring(L"RESULT ") + OutcomeName(model.result.outcome) + L" code=" + std::to_wstring(model.result.code));
    } catch (const std::exception& error) {
        model.result = {Outcome::Failed, Package::App, ERROR_INVALID_DATA};
        try { Log(log.value, L"ERROR " + std::wstring(error.what(), error.what() + strlen(error.what()))); } catch (...) {}
    }
    // Delete only exact files extracted by this run, after child exit and handle release; keep all logs.
    for (const auto& path : extracted) {
        if (!DeleteFileW(path.c_str()) && GetLastError() != ERROR_FILE_NOT_FOUND) {
            try { Log(log.value, L"NOTICE installer cleanup deferred; remove this log folder manually after review"); } catch (...) {}
        }
    }
    if (!model.directory.empty()) {
        try { SaveResult(model, verificationOnly); } catch (...) { model.result = {Outcome::Failed, Package::App, ERROR_WRITE_FAULT}; }
    }
    if (SUCCEEDED(com)) CoUninitialize();
    if (dialog) PostMessageW(dialog, FinishedMessage, 0, 0);
}
void Finished(HWND dialog, Model& model) {
    if (model.worker.joinable()) model.worker.join();
    model.active = false;
    model.complete = model.result.outcome == Outcome::Success || model.result.outcome == Outcome::Restart;
    SendDlgItemMessageW(dialog, IDC_PROGRESS, PBM_SETMARQUEE, FALSE, 0);
    Render(dialog, model);
    const auto& words = Text[model.language];
    const wchar_t* message = words.failed;
    switch (model.result.outcome) {
    case Outcome::Success: message = model.dependenciesOnly ? words.prepared : words.success; break;
    case Outcome::Cancelled: message = words.cancelled; break;
    case Outcome::Restart: message = words.restart; break;
    case Outcome::Unsupported: message = words.unsupported; break;
    default: break;
    }
    auto status = std::wstring(message);
    if (model.result.outcome == Outcome::Failed) status += L" (" + std::to_wstring(model.result.code) + L")";
    SetDlgItemTextW(dialog, IDC_STATUS, status.c_str());
}
INT_PTR CALLBACK SetupDialog(HWND dialog, UINT message, WPARAM wParam, LPARAM lParam) {
    auto model = reinterpret_cast<Model*>(GetWindowLongPtrW(dialog, DWLP_USER));
    if (message == WM_INITDIALOG) {
        model = reinterpret_cast<Model*>(lParam);
        SetWindowLongPtrW(dialog, DWLP_USER, lParam);
        SendMessageW(dialog, WM_SETICON, ICON_BIG, reinterpret_cast<LPARAM>(LoadIconW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDI_SETUP))));
        for (const auto language : {L"English", L"简体中文", L"繁體中文（台灣）", L"日本語"}) SendDlgItemMessageW(dialog, IDC_LANGUAGE, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(language));
        SendDlgItemMessageW(dialog, IDC_LANGUAGE, CB_SETCURSEL, model->language, 0);
        CheckDlgButton(dialog, IDC_ONLY, model->dependenciesOnly ? BST_CHECKED : BST_UNCHECKED);
        Render(dialog, *model);
        return TRUE;
    }
    if (!model) return FALSE;
    if (message == FinishedMessage) { Finished(dialog, *model); return TRUE; }
    if (message == ProgressMessage) {
        Render(dialog, *model);
        const auto& words = Text[model->language];
        const auto status = model->stop.load() ? std::wstring(words.waiting) :
            (lParam ? std::wstring(words.installing) : std::wstring(words.skip) + L": ") + Payloads[wParam].label;
        SetDlgItemTextW(dialog, IDC_STATUS, status.c_str());
        return TRUE;
    }
    if (message == WM_CLOSE || (message == WM_COMMAND && LOWORD(wParam) == IDCANCEL)) {
        if (model->active) { model->stop.store(true); SetDlgItemTextW(dialog, IDC_STATUS, Text[model->language].waiting); }
        else EndDialog(dialog, static_cast<INT_PTR>(model->result.code));
        return TRUE;
    }
    if (message != WM_COMMAND) return FALSE;
    switch (LOWORD(wParam)) {
    case IDC_STYLE:
        if (!model->active && !model->complete && !model->dependenciesOnly && HIWORD(wParam) == CBN_SELCHANGE) {
            const auto selected = SendDlgItemMessageW(dialog, IDC_STYLE, CB_GETCURSEL, 0, 0);
            if (selected >= 0 && selected < 2) model->interfaceStyle = static_cast<InterfaceStyle>(selected);
        }
        return TRUE;
    case IDC_ONLY:
        if (!model->active && !model->complete) {
            model->dependenciesOnly = IsDlgButtonChecked(dialog, IDC_ONLY) == BST_CHECKED;
            Render(dialog, *model);
        }
        return TRUE;
    case IDC_LANGUAGE:
        if (!model->active && HIWORD(wParam) == CBN_SELCHANGE) {
            const auto selected = SendDlgItemMessageW(dialog, IDC_LANGUAGE, CB_GETCURSEL, 0, 0);
            if (selected >= 0 && selected < 4) model->language = static_cast<unsigned>(selected);
            if (!model->directory.empty()) Finished(dialog, *model); else Render(dialog, *model);
        }
        return TRUE;
    case IDC_RECHECK: if (!model->active) Render(dialog, *model); return TRUE;
    case IDC_LOGS:
        if (!model->active && !model->directory.empty()) ShellExecuteW(dialog, L"open", model->directory.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
        return TRUE;
    case IDOK:
        if (!model->active && !model->complete && Inspect().supported) {
            model->dependenciesOnly = IsDlgButtonChecked(dialog, IDC_ONLY) == BST_CHECKED;
            model->stop.store(false); model->active = true;
            Render(dialog, *model);
            SetDlgItemTextW(dialog, IDC_STATUS, Text[model->language].verifying);
            SendDlgItemMessageW(dialog, IDC_PROGRESS, PBM_SETMARQUEE, TRUE, 35);
            try { model->worker = std::thread([dialog, model] { Work(dialog, *model); }); }
            catch (...) { model->result = {Outcome::Failed, Package::App, ERROR_NOT_ENOUGH_MEMORY}; Finished(dialog, *model); }
        }
        return TRUE;
    }
    return FALSE;
}
int Check() {
    const auto state = Inspect();
    const auto b = [](bool value) { return value ? "true" : "false"; };
    const auto result = std::string("{\"windows\":") + b(state.supported) + ",\"net\":" + b(state.ready[0]) +
        ",\"windowsAppRuntime\":" + b(state.ready[1]) + ",\"visualCpp\":" + b(state.ready[2]) + "}\r\n";
    DWORD written = 0;
    WriteFile(GetStdHandle(STD_OUTPUT_HANDLE), result.data(), static_cast<DWORD>(result.size()), &written, nullptr);
    return state.supported ? 0 : 1150;
}
} // namespace

#ifndef PYDECK_SETUP_TEST
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR commandLine, int) {
    SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32);
    SetDllDirectoryW(L"");
    const std::wstring args(commandLine);
    if (args == L"--check") return Check();
    Model model;
    model.interfaceStyle = InstalledStyle();
    if (args == L"--verify-payloads") {
        Work(nullptr, model, true);
        const auto text = model.directory + L"\\result.json\r\n";
        try { WriteUtf8(GetStdHandle(STD_OUTPUT_HANDLE), text); } catch (...) {}
        return static_cast<int>(model.result.code);
    }
    model.dependenciesOnly = args == L"--dependencies-only";
    if (!args.empty() && !model.dependenciesOnly) return ERROR_BAD_ARGUMENTS;
    // Never launch the per-user MSI under a different UAC identity. Elevate only individual machine runtimes.
    Handle token;
    TOKEN_ELEVATION elevation{}; DWORD size = sizeof(elevation);
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token.value) ||
        !GetTokenInformation(token.value, TokenElevation, &elevation, size, &size)) return ERROR_ACCESS_DENIED;
    if (elevation.TokenIsElevated) {
        MessageBoxW(nullptr, L"Open Setup normally, without Run as administrator. Setup requests elevation only for runtimes that need it.", L"PyDeck Setup", MB_OK | MB_ICONINFORMATION);
        return ERROR_ELEVATION_REQUIRED;
    }
    INITCOMMONCONTROLSEX controls{sizeof(controls), ICC_STANDARD_CLASSES | ICC_PROGRESS_CLASS};
    InitCommonControlsEx(&controls);
    const auto result = DialogBoxParamW(instance, MAKEINTRESOURCEW(IDD_SETUP), nullptr, SetupDialog, reinterpret_cast<LPARAM>(&model));
    return result == -1 ? static_cast<int>(GetLastError()) : static_cast<int>(result);
}
#endif
