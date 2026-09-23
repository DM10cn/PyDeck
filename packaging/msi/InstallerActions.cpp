// Native MSI UI actions: /MT keeps setup independent of installed .NET/VC runtimes.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <msiquery.h>
#include <shobjidl.h>
#include <string>
#include <vector>
#include <thread>

namespace {
std::wstring Property(MSIHANDLE install, const wchar_t* name) {
    DWORD count = 0;
    wchar_t empty = L'\0';
    const UINT first = MsiGetPropertyW(install, name, &empty, &count);
    if (first != ERROR_SUCCESS && first != ERROR_MORE_DATA) return {};
    std::vector<wchar_t> value(static_cast<size_t>(count) + 1);
    ++count;
    if (MsiGetPropertyW(install, name, value.data(), &count) != ERROR_SUCCESS) return {};
    return std::wstring(value.data(), count);
}
UINT Set(MSIHANDLE install, const wchar_t* name, const std::wstring& value) {
    return MsiSetPropertyW(install, name, value.c_str());
}
void ShowError(MSIHANDLE install, const wchar_t* text) {
    const MSIHANDLE record = MsiCreateRecord(0);
    if (!record) return;
    MsiRecordSetStringW(record, 0, text);
    MsiProcessMessage(install, static_cast<INSTALLMESSAGE>(INSTALLMESSAGE_USER | MB_OK | MB_ICONERROR), record);
    MsiCloseHandle(record);
}
struct FolderChoice { HWND owner; std::wstring initial, path; HRESULT result = E_FAIL; };
void ChooseFolder(FolderChoice& choice) {
    const HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
    if (FAILED(initialized)) { choice.result = initialized; return; }
    IFileOpenDialog* dialog = nullptr;
    choice.result = CoCreateInstance(CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&dialog));
    if (SUCCEEDED(choice.result)) {
        FILEOPENDIALOGOPTIONS options{};
        choice.result = dialog->GetOptions(&options);
        if (SUCCEEDED(choice.result)) choice.result = dialog->SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR | FOS_DONTADDTORECENT);
        dialog->SetTitle(L"Choose the installation folder for PyDeck");
        dialog->SetOkButtonLabel(L"Select folder");
        IShellItem* initial = nullptr;
        if (!choice.initial.empty() && SUCCEEDED(SHCreateItemFromParsingName(choice.initial.c_str(), nullptr, IID_PPV_ARGS(&initial)))) {
            dialog->SetFolder(initial);
            initial->Release();
        }
        if (SUCCEEDED(choice.result)) choice.result = dialog->Show(choice.owner);
        if (SUCCEEDED(choice.result)) {
            IShellItem* selected = nullptr;
            choice.result = dialog->GetResult(&selected);
            if (SUCCEEDED(choice.result)) {
                PWSTR path = nullptr;
                choice.result = selected->GetDisplayName(SIGDN_FILESYSPATH, &path);
                if (SUCCEEDED(choice.result)) {
                    try { choice.path = path; } catch (...) { choice.result = E_OUTOFMEMORY; }
                    CoTaskMemFree(path);
                }
                selected->Release();
            }
        }
        dialog->Release();
    }
    CoUninitialize();
}
}

extern "C" __declspec(dllexport) UINT __stdcall InitializeOptions(MSIHANDLE install) {
    try {
        // UI selections must survive the subsequent execute-sequence AppSearch.
        if (Property(install, L"PYDECK_OPTIONS_INITIALIZED") == L"1") return ERROR_SUCCESS;
        if (Property(install, L"INSTALLFOLDER").empty()) {
            const auto previous = Property(install, L"PREVIOUS_INSTALL_FOLDER");
            if (!previous.empty() && Set(install, L"INSTALLFOLDER", previous) != ERROR_SUCCESS) return ERROR_INSTALL_FAILURE;
        }
        struct Option { const wchar_t *name, *previous, *fallback; };
        for (const auto& option : {Option{L"DESKTOPSHORTCUT", L"PREVIOUS_DESKTOP_SHORTCUT", L"0"},
                                  Option{L"STARTMENUSHORTCUT", L"PREVIOUS_START_MENU_SHORTCUT", L"1"}}) {
            auto value = Property(install, option.name);
            if (value.empty()) value = Property(install, option.previous);
            if (value.empty()) value = option.fallback;
            if (value != L"0" && value != L"1") {
                ShowError(install, L"Shortcut options must be 0 (off) or 1 (on).");
                return ERROR_INSTALL_FAILURE;
            }
            // An unchecked MSI CheckBox is an empty property, not the string "0".
            if (Set(install, option.name, value == L"1" ? L"1" : L"") != ERROR_SUCCESS) return ERROR_INSTALL_FAILURE;
        }
        return Set(install, L"PYDECK_OPTIONS_INITIALIZED", L"1");
    } catch (...) { return ERROR_INSTALL_FAILURE; }
}

extern "C" __declspec(dllexport) UINT __stdcall BrowseInstallFolder(MSIHANDLE install) {
    try {
        HWND owner = GetActiveWindow();
        if (!owner) {
            owner = GetForegroundWindow();
            DWORD process = 0;
            GetWindowThreadProcessId(owner, &process);
            if (process != GetCurrentProcessId()) owner = nullptr;
        }
        FolderChoice choice{owner, Property(install, L"INSTALLFOLDER"), L""};
        // Shell folder dialogs need STA. MSI's invoking thread is not guaranteed to be STA.
        std::thread worker([&] { ChooseFolder(choice); });
        const HANDLE handle = worker.native_handle();
        while (MsgWaitForMultipleObjectsEx(1, &handle, INFINITE, QS_ALLINPUT, MWMO_INPUTAVAILABLE) == WAIT_OBJECT_0 + 1) {
            MSG message{};
            while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
        }
        worker.join();
        if (choice.result == HRESULT_FROM_WIN32(ERROR_CANCELLED)) return ERROR_SUCCESS;
        if (FAILED(choice.result) || choice.path.empty()) {
            ShowError(install, L"The folder browser could not open. You can enter the installation folder directly.");
            return ERROR_SUCCESS;
        }
        // Update MSI's directory tree; the UI then republishes the property to refresh PathEdit.
        if (MsiSetTargetPathW(install, L"INSTALLFOLDER", choice.path.c_str()) != ERROR_SUCCESS)
            ShowError(install, L"This folder could not be selected. Choose another folder or enter its path directly.");
        return ERROR_SUCCESS;
    } catch (...) {
        ShowError(install, L"The folder browser is unavailable. Enter the installation folder directly.");
        return ERROR_SUCCESS;
    }
}
