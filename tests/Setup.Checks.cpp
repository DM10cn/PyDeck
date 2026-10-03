#define PYDECK_SETUP_TEST
#include "../src/PyDeck.Setup/Setup.cpp"
#include <algorithm>

void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
std::vector<std::wstring> ParsedArguments(const std::wstring& arguments) {
    int count = 0;
    auto values = CommandLineToArgvW((L"msiexec.exe " + arguments).c_str(), &count);
    Require(values != nullptr, "Command-line parsing failed");
    const std::vector<std::wstring> result(values, values + count);
    LocalFree(values);
    return result;
}
bool Shown(HWND dialog, int control) {
    // The test dialog itself stays hidden; inspect the child's own visibility.
    return (GetWindowLongPtrW(GetDlgItem(dialog, control), GWL_STYLE) & WS_VISIBLE) != 0;
}
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
        const auto invalidFolder = [](const std::wstring& folder) {
            bool rejectedFolder = false;
            try { (void)ValidateInstallFolder(folder); }
            catch (const std::invalid_argument&) { rejectedFolder = true; }
            Require(rejectedFolder, "Invalid installation folder was accepted");
        };
        invalidFolder(path);
        invalidFolder(path + L"\\child");
        Require(DeleteFileW(path.c_str()), "Owned test file cleanup failed");
        ++groups;
        const auto installFolder = directory + L"\\安装 目录";
        Require(ValidateInstallFolder(installFolder + L"\\child\\..\\") == installFolder,
                "Installation folder normalization changed the destination");
        Require(GetFileAttributesW(installFolder.c_str()) == INVALID_FILE_ATTRIBUTES,
                "Folder validation created the destination");
        for (const auto folder : {L"", L"relative\\PyDeck", L"C:PyDeck", L"\\PyDeck", L"C:\\",
                 L"\\\\server\\share", L"\\\\?\\C:\\PyDeck", L"C:\\bad\"folder", L"C:\\bad\nfolder",
                 L"C:\\bad*folder", L"C:\\bad:folder", L"C:\\CON", L"C:\\folder. ", L"C:\\folder."})
            invalidFolder(folder);
        invalidFolder(directory + L"\\bad" + std::wstring(1, L'\0') + L"folder");
        invalidFolder(directory + L"\\" + std::wstring(241, L'a'));
        ++groups;
        for (const auto style : {InterfaceStyle::Material, InterfaceStyle::Fluent}) {
            for (bool desktop : {false, true}) for (bool startMenu : {false, true}) {
                const InstallOptions options{installFolder + L"\\", desktop, startMenu};
                const auto args = MsiArguments(L"C:\\安装 目录\\PyDeck.msi", L"C:\\Setup logs\\", style, options);
                const auto parsed = ParsedArguments(args);
                const auto has = [&](const std::wstring& value) { return std::find(parsed.begin(), parsed.end(), value) != parsed.end(); };
                Require(parsed.size() > 2 && parsed[1] == L"/i" && parsed[2] == L"C:\\安装 目录\\PyDeck.msi" &&
                    has(L"C:\\Setup logs\\msi.log") && has(L"INSTALLFOLDER=" + installFolder), "MSI paths with spaces were not quoted correctly");
                Require(has(L"/qn") && has(L"/norestart") && has(L"REBOOT=ReallySuppress") && has(L"MSIRESTARTMANAGERCONTROL=DisableShutdown"),
                    "Setup exposed a second wizard, automatic restart, or application shutdown");
                Require(has(std::wstring(L"PYDECKSTYLE=") + StyleName(style)), "Setup did not forward the chosen interface style");
                Require(has(std::wstring(L"DESKTOPSHORTCUT=") + (desktop ? L"1" : L"0")) &&
                    has(std::wstring(L"STARTMENUSHORTCUT=") + (startMenu ? L"1" : L"0")), "Shortcut choices were not forwarded explicitly");
            }
        }
        const auto trailingSlash = ParsedArguments(install_options_detail::QuoteArgument(L"C:\\folder with spaces\\"));
        Require(trailingSlash.size() == 2 && trailingSlash[1] == L"C:\\folder with spaces\\", "Trailing backslash escaped the closing argument quote");
        bool styleRejected = false;
        try { (void)BuildMsiArguments(L"C:\\PyDeck.msi", directory, L"Material OTHER=1", {installFolder, false, true}); }
        catch (const std::invalid_argument&) { styleRejected = true; }
        Require(styleRejected, "Unexpected interface-style value was accepted");
        Require(RemoveDirectoryW(directory.c_str()), "Owned test directory cleanup failed");
        ++groups;
        Require(SetupLanguage(MAKELANGID(LANG_ENGLISH, SUBLANG_ENGLISH_US)) == 0 &&
            SetupLanguage(MAKELANGID(LANG_GERMAN, SUBLANG_GERMAN)) == 0, "Unsupported language did not fall back to English");
        Require(SetupLanguage(MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SIMPLIFIED)) == 1 &&
            SetupLanguage(MAKELANGID(LANG_CHINESE, SUBLANG_CHINESE_SINGAPORE)) == 1, "Simplified Chinese language mapping failed");
        for (const auto region : {SUBLANG_CHINESE_TRADITIONAL, SUBLANG_CHINESE_HONGKONG, SUBLANG_CHINESE_MACAU})
            Require(SetupLanguage(static_cast<LANGID>(MAKELANGID(LANG_CHINESE, region))) == 2, "Traditional Chinese language mapping failed");
        Require(SetupLanguage(MAKELANGID(LANG_JAPANESE, SUBLANG_DEFAULT)) == 3, "Japanese language mapping failed");
        ++groups;
        INITCOMMONCONTROLSEX controls{sizeof(controls), ICC_STANDARD_CLASSES | ICC_PROGRESS_CLASS}; InitCommonControlsEx(&controls);
        Model model;
        ProductState fixtureProduct;
        Snapshot fixturePrerequisites{true, {true, true, true}};
        bool fixtureRunning = false, fixtureConfirmRemove = false;
        unsigned productInspections = 0, prerequisiteInspections = 0, runningChecks = 0, confirmations = 0, started = 0;
        InstallOptions startedOptions;
        InterfaceStyle startedStyle = InterfaceStyle::Material;
        bool startedDependenciesOnly = false, startedRemoving = false;
        model.actions.inspectProduct = [&] { ++productInspections; return fixtureProduct; };
        model.actions.inspectPrerequisites = [&] { ++prerequisiteInspections; return fixturePrerequisites; };
        model.actions.isRunning = [&](const std::vector<std::wstring>&) { ++runningChecks; return fixtureRunning; };
        model.actions.confirmRemove = [&](HWND, const wchar_t*) { ++confirmations; return fixtureConfirmRemove; };
        model.actions.startWork = [&](HWND, Model& pending) {
            ++started;
            startedOptions = pending.options;
            startedStyle = pending.interfaceStyle;
            startedDependenciesOnly = pending.dependenciesOnly;
            startedRemoving = pending.removing;
        };
        const auto dialog = CreateDialogParamW(GetModuleHandleW(nullptr), MAKEINTRESOURCEW(IDD_SETUP), nullptr, SetupDialog, reinterpret_cast<LPARAM>(&model));
        Require(dialog != nullptr, "Setup dialog did not load");
        // Every inspection/action used by button callbacks is a fixture. The work
        // callback only captures choices and never starts a thread or installer.
        model.product = {};
        for (unsigned language = 0; language < 4; ++language) {
            model.language = language;
            for (const auto style : {InterfaceStyle::Material, InterfaceStyle::Fluent}) {
                model.interfaceStyle = style;
                Render(dialog, model, Snapshot{true,{true,true,true}});
                Require(SendDlgItemMessageW(dialog, IDC_STYLE, CB_GETCOUNT, 0, 0) == 2 &&
                    SendDlgItemMessageW(dialog, IDC_STYLE, CB_GETCURSEL, 0, 0) == static_cast<LRESULT>(style), "Localized UI lost the selected style");
                const auto other = style == InterfaceStyle::Material ? InterfaceStyle::Fluent : InterfaceStyle::Material;
                SendDlgItemMessageW(dialog, IDC_STYLE, CB_SETCURSEL, static_cast<WPARAM>(other), 0);
                SendMessageW(dialog, WM_COMMAND, MAKEWPARAM(IDC_STYLE, CBN_SELCHANGE), reinterpret_cast<LPARAM>(GetDlgItem(dialog, IDC_STYLE)));
                Require(model.interfaceStyle == other, "Style selection was not recorded");
            }
            model.dependenciesOnly = true; Render(dialog, model, Snapshot{true,{true,true,true}});
            Require(!IsWindowEnabled(GetDlgItem(dialog, IDC_STYLE)), "Dependency-only mode offered an ineffective style choice");
            for (const auto control : {IDC_FOLDER, IDC_BROWSE, IDC_DESKTOP, IDC_START_MENU})
                Require(!IsWindowEnabled(GetDlgItem(dialog, control)), "Dependency-only mode offered installation options");
            model.dependenciesOnly = false;
            for (unsigned mask = 0; mask < 8; ++mask) {
                Render(dialog, model, Snapshot{true, {(mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0}});
                std::array<wchar_t, 512> text{};
                GetDlgItemTextW(dialog, IDC_NET, text.data(), static_cast<int>(text.size()));
                Require(std::wstring(text.data()).find(mask & 1 ? Text[language].skip : Text[language].needed) != std::wstring::npos, "Incorrect localized plan");
                Require(IsWindowEnabled(GetDlgItem(dialog, IDOK)), "Missing prerequisites incorrectly disabled Continue");
            }
            model.active = true; Render(dialog, model);
            Require(!IsWindowEnabled(GetDlgItem(dialog, IDOK)) && !IsWindowEnabled(GetDlgItem(dialog, IDC_LANGUAGE)) && !IsWindowEnabled(GetDlgItem(dialog, IDC_STYLE)), "Concurrent setup or style change was allowed");
            model.active = false; Render(dialog, model, Snapshot{false,{true,true,true}});
            Require(!IsWindowEnabled(GetDlgItem(dialog, IDOK)), "Unsupported Windows allowed Continue");
        }
        ++groups;
        const auto selectChoice = [&](int control, WPARAM choice) {
            SendDlgItemMessageW(dialog, control, CB_SETCURSEL, choice, 0);
            SendMessageW(dialog, WM_COMMAND, MAKEWPARAM(control, CBN_SELCHANGE), reinterpret_cast<LPARAM>(GetDlgItem(dialog, control)));
        };
        const auto notifyClick = [&](int control) {
            SendMessageW(dialog, WM_COMMAND, MAKEWPARAM(control, BN_CLICKED), reinterpret_cast<LPARAM>(GetDlgItem(dialog, control)));
        };
        model.snapshot = {true, {true, true, true}};
        model.interfaceStyle = InterfaceStyle::Material;
        SetDlgItemTextW(dialog, IDC_FOLDER, installFolder.c_str());
        CheckDlgButton(dialog, IDC_DESKTOP, BST_UNCHECKED);
        CheckDlgButton(dialog, IDC_START_MENU, BST_CHECKED);
        CheckDlgButton(dialog, IDC_ONLY, BST_UNCHECKED);
        Render(dialog, model);
        SendDlgItemMessageW(dialog, IDC_DESKTOP, BM_CLICK, 0, 0);
        SendDlgItemMessageW(dialog, IDC_START_MENU, BM_CLICK, 0, 0);
        Require(IsDlgButtonChecked(dialog, IDC_DESKTOP) == BST_CHECKED && IsDlgButtonChecked(dialog, IDC_START_MENU) == BST_UNCHECKED,
            "Shortcut checkbox clicks did not toggle their choices");
        selectChoice(IDC_STYLE, static_cast<WPARAM>(InterfaceStyle::Fluent));
        for (unsigned language = 0; language < 4; ++language) {
            selectChoice(IDC_LANGUAGE, language);
            Require(model.language == language && ControlText(dialog, IDC_FOLDER_LABEL) == UiText[language].folder,
                "Language selection callback did not translate the dialog");
            Require(ControlText(dialog, IDC_FOLDER) == installFolder && model.interfaceStyle == InterfaceStyle::Fluent &&
                SendDlgItemMessageW(dialog, IDC_STYLE, CB_GETCURSEL, 0, 0) == static_cast<LRESULT>(InterfaceStyle::Fluent) &&
                IsDlgButtonChecked(dialog, IDC_DESKTOP) == BST_CHECKED && IsDlgButtonChecked(dialog, IDC_START_MENU) == BST_UNCHECKED,
                "Language switching discarded installation choices");
        }
        SendDlgItemMessageW(dialog, IDC_ONLY, BM_CLICK, 0, 0);
        Require(model.dependenciesOnly && IsDlgButtonChecked(dialog, IDC_ONLY) == BST_CHECKED,
            "Dependency-only checkbox click was not recorded");
        for (const auto control : {IDC_FOLDER, IDC_BROWSE, IDC_DESKTOP, IDC_START_MENU, IDC_STYLE})
            Require(!IsWindowEnabled(GetDlgItem(dialog, control)), "Dependency-only click did not disable install-only options");
        selectChoice(IDC_STYLE, static_cast<WPARAM>(InterfaceStyle::Material));
        Require(model.interfaceStyle == InterfaceStyle::Fluent, "Disabled style notification changed dependency-only choices");
        SendDlgItemMessageW(dialog, IDC_ONLY, BM_CLICK, 0, 0);
        Require(!model.dependenciesOnly && IsDlgButtonChecked(dialog, IDC_ONLY) == BST_UNCHECKED,
            "Dependency-only checkbox could not return to installation mode");
        for (const auto control : {IDC_FOLDER, IDC_BROWSE, IDC_DESKTOP, IDC_START_MENU, IDC_STYLE})
            Require(IsWindowEnabled(GetDlgItem(dialog, control)), "Leaving dependency-only mode did not restore options");
        Require(ControlText(dialog, IDC_FOLDER) == installFolder && model.interfaceStyle == InterfaceStyle::Fluent &&
            SendDlgItemMessageW(dialog, IDC_STYLE, CB_GETCURSEL, 0, 0) == static_cast<LRESULT>(InterfaceStyle::Fluent) &&
            IsDlgButtonChecked(dialog, IDC_DESKTOP) == BST_CHECKED && IsDlgButtonChecked(dialog, IDC_START_MENU) == BST_UNCHECKED,
            "Dependency-only round trip discarded installation choices");
        ++groups;
        model.active = true;
        model.product.productCode = L"guard-fixture-product";
        model.snapshot = {true, {false, true, false}};
        Render(dialog, model);
        const auto activeLanguage = model.language;
        selectChoice(IDC_STYLE, static_cast<WPARAM>(InterfaceStyle::Material));
        selectChoice(IDC_LANGUAGE, (activeLanguage + 1) % 4);
        notifyClick(IDC_RECHECK);
        CheckDlgButton(dialog, IDC_ONLY, BST_CHECKED);
        notifyClick(IDC_ONLY);
        Require(model.interfaceStyle == InterfaceStyle::Fluent && model.language == activeLanguage && !model.dependenciesOnly,
            "Disabled notifications changed choices during installation");
        Require(model.product.productCode == L"guard-fixture-product" && model.snapshot.supported &&
            model.snapshot.ready == std::array<bool, 3>{false, true, false}, "Recheck ran while installation was active");
        model.active = false;
        model.complete = true;
        Render(dialog, model);
        selectChoice(IDC_STYLE, static_cast<WPARAM>(InterfaceStyle::Material));
        notifyClick(IDC_ONLY);
        Require(model.interfaceStyle == InterfaceStyle::Fluent && !model.dependenciesOnly,
            "Disabled notifications changed completed installation choices");
        // Completion still permits translating the result, while options remain locked.
        selectChoice(IDC_LANGUAGE, 1);
        Require(model.language == 1 && model.complete && ControlText(dialog, IDC_FOLDER) == installFolder,
            "Completion language change lost the result or folder selection");
        model.complete = false;
        model.product = {};
        CheckDlgButton(dialog, IDC_ONLY, BST_UNCHECKED);
        ++groups;
        model.snapshot = {true, {true, true, true}};
        for (const auto conflict : {InstallRelation::SameVersionConflict, InstallRelation::NewerInstalled}) {
            model.product.relation = conflict;
            Render(dialog, model);
            Require(!IsWindowEnabled(GetDlgItem(dialog, IDOK)), "A conflicting installed product allowed installation");
            Require(ControlText(dialog, IDC_STATUS) == (conflict == InstallRelation::NewerInstalled ? UiText[model.language].newerVersion : UiText[model.language].sameVersion),
                "An installed-product conflict had no actionable explanation");
            model.dependenciesOnly = true;
            Render(dialog, model);
            Require(IsWindowEnabled(GetDlgItem(dialog, IDOK)), "An installed-product conflict blocked dependency-only preparation");
            model.dependenciesOnly = false;
        }
        model.product.relation = InstallRelation::Repair;
        Render(dialog, model);
        Require(!IsWindowEnabled(GetDlgItem(dialog, IDC_FOLDER)) && !IsWindowEnabled(GetDlgItem(dialog, IDC_BROWSE)) &&
            IsWindowEnabled(GetDlgItem(dialog, IDC_DESKTOP)), "Repair offered relocation or disabled shortcut maintenance");
        model.product = {};
        ++groups;
        model.complete = true;
        model.result = {Outcome::Success, Package::App, 0};
        Render(dialog, model);
        Require(Shown(dialog, IDC_LAUNCH) && IsWindowEnabled(GetDlgItem(dialog, IDC_LAUNCH)) && !Shown(dialog, IDOK),
            "Successful app installation did not offer an explicit launch button");
        Require(LOWORD(SendMessageW(dialog, DM_GETDEFID, 0, 0)) == IDCANCEL,
            "Completion did not transfer the Enter default from the hidden Install button to Close");
        for (const auto outcome : {Outcome::Restart, Outcome::Failed, Outcome::Cancelled}) {
            model.result.outcome = outcome;
            Render(dialog, model);
            Require(!Shown(dialog, IDC_LAUNCH) && !IsWindowEnabled(GetDlgItem(dialog, IDC_LAUNCH)),
                "Incomplete installation or required restart offered app launch");
        }
        model.result.outcome = Outcome::Success;
        model.dependenciesOnly = true;
        Render(dialog, model);
        Require(!Shown(dialog, IDC_LAUNCH) && !IsWindowEnabled(GetDlgItem(dialog, IDC_LAUNCH)), "Dependency-only completion offered app launch");
        model.dependenciesOnly = false;
        model.removing = true;
        Render(dialog, model);
        Require(!Shown(dialog, IDC_LAUNCH) && !IsWindowEnabled(GetDlgItem(dialog, IDC_LAUNCH)), "Uninstall completion offered app launch");
        model.complete = false;
        model.result.outcome = Outcome::Failed;
        model.language = 0;
        Render(dialog, model);
        Require(IsWindowEnabled(GetDlgItem(dialog, IDOK)) && ControlText(dialog, IDOK) == L"Retry uninstall",
            "Failed uninstall did not offer an explicit uninstall retry");
        for (const auto control : {IDC_FOLDER, IDC_BROWSE, IDC_DESKTOP, IDC_START_MENU, IDC_STYLE, IDC_ONLY})
            Require(!IsWindowEnabled(GetDlgItem(dialog, control)), "Uninstall retry re-enabled install-only options");
        model.result.outcome = Outcome::Success;
        model.removing = false;
        ++groups;
        model.preview = true;
        model.complete = false;
        model.product.productCode = L"{00000000-0000-0000-0000-000000000001}";
        Render(dialog, model);
        Require(!IsWindowEnabled(GetDlgItem(dialog, IDOK)) && !IsWindowEnabled(GetDlgItem(dialog, IDC_REMOVE)),
            "Preview enabled install or uninstall");
        const auto previewProduct = model.product.productCode;
        model.options = {installFolder, true, false};
        CheckDlgButton(dialog, IDC_ONLY, BST_CHECKED);
        BeginAction(dialog, model, false);
        BeginAction(dialog, model, true);
        notifyClick(IDOK);
        notifyClick(IDC_REMOVE);
        Require(!model.active && !model.worker.joinable() && model.directory.empty() && !model.removing && !model.dependenciesOnly &&
            model.product.productCode == previewProduct && model.options.folder == installFolder && model.options.desktop && !model.options.startMenu,
            "Preview action callback changed operation state or started work");
        Require(GetFileAttributesW(directory.c_str()) == INVALID_FILE_ATTRIBUTES,
            "Preview action callback created its fixture installation directory");
        model.complete = true;
        Render(dialog, model);
        Require(!IsWindowEnabled(GetDlgItem(dialog, IDC_LAUNCH)), "Preview enabled app launch");
        SetDlgItemTextW(dialog, IDC_STATUS, L"preview-launch-guard");
        notifyClick(IDC_LAUNCH);
        Require(IsWindow(dialog) && ControlText(dialog, IDC_STATUS) == L"preview-launch-guard" &&
            !model.active && !model.worker.joinable() && model.directory.empty(), "Preview launch callback attempted to open the app");
        Require(started == 0, "Preview reached the work callback");
        ++groups;
        const auto resetAction = [&] {
            model.active = model.complete = model.dependenciesOnly = model.removing = model.preview = false;
            model.stop.store(false);
            model.directory.clear();
            model.product = fixtureProduct;
            model.snapshot = fixturePrerequisites;
            model.result = {Outcome::Cancelled, Package::App, 1602};
            model.options = {installFolder, false, true};
            model.interfaceStyle = InterfaceStyle::Material;
            SetDlgItemTextW(dialog, IDC_FOLDER, installFolder.c_str());
            CheckDlgButton(dialog, IDC_DESKTOP, BST_UNCHECKED);
            CheckDlgButton(dialog, IDC_START_MENU, BST_CHECKED);
            CheckDlgButton(dialog, IDC_ONLY, BST_UNCHECKED);
            SendDlgItemMessageW(dialog, IDC_PROGRESS, PBM_SETMARQUEE, FALSE, 0);
            Render(dialog, model);
        };
        resetAction();
        const auto selectedFolder = installFolder + L"\\Selected PyDeck";
        SetDlgItemTextW(dialog, IDC_FOLDER, (selectedFolder + L"\\").c_str());
        SendDlgItemMessageW(dialog, IDC_DESKTOP, BM_CLICK, 0, 0);
        SendDlgItemMessageW(dialog, IDC_START_MENU, BM_CLICK, 0, 0);
        selectChoice(IDC_STYLE, static_cast<WPARAM>(InterfaceStyle::Fluent));
        notifyClick(IDOK);
        Require(started == 1 && model.active && !model.worker.joinable() && model.directory.empty() &&
            startedOptions.folder == selectedFolder && startedOptions.desktop && !startedOptions.startMenu &&
            startedStyle == InterfaceStyle::Fluent && !startedDependenciesOnly && !startedRemoving,
            "Install callback did not forward edited folder, shortcuts, and style to its work boundary");
        const auto inspectionsAtStart = productInspections;
        notifyClick(IDOK);
        Require(started == 1 && productInspections == inspectionsAtStart, "An active install callback started concurrent work");
        notifyClick(IDCANCEL);
        Require(model.active && model.stop.load() && IsWindow(dialog), "Cancel during active work did not request a deferred stop");
        ++groups;
        resetAction();
        SendDlgItemMessageW(dialog, IDC_ONLY, BM_CLICK, 0, 0);
        SetDlgItemTextW(dialog, IDC_FOLDER, L"relative-invalid-folder");
        const auto runningBeforeDependencies = runningChecks;
        notifyClick(IDOK);
        Require(started == 2 && startedDependenciesOnly && !startedRemoving && startedOptions.folder == installFolder &&
            runningChecks == runningBeforeDependencies, "Dependency-only callback validated an unused folder or blocked on a running app");
        ++groups;
        for (const auto conflict : {InstallRelation::SameVersionConflict, InstallRelation::NewerInstalled}) {
            fixtureProduct.relation = conflict;
            resetAction();
            notifyClick(IDOK);
            Require(started == 2 && !model.active && !model.worker.joinable(), "Conflicting upgrade callback crossed the work boundary");
            Require(ControlText(dialog, IDC_STATUS) == (conflict == InstallRelation::NewerInstalled ? UiText[model.language].newerVersion : UiText[model.language].sameVersion),
                "Conflicting upgrade callback did not explain the blocked action");
        }
        const auto registeredFolder = installFolder + L"\\Registered PyDeck";
        fixtureProduct = {InstallRelation::Repair, L"fixture-installed-product", PayloadProductVersion, registeredFolder};
        resetAction();
        const auto productsBeforeRecheck = productInspections, prerequisitesBeforeRecheck = prerequisiteInspections;
        notifyClick(IDC_RECHECK);
        Require(productInspections == productsBeforeRecheck + 1 && prerequisiteInspections == prerequisitesBeforeRecheck + 1 &&
            model.options.folder == registeredFolder && ControlText(dialog, IDC_FOLDER) == registeredFolder,
            "Recheck callback did not refresh the registered repair location");
        notifyClick(IDC_BROWSE);
        Require(ControlText(dialog, IDC_FOLDER) == registeredFolder && started == 2, "Repair browse callback changed its locked location");
        SetDlgItemTextW(dialog, IDC_FOLDER, selectedFolder.c_str());
        notifyClick(IDOK);
        Require(started == 3 && startedOptions.folder == registeredFolder && !startedRemoving && !startedDependenciesOnly &&
            ControlText(dialog, IDC_FOLDER) == registeredFolder, "Repair callback accepted an alternate installation location");
        ++groups;
        resetAction();
        SendDlgItemMessageW(dialog, IDC_ONLY, BM_CLICK, 0, 0);
        fixtureConfirmRemove = false;
        const auto confirmationsBeforeCancel = confirmations;
        notifyClick(IDC_REMOVE);
        Require(confirmations == confirmationsBeforeCancel + 1 && started == 3 && !model.active && !model.removing &&
            model.dependenciesOnly && IsDlgButtonChecked(dialog, IDC_ONLY) == BST_CHECKED,
            "Cancelling uninstall confirmation changed the selected dependency-only mode");
        fixtureRunning = true;
        const auto confirmationsBeforeRunning = confirmations;
        notifyClick(IDC_REMOVE);
        Require(confirmations == confirmationsBeforeRunning && started == 3 && !model.active && !model.removing &&
            model.dependenciesOnly && IsDlgButtonChecked(dialog, IDC_ONLY) == BST_CHECKED &&
            ControlText(dialog, IDC_STATUS) == UiText[model.language].running,
            "Running-app uninstall guard lost the selected mode or opened confirmation");
        fixtureRunning = false;
        fixtureConfirmRemove = true;
        notifyClick(IDC_REMOVE);
        Require(started == 4 && startedRemoving && !startedDependenciesOnly && model.active &&
            !model.dependenciesOnly && IsDlgButtonChecked(dialog, IDC_ONLY) == BST_UNCHECKED,
            "Confirmed uninstall callback did not commit the uninstall operation");
        ++groups;
        model.active = false;
        model.result = {Outcome::Failed, Package::App, 1603};
        fixtureProduct = {};
        notifyClick(IDOK);
        Require(started == 4 && model.complete && model.result.outcome == Outcome::Success && model.removing &&
            !model.active && !Shown(dialog, IDC_LAUNCH), "Uninstall retry did not recognize an already-removed target");
        resetAction();
        model.removing = true;
        model.result = {Outcome::Failed, Package::App, 1603};
        notifyClick(IDC_RECHECK);
        Require(started == 4 && model.complete && model.result.outcome == Outcome::Success && !Shown(dialog, IDC_LAUNCH),
            "Recheck did not finish an uninstall whose target was already removed");
        Require(!model.worker.joinable() && model.directory.empty() && GetFileAttributesW(directory.c_str()) == INVALID_FILE_ATTRIBUTES,
            "Button callback checks started external work or created an installation directory");
        unsigned openedPaths = 0;
        std::wstring openedPath, openedWorkingDirectory;
        model.actions.openPath = [&](HWND, const std::wstring& target, const std::wstring& working) {
            ++openedPaths; openedPath = target; openedWorkingDirectory = working;
            return false; // Exercise launch error reporting without starting anything.
        };
        resetAction();
        notifyClick(IDC_LOGS);
        Require(openedPaths == 0, "Logs button attempted to open a missing run directory");
        model.directory = directory;
        model.active = true;
        notifyClick(IDC_LOGS);
        Require(openedPaths == 0, "Logs button ignored its active-work guard");
        model.active = false;
        notifyClick(IDC_LOGS);
        Require(openedPaths == 1 && openedPath == directory && openedWorkingDirectory == directory,
            "Logs button did not forward the current run directory");
        model.complete = true;
        model.result = {Outcome::Success, Package::App, 0};
        notifyClick(IDC_LAUNCH);
        Require(openedPaths == 2 && openedPath == installFolder + L"\\PyDeck.Launcher.exe" && openedWorkingDirectory == installFolder &&
            ControlText(dialog, IDC_STATUS) == UiText[model.language].launchFailed,
            "Launch button routed to the wrong executable or failed without an explanation");
        for (const auto outcome : {Outcome::Restart, Outcome::Failed, Outcome::Cancelled}) {
            model.result.outcome = outcome;
            notifyClick(IDC_LAUNCH);
        }
        model.result.outcome = Outcome::Success;
        model.dependenciesOnly = true; notifyClick(IDC_LAUNCH);
        model.dependenciesOnly = false; model.removing = true; notifyClick(IDC_LAUNCH);
        model.removing = false; model.active = true; notifyClick(IDC_LAUNCH);
        Require(openedPaths == 2, "Launch callback bypassed an incomplete, dependency-only, removal, or active-work guard");
        ++groups;
        DestroyWindow(dialog);
        ++groups;
        std::printf("PASS %u setup check groups: workflow, payload integrity, paths/arguments, languages, native button callbacks, conflicts/repair, uninstall retry, completion and preview guards. No installer or application was executed.\n", groups);
        return 0;
    } catch (const std::exception& error) { std::fprintf(stderr, "FAIL %s\n", error.what()); return 1; }
}
