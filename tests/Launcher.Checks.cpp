#define PYDECK_LAUNCHER_TEST
#include "../src/PyDeck.Launcher/Launcher.cpp"
#include <cstdio>
#include <stdexcept>

void Require(bool valid, const char* message) { if (!valid) throw std::runtime_error(message); }
int main() {
    try {
        std::array<unsigned, 4> parts{};
        Require(Version(L"10.0.15", parts, 3) && parts[2] == 15, "Stable .NET patch rejected");
        for (auto text : {L"10.0.1-preview", L"10.0.1.0", L"10..1", L"10.0.999999999", L"10.0.1junk", L"-1.0.0"})
            Require(!Version(text, parts, 3), "Invalid version accepted");
        for (unsigned mask = 0; mask < 16; ++mask) {
            State state{(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, false};
            Require(state.Ready() == (mask == 15), "Partial dependencies allowed launch");
            Require(state.Missing() == (15u ^ mask), "Missing dependency mask incorrect");
        }
        Require(State{true, true, true, false, true}.Ready(), "Packaged runtime path blocked on unpackaged VC registry");
        Require(!HasNet(L"Z:\\PyDeck-nonexistent-dotnet"), "Missing .NET root accepted");
        const auto previousRoot = Environment(L"DOTNET_ROOT_X64");
        SetEnvironmentVariableW(L"DOTNET_ROOT_X64", L"Z:\\PyDeck-nonexistent-dotnet");
        Require(!Inspect().net, "Invalid x64 runtime override incorrectly falls back to an unrelated runtime");
        SetEnvironmentVariableW(L"DOTNET_ROOT_X64", previousRoot.empty() ? nullptr : previousRoot.c_str());
        std::array<wchar_t, 32768> ownPath{};
        GetModuleFileNameW(nullptr, ownPath.data(), static_cast<DWORD>(ownPath.size()));
        Require(Amd64Image(ownPath.data()), "x64 PE image rejected");
        std::array<wchar_t, MAX_PATH> windowsPath{};
        GetWindowsDirectoryW(windowsPath.data(), static_cast<UINT>(windowsPath.size()));
        Require(!Amd64Image(std::wstring(windowsPath.data()) + L"\\SysWOW64\\kernel32.dll"), "x86 PE image accepted as x64");
        INITCOMMONCONTROLSEX controls{sizeof(controls), ICC_STANDARD_CLASSES};
        InitCommonControlsEx(&controls);
        DialogState model{};
        model.appExists = true;
        const HWND dialog = CreateDialogParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_DEPENDENCIES), nullptr, DialogProc, reinterpret_cast<LPARAM>(&model));
        Require(dialog != nullptr, "Native dependency dialog creation failed");
        for (unsigned language = 0; language < 4; ++language) {
            model.language = language;
            for (unsigned mask = 0; mask < 16; ++mask) {
                model.state = {(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, false};
                Render(dialog, model);
                Require(IsWindowEnabled(GetDlgItem(dialog, IDOK)) == (mask == 15), "Dialog launch button not gated");
                std::array<wchar_t, 256> status{};
                GetDlgItemTextW(dialog, IDC_NET, status.data(), static_cast<int>(status.size()));
                Require(std::wstring(status.data()).find(mask & 2 ? Languages[language].ready : Languages[language].missing) != std::wstring::npos, "Localized status mismatch");
                Require(IsWindowEnabled(GetDlgItem(dialog, IDC_DOWNLOAD_NET)), "Download unavailable when repair may be needed");
            }
        }
        model.appExists = false;
        Render(dialog, model);
        Require(!IsWindowEnabled(GetDlgItem(dialog, IDOK)), "Standalone helper can launch an app");
        SendMessageW(dialog, WM_COMMAND, IDC_RECHECK, 0);
        Require(model.state.Ready() == Inspect().Ready(), "Recheck did not refresh the state");
        DestroyWindow(dialog);
        std::puts("PASS version parsing, all prerequisite combinations, four-language native dialog, standalone gating and live recheck");
        Report(Inspect());
        return 0;
    } catch (const std::exception& error) { std::fprintf(stderr, "FAIL %s\n", error.what()); return 1; }
}
