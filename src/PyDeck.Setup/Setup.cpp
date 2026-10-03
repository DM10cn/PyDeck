// Offline setup orchestration. This executable imports only Windows system DLLs.
#include "Storage.h"
#include "Workflow.h"
#include "Payloads.h" // Generated from hash-pinned, Microsoft-signed runtime installers and our MSI.
#include "resource.h"
#include "SetupUi.h"
#include "InstallOptions.h"
#include "Preflight.h"
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
    {L"Install, repair or remove PyDeck for your Windows account",
     L"Ready - skip", L"Missing or incomplete - install",
     L"Missing Microsoft runtimes are included offline and may request administrator approval.\nUninstalling PyDeck keeps shared runtimes and your Python installations.",
     L"Prepare dependencies only (for a separate MSIX installation)", L"Install", L"Cancel", L"Close", L"Check again", L"Open logs",
     L"Installing: ", L"Verifying offline installers", L"Stopping after the current installer finishes",
     L"PyDeck installation completed", L"Dependencies are ready - you can now install the MSIX separately",
     L"Installation stopped - check the logs, resolve the issue, then try again",
     L"Cancelled - completed runtime installations have been kept",
     L"A restart is required - restart Windows, then run Setup again; Setup will not restart your PC",
     L"This build requires Windows 11 x64", L"Initial interface style",
     L"Choose the first-run appearance. You can change it in Settings. Existing app preferences are kept.",
     L"Material 3 Expressive (recommended)"},
    {L"为当前 Windows 用户安装、修复或卸载 PyDeck",
     L"已满足，将跳过", L"缺失或不完整，需要安装",
     L"缺少的微软运行时已包含在此离线包中，安装时可能需要管理员确认。\n卸载 PyDeck 会保留共享运行时与 Python 安装。",
     L"仅准备依赖（用于另行安装 MSIX）", L"安装", L"取消", L"关闭", L"重新检查", L"打开日志",
     L"正在安装：", L"正在校验离线安装程序", L"等待当前安装程序结束后停止",
     L"PyDeck 安装完成", L"依赖已就绪，可另行安装 MSIX",
     L"安装已停止，请查看日志，处理后重试", L"已取消，已完成的运行时安装会保留",
     L"需要重启 Windows，重启后再次运行 Setup；不会自动重启电脑", L"此版本需要 Windows 11 x64",
     L"首次使用的界面风格", L"选择首次打开时的外观，之后可在设置中切换。已有应用偏好会保留。",
     L"Material 3 Expressive（推荐）"},
    {L"為目前 Windows 使用者安裝、修復或解除安裝 PyDeck",
     L"已具備，將略過", L"缺少或不完整，需要安裝",
     L"缺少的 Microsoft 執行階段已包含在離線套件中，安裝時可能需要系統管理員確認。\n解除安裝 PyDeck 會保留共用執行階段與 Python。",
     L"僅準備相依套件（供另外安裝 MSIX）", L"安裝", L"取消", L"關閉", L"重新檢查", L"開啟記錄",
     L"正在安裝：", L"正在驗證離線安裝程式", L"等待目前的安裝程式結束後停止",
     L"PyDeck 安裝完成", L"相依套件已就緒，可另外安裝 MSIX",
     L"安裝已停止，請查看記錄並處理後重試", L"已取消，已完成的執行階段安裝會保留",
     L"需要重新啟動 Windows，之後再次執行 Setup；不會自動重新啟動電腦", L"此版本需要 Windows 11 x64",
     L"首次使用的介面風格", L"選擇首次開啟時的外觀，之後可在設定中切換。既有應用程式偏好會保留。",
     L"Material 3 Expressive（建議）"},
    {L"現在のユーザーに PyDeck をインストール、修復、削除します",
     L"導入済み - スキップ", L"未導入または不完全 - インストール",
     L"不足する Microsoft ランタイムをオフラインで導入します。管理者の承認が必要な場合があります。\nPyDeck を削除しても共有ランタイムと Python は残ります。",
     L"ランタイムのみ準備する（MSIX を別途インストール）", L"インストール", L"キャンセル", L"閉じる", L"再確認", L"ログを開く",
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
std::wstring MsiArguments(const std::wstring& path, const std::wstring& directory, InterfaceStyle style, const InstallOptions& options) {
    return BuildMsiArguments(path, directory, StyleName(style), options) + L" MSIRESTARTMANAGERCONTROL=DisableShutdown";
}
Snapshot Inspect() {
    const auto s = prereq::Inspect();
    return {s.windows, {s.net, s.runtime, s.vc}};
}
struct Model;
// Keep action planning independent of external execution, so UI callbacks can
// be exercised without installing software or changing the current account.
struct ActionServices {
    std::function<ProductState()> inspectProduct = [] { return InspectProduct(PayloadProductCode, PayloadProductVersion); };
    std::function<Snapshot()> inspectPrerequisites = Inspect;
    std::function<bool(const std::vector<std::wstring>&)> isRunning = IsPyDeckRunning;
    std::function<bool(HWND, const wchar_t*)> confirmRemove = [](HWND owner, const wchar_t* message) {
        return MessageBoxW(owner, message, L"PyDeck", MB_YESNO | MB_ICONQUESTION | MB_DEFBUTTON2) == IDYES;
    };
    std::function<bool(HWND, const std::wstring&, const std::wstring&)> openPath = [](HWND owner, const std::wstring& path, const std::wstring& directory) {
        return reinterpret_cast<INT_PTR>(ShellExecuteW(owner, L"open", path.c_str(), nullptr, directory.c_str(), SW_SHOWNORMAL)) > 32;
    };
    std::function<void(HWND, Model&)> startWork;
};
struct Model {
    unsigned language = SetupLanguage(GetUserDefaultUILanguage()), renderedLanguage = ~0u;
    bool active = false, complete = false, dependenciesOnly = false, removing = false, preview = false;
    InstallOptions options;
    ProductState product;
    ActionServices actions;
    Snapshot snapshot{false, {false, false, false}};
    HFONT headingFont = nullptr;
    InterfaceStyle interfaceStyle = InterfaceStyle::Material;
    std::atomic<bool> stop{false};
    std::thread worker;
    std::wstring directory;
    Result result{Outcome::Cancelled, Package::App, 1602};
};
void Render(HWND dialog, Model& model, std::optional<Snapshot> supplied = std::nullopt) {
    if (supplied) model.snapshot = *supplied;
    const auto state = model.snapshot;
    const auto& words = Text[model.language];
    const auto& ui = UiText[model.language];
    constexpr const wchar_t* retryRemove[]{L"Retry uninstall", L"重试卸载", L"重試解除安裝", L"削除を再試行"};
    const auto title = std::wstring(L"PyDeck Setup ") + SetupVersion + (model.preview ? L" [Preview]" : L"");
    SetWindowTextW(dialog, title.c_str());
    SetDlgItemTextW(dialog, IDC_HEADING, model.removing ? ui.removing : model.product.relation == InstallRelation::Repair ? ui.repair :
        model.product.relation == InstallRelation::Upgrade ? ui.upgrade : ui.title);
    SetDlgItemTextW(dialog, IDC_FOLDER_LABEL, ui.folder);
    SetDlgItemTextW(dialog, IDC_BROWSE, ui.browse);
    SetDlgItemTextW(dialog, IDC_DESKTOP, ui.desktop);
    SetDlgItemTextW(dialog, IDC_START_MENU, ui.startMenu);
    SetDlgItemTextW(dialog, IDC_DEPENDENCIES, ui.dependencies);
    SetDlgItemTextW(dialog, IDC_LICENSE, ui.license);
    SetDlgItemTextW(dialog, IDC_LAUNCH, ui.launch);
    SetDlgItemTextW(dialog, IDC_REMOVE, ui.remove);
    SetDlgItemTextW(dialog, IDC_INTRO, words.intro);
    SetDlgItemTextW(dialog, IDC_NOTE, words.note);
    SetDlgItemTextW(dialog, IDC_ONLY, words.only);
    CheckDlgButton(dialog, IDC_ONLY, model.dependenciesOnly ? BST_CHECKED : BST_UNCHECKED);
    SetDlgItemTextW(dialog, IDC_STYLE_LABEL, words.styleTitle);
    SetDlgItemTextW(dialog, IDC_STYLE_HINT, words.styleHint);
    if (model.renderedLanguage != model.language) {
        SendDlgItemMessageW(dialog, IDC_STYLE, CB_RESETCONTENT, 0, 0);
        SendDlgItemMessageW(dialog, IDC_STYLE, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(words.materialStyle));
        SendDlgItemMessageW(dialog, IDC_STYLE, CB_ADDSTRING, 0, reinterpret_cast<LPARAM>(L"Windows Fluent"));
        model.renderedLanguage = model.language;
    }
    SendDlgItemMessageW(dialog, IDC_STYLE, CB_SETCURSEL, static_cast<WPARAM>(model.interfaceStyle), 0);
    SetDlgItemTextW(dialog, IDC_RECHECK, words.check);
    SetDlgItemTextW(dialog, IDC_LOGS, words.logs);
    SetDlgItemTextW(dialog, IDOK, model.removing ? retryRemove[model.language] : !model.active && !model.directory.empty() && !model.complete ? ui.retry :
        model.product.relation == InstallRelation::Repair && !model.dependenciesOnly ? ui.repair :
        model.product.relation == InstallRelation::Upgrade && !model.dependenciesOnly ? ui.upgrade : words.begin);
    SetDlgItemTextW(dialog, IDCANCEL, model.complete ? words.close : words.cancel);
    const int rows[]{IDC_NET, IDC_RUNTIME, IDC_VC};
    constexpr const wchar_t* msixProvided[]{L"Handled by MSIX", L"由 MSIX 处理", L"由 MSIX 處理", L"MSIX が処理"};
    for (unsigned i = 0; i < 3; ++i) {
        const auto line = std::wstring(Payloads[i].label) + L"  -  " +
            (i == 2 && model.dependenciesOnly ? msixProvided[model.language] : state.ready[i] ? words.skip : words.needed);
        SetDlgItemTextW(dialog, rows[i], line.c_str());
    }
    const bool conflict = model.product.relation == InstallRelation::SameVersionConflict || model.product.relation == InstallRelation::NewerInstalled;
    const bool canEdit = !model.active && !model.complete;
    const bool launchReady = !model.active && model.complete && model.result.outcome == Outcome::Success && !model.dependenciesOnly && !model.removing;
    ShowWindow(GetDlgItem(dialog, IDC_LAUNCH), launchReady ? SW_SHOW : SW_HIDE);
    ShowWindow(GetDlgItem(dialog, IDOK), launchReady ? SW_HIDE : SW_SHOW);
    EnableWindow(GetDlgItem(dialog, IDC_LAUNCH), launchReady && !model.preview);
    EnableWindow(GetDlgItem(dialog, IDOK), canEdit && (model.removing || (state.supported && (!conflict || model.dependenciesOnly))) && !model.preview);
    EnableWindow(GetDlgItem(dialog, IDC_RECHECK), !model.active);
    EnableWindow(GetDlgItem(dialog, IDC_LANGUAGE), !model.active);
    EnableWindow(GetDlgItem(dialog, IDC_ONLY), canEdit && !model.removing);
    EnableWindow(GetDlgItem(dialog, IDC_STYLE), canEdit && !model.removing && !model.dependenciesOnly);
    EnableWindow(GetDlgItem(dialog, IDC_LOGS), !model.active && !model.directory.empty());
    for (const auto control : {IDC_FOLDER, IDC_BROWSE, IDC_DESKTOP, IDC_START_MENU})
        EnableWindow(GetDlgItem(dialog, control), canEdit && !model.removing && !model.dependenciesOnly && ((control != IDC_FOLDER && control != IDC_BROWSE) || model.product.relation != InstallRelation::Repair));
    ShowWindow(GetDlgItem(dialog, IDC_REMOVE), model.product.productCode.empty() ? SW_HIDE : SW_SHOW);
    EnableWindow(GetDlgItem(dialog, IDC_REMOVE), canEdit && !model.preview);
    EnableWindow(GetDlgItem(dialog, IDC_LICENSE), !model.active);
    // Completion uses Close as the default; launching the application remains
    // an explicit choice. Uninstall retries still require a confirmation.
    SendMessageW(dialog, DM_SETDEFID, model.complete ? IDCANCEL : IDOK, 0);
    constexpr const wchar_t* previewText[]{L"Preview only. Installation, removal and app launch are disabled.", L"仅预览安装界面，不执行安装、卸载或启动应用。", L"僅預覽安裝介面，不會安裝、解除安裝或啟動應用程式。", L"プレビューのみです。インストール、削除、起動は実行されません。"};
    if (!model.active && model.directory.empty()) SetDlgItemTextW(dialog, IDC_STATUS,
        model.preview ? previewText[model.language] : !state.supported ? words.unsupported : conflict && !model.dependenciesOnly ?
        (model.product.relation == InstallRelation::NewerInstalled ? ui.newerVersion : ui.sameVersion) : ui.ready);
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
    const auto state = model.removing ? model.snapshot : Inspect();
    Handle file(CreateFileW((model.directory + L"\\result.json").c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr));
    const auto boolean = [](bool value) { return value ? L"true" : L"false"; };
    auto json = std::wstring(L"{\"schemaVersion\":1,\"setupVersion\":\"") + SetupVersion + L"\",\"outcome\":\"" + OutcomeName(model.result.outcome) +
        L"\",\"package\":" + std::to_wstring(static_cast<unsigned>(model.result.package)) + L",\"exitCode\":" + std::to_wstring(model.result.code) +
        L",\"verificationOnly\":" + boolean(verificationOnly) + L",\"dependenciesOnly\":" + boolean(model.dependenciesOnly) +
        L",\"operation\":\"" + (model.removing ? L"uninstall" : L"install") + L"\",\"interfaceStyle\":\"" + StyleName(model.interfaceStyle) + L"\"" +
        L",\"windows\":" + boolean(state.supported) + L",\"net\":" + boolean(state.ready[0]) + L",\"windowsAppRuntime\":" + boolean(state.ready[1]) +
        L",\"visualCpp\":" + boolean(state.ready[2]) + L",\"payloads\":[";
    for (size_t i = 0; !model.removing && i < Payloads.size(); ++i) {
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
        Log(log.value, std::wstring(L"PyDeck Setup ") + SetupVersion + L"; operation=" + (model.removing ? L"uninstall" : L"install") + L"; dependenciesOnly=" + (model.dependenciesOnly ? L"true" : L"false"));
        if (verificationOnly) {
            for (const auto& payload : Payloads) {
                const auto path = model.directory + L"\\" + payload.file;
                extracted.push_back(path);
                const auto locked = Extract(payload.id, path, payload.sha256);
                Log(log.value, std::wstring(L"VERIFY ") + payload.label + L" SHA256=" + payload.sha256);
            }
            model.result = {Outcome::Success, Package::App, 0};
        } else if (model.removing) {
            // Windows uses its cached MSI. Do not extract the bundle or inspect
            // runtime dependencies just to remove an installed application.
            DWORD code = ERROR_INSTALL_USEREXIT;
            if (!model.stop.load()) {
                std::array<wchar_t, MAX_PATH> system{};
                GetSystemDirectoryW(system.data(), static_cast<UINT>(system.size()));
                Log(log.value, L"UNINSTALL " + model.product.productCode);
                code = IsPyDeckRunning({model.product.folder}) ? ERROR_SHARING_VIOLATION : StartInstaller(dialog, std::wstring(system.data()) + L"\\msiexec.exe",
                    L"/x " + model.product.productCode + L" /qn /norestart /L*V \"" + model.directory +
                    L"\\msi.log\" REBOOT=ReallySuppress MSIRESTARTMANAGERCONTROL=DisableShutdown", false, false, model.directory);
            }
            const auto outcome = code == 3010 || code == 1641 ? Outcome::Restart : code == 1602 || code == 1223 ? Outcome::Cancelled :
                code ? Outcome::Failed : Outcome::Success;
            model.result = {outcome, Package::App, code};
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
                        if (IsPyDeckRunning({model.options.folder, model.product.folder})) return ERROR_SHARING_VIOLATION;
                        std::array<wchar_t, MAX_PATH> system{};
                        GetSystemDirectoryW(system.data(), static_cast<UINT>(system.size()));
                        auto arguments = MsiArguments(path, model.directory, model.interfaceStyle, model.options);
                        if (model.product.relation == InstallRelation::Repair) arguments += L" REINSTALL=ALL REINSTALLMODE=omus";
                        code = StartInstaller(dialog, std::wstring(system.data()) + L"\\msiexec.exe",
                            arguments, false, false, model.directory);
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
    if (!model.removing) model.snapshot = model.actions.inspectPrerequisites();
    Render(dialog, model);
    const auto& words = Text[model.language];
    const auto& ui = UiText[model.language];
    const wchar_t* message = words.failed;
    switch (model.result.outcome) {
    case Outcome::Success: message = model.removing ? ui.removed : model.dependenciesOnly ? words.prepared : words.success; break;
    case Outcome::Cancelled: message = words.cancelled; break;
    case Outcome::Restart: message = words.restart; break;
    case Outcome::Unsupported: message = words.unsupported; break;
    default: message = model.result.code == ERROR_SHARING_VIOLATION ? ui.running : model.result.code == 1618 ? ui.installBusy : model.result.code == 1638 ? ui.sameVersion : ui.installFailed; break;
    }
    auto status = std::wstring(message);
    if (model.result.outcome == Outcome::Failed) status += L" (" + std::to_wstring(model.result.code) + L")";
    SetDlgItemTextW(dialog, IDC_STATUS, status.c_str());
}
std::wstring ControlText(HWND dialog, int id) {
    const auto control = GetDlgItem(dialog, id);
    std::wstring value(static_cast<size_t>(GetWindowTextLengthW(control)) + 1, L'\0');
    value.resize(GetWindowTextW(control, value.data(), static_cast<int>(value.size())));
    return value;
}
INT_PTR CALLBACK LicenseDialog(HWND dialog, UINT message, WPARAM wParam, LPARAM) {
    if (message == WM_INITDIALOG) {
        const auto resource = FindResourceW(nullptr, MAKEINTRESOURCEW(IDR_LICENSE), RT_RCDATA);
        const auto loaded = LoadResource(nullptr, resource);
        const auto data = static_cast<const char*>(LockResource(loaded));
        const auto size = static_cast<int>(SizeofResource(nullptr, resource));
        const auto length = data ? MultiByteToWideChar(CP_UTF8, 0, data, size, nullptr, 0) : 0;
        std::wstring text(length, L'\0');
        if (length) MultiByteToWideChar(CP_UTF8, 0, data, size, text.data(), length);
        std::wstring lines;
        for (const auto character : text) { if (character == L'\n') lines += L'\r'; if (character != L'\r') lines += character; }
        SetDlgItemTextW(dialog, IDC_LICENSE_TEXT, lines.c_str());
        SetFocus(GetDlgItem(dialog, IDOK));
        return FALSE;
    }
    if (message == WM_CLOSE || (message == WM_COMMAND && (LOWORD(wParam) == IDOK || LOWORD(wParam) == IDCANCEL))) {
        EndDialog(dialog, 0); return TRUE;
    }
    return FALSE;
}
void BeginAction(HWND dialog, Model& model, bool removing) {
    if (model.active || model.complete || model.preview) return;
    const auto& ui = UiText[model.language];
    model.product = model.actions.inspectProduct();
    const bool dependenciesOnly = !removing && IsDlgButtonChecked(dialog, IDC_ONLY) == BST_CHECKED;
    if (removing && model.product.productCode.empty()) {
        model.removing = true;
        model.result = {Outcome::Success, Package::App, ERROR_SUCCESS};
        Finished(dialog, model); return;
    }
    if (!removing) {
        model.snapshot = model.actions.inspectPrerequisites();
        if (!model.snapshot.supported) { Render(dialog, model); return; }
        if (!dependenciesOnly) {
            if (model.product.relation == InstallRelation::SameVersionConflict || model.product.relation == InstallRelation::NewerInstalled) {
                Render(dialog, model); return;
            }
            try {
                model.options.folder = ValidateInstallFolder(model.product.relation == InstallRelation::Repair && !model.product.folder.empty() ? model.product.folder : ControlText(dialog, IDC_FOLDER));
                SetDlgItemTextW(dialog, IDC_FOLDER, model.options.folder.c_str());
            }
            catch (const std::exception&) { SetDlgItemTextW(dialog, IDC_STATUS, ui.invalidFolder); SetFocus(GetDlgItem(dialog, IDC_FOLDER)); return; }
            model.options.desktop = IsDlgButtonChecked(dialog, IDC_DESKTOP) == BST_CHECKED;
            model.options.startMenu = IsDlgButtonChecked(dialog, IDC_START_MENU) == BST_CHECKED;
        }
    }
    if ((removing || !dependenciesOnly) && model.actions.isRunning({model.options.folder, model.product.folder})) {
        SetDlgItemTextW(dialog, IDC_STATUS, ui.running); return;
    }
    if (removing && !model.actions.confirmRemove(dialog, ui.confirmRemove)) return;
    model.dependenciesOnly = dependenciesOnly;
    model.removing = removing;
    model.stop.store(false); model.active = true;
    Render(dialog, model);
    SetDlgItemTextW(dialog, IDC_STATUS, removing ? ui.removing : Text[model.language].verifying);
    SendDlgItemMessageW(dialog, IDC_PROGRESS, PBM_SETMARQUEE, TRUE, 35);
    try {
        if (model.actions.startWork) model.actions.startWork(dialog, model);
        else model.worker = std::thread([dialog, &model] { Work(dialog, model); });
    }
    catch (...) { model.result = {Outcome::Failed, Package::App, ERROR_NOT_ENOUGH_MEMORY}; Finished(dialog, model); }
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
        try {
            model->options = LoadInstallOptions();
            model->snapshot = model->actions.inspectPrerequisites();
            model->product = model->actions.inspectProduct();
        } catch (...) {
            MessageBoxW(dialog, UiText[model->language].installFailed, L"PyDeck Setup", MB_OK | MB_ICONERROR);
            EndDialog(dialog, ERROR_INVALID_DATA); return TRUE;
        }
        if (model->product.relation == InstallRelation::Repair && !model->product.folder.empty()) model->options.folder = model->product.folder;
        SetDlgItemTextW(dialog, IDC_FOLDER, model->options.folder.c_str());
        CheckDlgButton(dialog, IDC_DESKTOP, model->options.desktop ? BST_CHECKED : BST_UNCHECKED);
        CheckDlgButton(dialog, IDC_START_MENU, model->options.startMenu ? BST_CHECKED : BST_UNCHECKED);
        LOGFONTW heading{};
        if (GetObjectW(reinterpret_cast<HFONT>(SendMessageW(dialog, WM_GETFONT, 0, 0)), sizeof(heading), &heading)) {
            heading.lfHeight = MulDiv(heading.lfHeight, 3, 2); heading.lfWeight = FW_SEMIBOLD;
            model->headingFont = CreateFontIndirectW(&heading);
            if (model->headingFont) SendDlgItemMessageW(dialog, IDC_HEADING, WM_SETFONT, reinterpret_cast<WPARAM>(model->headingFont), TRUE);
        }
        Render(dialog, *model);
        return TRUE;
    }
    if (!model) return FALSE;
    if (message == WM_DESTROY) { if (model->headingFont) { DeleteObject(model->headingFont); model->headingFont = nullptr; } return TRUE; }
    if (message == FinishedMessage) { Finished(dialog, *model); return TRUE; }
    if (message == ProgressMessage) {
        model->snapshot = Inspect();
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
    try { switch (LOWORD(wParam)) {
    case IDC_BROWSE:
        if (!model->active && !model->complete && !model->removing && !model->dependenciesOnly && model->product.relation != InstallRelation::Repair)
            if (const auto folder = BrowseInstallFolder(dialog, ControlText(dialog, IDC_FOLDER))) SetDlgItemTextW(dialog, IDC_FOLDER, folder->c_str());
        return TRUE;
    case IDC_LICENSE:
        if (!model->active) DialogBoxW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_LICENSE), dialog, LicenseDialog);
        return TRUE;
    case IDC_REMOVE: BeginAction(dialog, *model, true); return TRUE;
    case IDC_LAUNCH:
        if (!model->active && model->complete && model->result.outcome == Outcome::Success && !model->dependenciesOnly && !model->removing && !model->preview) {
            const auto executable = model->options.folder + L"\\PyDeck.Launcher.exe";
            if (!model->actions.openPath(dialog, executable, model->options.folder))
                SetDlgItemTextW(dialog, IDC_STATUS, UiText[model->language].launchFailed);
            else EndDialog(dialog, 0);
        }
        return TRUE;
    case IDC_STYLE:
        if (!model->active && !model->complete && !model->removing && !model->dependenciesOnly && HIWORD(wParam) == CBN_SELCHANGE) {
            const auto selected = SendDlgItemMessageW(dialog, IDC_STYLE, CB_GETCURSEL, 0, 0);
            if (selected >= 0 && selected < 2) model->interfaceStyle = static_cast<InterfaceStyle>(selected);
        }
        return TRUE;
    case IDC_ONLY:
        if (!model->active && !model->complete && !model->removing) {
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
    case IDC_RECHECK:
        if (!model->active) {
            model->snapshot = model->actions.inspectPrerequisites(); model->product = model->actions.inspectProduct();
            if (model->removing && model->product.productCode.empty()) {
                model->result = {Outcome::Success, Package::App, ERROR_SUCCESS};
                Finished(dialog, *model); return TRUE;
            }
            if (model->product.relation == InstallRelation::Repair && !model->product.folder.empty()) {
                model->options.folder = model->product.folder;
                SetDlgItemTextW(dialog, IDC_FOLDER, model->options.folder.c_str());
            }
            Render(dialog, *model);
        }
        return TRUE;
    case IDC_LOGS:
        if (!model->active && !model->directory.empty()) model->actions.openPath(dialog, model->directory, model->directory);
        return TRUE;
    case IDOK: BeginAction(dialog, *model, model->removing); return TRUE;
    } } catch (const std::exception&) { SetDlgItemTextW(dialog, IDC_STATUS, UiText[model->language].installFailed); }
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
    model.preview = args == L"--preview";
    if (!args.empty() && !model.dependenciesOnly && !model.preview) return ERROR_BAD_ARGUMENTS;
    // Never launch the per-user MSI under a different UAC identity. Elevate only individual machine runtimes.
    Handle token;
    TOKEN_ELEVATION elevation{}; DWORD size = sizeof(elevation);
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token.value) ||
        !GetTokenInformation(token.value, TokenElevation, &elevation, size, &size)) return ERROR_ACCESS_DENIED;
    if (elevation.TokenIsElevated && !model.preview) {
        MessageBoxW(nullptr, L"Open Setup normally, without Run as administrator. Setup requests elevation only for runtimes that need it.", L"PyDeck Setup", MB_OK | MB_ICONINFORMATION);
        return ERROR_ELEVATION_REQUIRED;
    }
    INITCOMMONCONTROLSEX controls{sizeof(controls), ICC_STANDARD_CLASSES | ICC_PROGRESS_CLASS};
    InitCommonControlsEx(&controls);
    const auto com = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
    const auto result = DialogBoxParamW(instance, MAKEINTRESOURCEW(IDD_SETUP), nullptr, SetupDialog, reinterpret_cast<LPARAM>(&model));
    const auto dialogError = result == -1 ? GetLastError() : 0;
    if (SUCCEEDED(com)) CoUninitialize();
    return result == -1 ? static_cast<int>(dialogError) : static_cast<int>(result);
}
#endif
