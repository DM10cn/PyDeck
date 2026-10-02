#define PYDECK_SETUP_TEST
#include "../src/PyDeck.Setup/Setup.cpp"
#include <algorithm>

void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
int wmain(int argumentCount, wchar_t** arguments) {
    try {
        unsigned groups = 0;
        for (unsigned mask = 0; mask < 8; ++mask) {
            for (bool only : {false, true}) {
                Snapshot state{true, {(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0}};
                std::vector<Package> launched;
                Operations ops{[&] { return state; }, [] { return false; }, [&](Package p) {
                    launched.push_back(p);
                    if (p != Package::App) state.ready[static_cast<unsigned>(p)] = true;
                    return 0ul;
                }, [](Package, bool) {}};
                const auto result = Run(ops, only);
                Require(result.outcome == Outcome::Success, "Readiness combination failed");
                for (unsigned i = 0; i < 3; ++i) Require((std::count(launched.begin(), launched.end(), static_cast<Package>(i)) == 1) == (!(mask & (1u << i)) && !(only && i == 2)), "Compatible runtime was reinstalled or missing runtime skipped");
                Require(std::count(launched.begin(), launched.end(), Package::App) == (only ? 0 : 1), "Wrong MSI launch count");
                Require(std::is_sorted(launched.begin(), launched.end()), "MSI ran before dependencies");
            }
        }
        ++groups;
        for (const auto code : {1618ul, 1602ul, 1223ul, 3010ul, 1641ul}) {
            unsigned launches = 0;
            Operations ops{[] { return Snapshot{true, {false,false,false}}; }, [] { return false; }, [&](Package) { ++launches; return code; }, [](Package,bool) {}};
            const auto result = Run(ops, false);
            Require(launches == 1, "Setup continued after failure, cancellation, or restart");
            Require(result.code == code, "Installer exit code was lost");
            Require(result.outcome == (code == 3010 || code == 1641 ? Outcome::Restart : code == 1602 || code == 1223 ? Outcome::Cancelled : Outcome::Failed), "Wrong outcome");
        }
        ++groups;
        {
            unsigned launches = 0;
            Operations ops{[] { return Snapshot{true, {false,false,false}}; }, [] { return false; }, [&](Package) { ++launches; return 0ul; }, [](Package,bool) {}};
            Require(Run(ops, false).outcome == Outcome::Failed && launches == 1, "Installer exit zero bypassed recheck");
            ops.cancelled = [] { return true; }; launches = 0;
            Require(Run(ops, false).outcome == Outcome::Cancelled && launches == 0, "Pre-cancel started an installer");
            ops.inspect = [] { return Snapshot{false, {true,true,true}}; };
            Require(Run(ops, false).outcome == Outcome::Unsupported && launches == 0, "Unsupported Windows started installation");
        }
        ++groups;
        {
            bool stopped = false;
            Snapshot state{true, {false,false,false}};
            unsigned launches = 0;
            Operations ops{[&] { return state; }, [&] { return stopped; }, [&](Package) { ++launches; state.ready[0] = true; stopped = true; return 0ul; }, [](Package,bool) {}};
            Require(Run(ops, false).outcome == Outcome::Cancelled && launches == 1, "Cancellation did not stop the next installer");
            unsigned inspections = 0;
            ops.cancelled = [] { return false; };
            ops.inspect = [&] { ++inspections; return Snapshot{true, {true, inspections < 5, true}}; };
            launches = 0;
            Require(Run(ops, false).outcome == Outcome::Failed && launches == 0, "Last readiness check did not catch removed registration");
        }
        ++groups;
        Require(Sha256(reinterpret_cast<const BYTE*>("abc"), 3) == L"ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA256 failed");
        const auto directory = PrivateDirectory(argumentCount > 1 ? arguments[1] : std::wstring());
        const auto path = directory + L"\\test.exe";
        bool rejected = false;
        try { const auto file = Extract(Payloads[0].id, path, L"invalid"); } catch (const std::exception&) { rejected = true; }
        Require(rejected && !prereq::FileExists(path), "Tampered payload was written");
        {
            const auto file = Extract(Payloads[0].id, path, Payloads[0].sha256);
            Require(prereq::Amd64Image(path) || prereq::FileExists(path), "Payload extraction failed");
            Handle writer(CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr));
            Require(writer.value == INVALID_HANDLE_VALUE, "Extracted installer was writable during execution window");
        }
        Require(DeleteFileW(path.c_str()) && RemoveDirectoryW(directory.c_str()), "Owned test file cleanup failed");
        ++groups;
        for (const auto style : {InterfaceStyle::Material, InterfaceStyle::Fluent}) {
            const auto args = MsiArguments(L"C:\\安装 目录\\PyDeck.msi", L"C:\\Setup logs", style);
            Require(args.find(std::wstring(L"PYDECKSTYLE=") + StyleName(style)) != std::wstring::npos, "Setup did not forward the chosen interface style");
            Require(args.find(L"/i \"C:\\安装 目录\\PyDeck.msi\"") == 0 && args.find(L"/L*V \"C:\\Setup logs\\msi.log\"") != std::wstring::npos, "MSI paths with spaces were not quoted");
        }
        ++groups;
        INITCOMMONCONTROLSEX controls{sizeof(controls), ICC_STANDARD_CLASSES | ICC_PROGRESS_CLASS}; InitCommonControlsEx(&controls);
        Model model;
        const auto dialog = CreateDialogParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_SETUP), nullptr, SetupDialog, reinterpret_cast<LPARAM>(&model));
        Require(dialog != nullptr, "Setup dialog did not load");
        for (unsigned language = 0; language < 4; ++language) {
            model.language = language;
            for (const auto style : {InterfaceStyle::Material, InterfaceStyle::Fluent}) {
                model.interfaceStyle = style;
                Render(dialog, model, {true,{true,true,true}});
                Require(SendDlgItemMessageW(dialog, IDC_STYLE, CB_GETCOUNT, 0, 0) == 2 &&
                    SendDlgItemMessageW(dialog, IDC_STYLE, CB_GETCURSEL, 0, 0) == static_cast<LRESULT>(style), "Localized UI lost the selected style");
                const auto other = style == InterfaceStyle::Material ? InterfaceStyle::Fluent : InterfaceStyle::Material;
                SendDlgItemMessageW(dialog, IDC_STYLE, CB_SETCURSEL, static_cast<WPARAM>(other), 0);
                SendMessageW(dialog, WM_COMMAND, MAKEWPARAM(IDC_STYLE, CBN_SELCHANGE), reinterpret_cast<LPARAM>(GetDlgItem(dialog, IDC_STYLE)));
                Require(model.interfaceStyle == other, "Style selection was not recorded");
            }
            model.dependenciesOnly = true; Render(dialog, model, {true,{true,true,true}});
            Require(!IsWindowEnabled(GetDlgItem(dialog, IDC_STYLE)), "Dependency-only mode offered an ineffective style choice");
            model.dependenciesOnly = false;
            for (unsigned mask = 0; mask < 8; ++mask) {
                Render(dialog, model, {true, {(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0}});
                std::array<wchar_t, 512> text{};
                GetDlgItemTextW(dialog, IDC_NET, text.data(), static_cast<int>(text.size()));
                Require(std::wstring(text.data()).find(mask & 1 ? Text[language].skip : Text[language].needed) != std::wstring::npos, "Incorrect localized plan");
                Require(IsWindowEnabled(GetDlgItem(dialog, IDOK)), "Missing prerequisites incorrectly disabled Continue");
            }
            model.active = true; Render(dialog, model);
            Require(!IsWindowEnabled(GetDlgItem(dialog, IDOK)) && !IsWindowEnabled(GetDlgItem(dialog, IDC_LANGUAGE)) && !IsWindowEnabled(GetDlgItem(dialog, IDC_STYLE)), "Concurrent setup or style change was allowed");
            model.active = false; Render(dialog, model, {false,{true,true,true}});
            Require(!IsWindowEnabled(GetDlgItem(dialog, IDOK)), "Unsupported Windows allowed Continue");
        }
        DestroyWindow(dialog);
        ++groups;
        std::printf("PASS %u setup check groups: skip/install combinations, current-user recheck, cancellation, restart, SHA256/file locking, style forwarding, and four-language native UI. No installer was executed.\n", groups);
        return 0;
    } catch (const std::exception& error) { std::fprintf(stderr, "FAIL %s\n", error.what()); return 1; }
}
