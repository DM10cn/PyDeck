using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace PimGui.App;

public sealed partial class MainWindow : Window
{
    private static string AppVersionLabel => System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(MainWindow).Assembly)?.InformationalVersion
        ?? typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";
    private PimClient client = new(new ProcessRunner());
    private readonly SettingsStore store;
    private AppSettings preferences;
    private DesktopPresentation presentation = null!;
    private string ActiveDesign => presentation.Design;
    private Palette palette = null!;
    private IReadOnlyList<PythonRuntime> installed = [];
    private IReadOnlyList<PythonRuntime>? catalog;
    private CancellationTokenSource? catalogCancellation;
    private Task catalogLoading = Task.CompletedTask;
    private OfflineBundle? offlineBundle;
    private bool OfflineSource => preferences.CatalogSource == "Offline";
    private readonly ActivityLog activityLog = new();
    private readonly CoalescedAction activityRefresh;
    private readonly CoalescedAction operationRefresh;
    private ActivityLevel? activityFilter;
    private string page = "runtimes";
    private string search = "";
    private string architecture = "All architectures";
    private bool initialized;
    private bool connected;
    private bool closeDialogOpen;
    private bool confirmationOpen;
    private ListView? runtimeRows;
    private TextBlock? resultLabel;
    private ScrollViewer? settingsScroll;
    private double settingsOffset;
    private readonly string? smokeDirectory;
    internal bool PerformanceProbe { get; }
    internal bool DesignProbe { get; }
    internal bool WorkspaceProbe { get; }
    internal bool RestartProbe { get; }
    internal int VisibleRuntimeCount { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        activityRefresh = new(action => DispatcherQueue.TryEnqueue(() => action()), () => RefreshActivityOutput());
        operationRefresh = new(action => DispatcherQueue.TryEnqueue(() => action()), UpdateOperationPanel);
        var args = Environment.GetCommandLineArgs();
        var smokeIndex = Array.IndexOf(args, "--smoke-test");
        if (smokeIndex >= 0 && smokeIndex + 1 < args.Length) smokeDirectory = Path.GetFullPath(args[smokeIndex + 1]);
        PerformanceProbe = smokeDirectory is not null && args.Contains("--performance-only");
        DesignProbe = smokeDirectory is not null && args.Contains("--design-only");
        WorkspaceProbe = smokeDirectory is not null && args.Contains("--workspace-only");
        RestartProbe = smokeDirectory is not null && args.Contains("--restart-only");
        store = new(smokeDirectory is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PyDeck")
            : Path.Combine(smokeDirectory, "preferences"));
        preferences = store.Load();
        if (smokeDirectory is null && !File.Exists(store.FilePath))
        {
            var legacyStore = new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PimGui"));
            preferences = legacyStore.Load();
            if (!File.Exists(legacyStore.FilePath))
            {
                using var installer = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\DM10cn\PyDeck\Installer");
                if (installer?.GetValue("InterfaceStyle") is string initialDesign && initialDesign is "Fluent" or "Material")
                    preferences = preferences with { Design = initialDesign };
            }
        }
        InitializePresentation(preferences.Design);
        InitializeMaterialColors();
        palette = new(DesignTokens.For(ActiveDesign, preferences.Theme == "Light", MaterialSeedForRender, preferences.MaterialColorStyle, MaterialSecondSeedForRender));
        client = CreateClient();
        InitializeSystemAppearance();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        InitializeWindowPreferences();
        AppWindow.Resize(new SizeInt32(1200, 820));
        AppWindow.Closing += OnClosing;
        Closed += (_, _) => CancelSearchRefresh();
        Root.Loaded += async (_, _) =>
        {
            if (initialized) return;
            initialized = true;
            CollectShellLabels(Root);
            ApplyLanguage();
            if (!PerformanceProbe && !DesignProbe && !WorkspaceProbe && !RestartProbe) SizeForDisplay();
            ApplyAppearance();
            RefreshMaterialColors();
            if (RestartProbe) { await RunRestartProbeAsync(smokeDirectory!); return; }
            if (WorkspaceProbe) { await RunWorkspaceProbeAsync(smokeDirectory!); return; }
            if (DesignProbe) { await RunDesignProbeAsync(smokeDirectory!); return; }
            if (PerformanceProbe) { await RunPerformanceProbeAsync(smokeDirectory!); return; }
            try { Builds.RecoverInterrupted(); ReloadLocalRuntimes(); } catch (Exception ex) { ShowError(ex); }
            await ConnectAsync();
            if (smokeDirectory is null && page == "runtimes" && preferences.StartupPage != "runtimes") Navigate(preferences.StartupPage);
            if (store.LoadWarning is { } warning) Notify(warning, InfoBarSeverity.Warning);
            if (smokeDirectory is not null) await RunSmokeTestAsync(smokeDirectory);
        };
        Root.ActualThemeChanged += OnActualThemeChanged;
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (initialized && !closed) QueueAppearance();
    }

    private void InitializePresentation(string design)
    {
        // Production calls this once. Isolated visual fixtures may explicitly create another presentation.
        presentation?.Detach();
        ShellHost.Children.Clear();
        // Before the initial visual tree is loaded, Parent can still be null even
        // though the footer owns this button. Detach through its known collection
        // before the Material drawer takes ownership.
        FooterActions.Children.Remove(RefreshDatabaseButton);
        presentation = design == "Fluent" ? new FluentPresentation() : new MaterialPresentation();
        var material = presentation.Design == "Material";
        if (!material) FooterActions.Children.Add(RefreshDatabaseButton);
        FooterBar.Visibility = material ? Visibility.Collapsed : Visibility.Visible;
        Root.RowDefinitions[2].Height = new GridLength(material ? 0 : 36);
        ShellHost.Children.Add(presentation.CreateShell(PageSurface, BrandPanel, ConnectionCard, RefreshDatabaseButton, Navigate));
        if (initialized)
        {
            // Shared chrome keeps its original translation keys. Navigation labels belong
            // to the new presentation and are translated when its selection is updated.
            ApplyLanguage();
        }
    }

    private void SizeForDisplay()
    {
        var scale = Root.XamlRoot.RasterizationScale;
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)(1180 * scale), work.Width - 40);
        var height = Math.Min((int)(850 * scale), work.Height - 40);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Math.Min((int)(930 * scale), work.Width - 40);
            presenter.PreferredMinimumHeight = Math.Min((int)(620 * scale), work.Height - 40);
        }
        AppWindow.MoveAndResize(new RectInt32(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height));
    }

    private void ApplyAppearance()
    {
        if (closed) return;
        Root.RequestedTheme = preferences.Theme switch { "Light" => ElementTheme.Light, "System" => ElementTheme.Default, _ => ElementTheme.Dark };
        ApplyBackdrop();
        ApplyColors();
    }

    private void ApplyColors()
    {
        if (closed) return;
        palette = new(AccessibleTokens(ApplyWindowPreferenceTokens(DesignTokens.For(ActiveDesign, Root.ActualTheme == ElementTheme.Light, MaterialSeedForRender, preferences.MaterialColorStyle, MaterialSecondSeedForRender).WithBackdrop(SystemBackdrop is not null))));
        ApplyWindowPreferenceFonts();
        palette.InstallResources(Root.Resources);
        palette.ConfigureProgress(BusyProgress);
        palette.ConfigureProgress(OperationProgressBar);
        palette.ConfigureAction(RefreshDatabaseButton, ActionRole.Quiet, compact: ActiveDesign != "Material");
        palette.ConfigureAction(CancelOperationButton, ActionRole.Secondary, compact: true);
        ApplyPopupResources();
        Root.Background = SystemBackdrop is null ? Palette.Brush(palette.Shell) : new SolidColorBrush(Colors.Transparent);
        TitleBar.RequestedTheme = Root.RequestedTheme;
        presentation.ApplyShell(palette);
        BrandMark.Background = new SolidColorBrush(Colors.Transparent);
        BrandMark.CornerRadius = new(palette.Tokens.IconRadius);
        ConnectionDot.Fill = Palette.Brush(connected ? palette.Green : palette.Muted);
        StyleStatus.Text = palette.Tokens.Name + "  ·  PyDeck " + AppVersionLabel;
        ApplyWindowCaptionColors();
        UpdateOperationPanel();
        RenderPage();
        MarkMaterialColorsApplied();
        MarkSystemAppearanceApplied();
    }

    private bool CanApplyPreparedAppearance()
    {
        // Wallpaper updates may arrive at any time. Preserve in-progress editors and dialogs;
        // the next explicit navigation consumes the prepared palette without losing input.
        if (!initialized || closed ||
            confirmationOpen || closeDialogOpen || cancelDialogOpen || page is "settings" or "build" or "environments") return false;
        return FocusManager.GetFocusedElement(Root.XamlRoot) is not (TextBox or PasswordBox or RichEditBox or ComboBox or AutoSuggestBox);
    }

    private void ApplyPreparedMaterialColors()
    {
        if (ActiveDesign == "Material" && MaterialColorsPending && CanApplyPreparedAppearance()) ApplyAppearance();
    }

    private void Navigate(string destination)
    {
        if (destination == "settings" && page is not ("settings" or "components"))
        {
            settingsReturnPage = page;
            settingsDetailSelected = false;
        }
        page = destination;
        search = "";
        architecture = destination == "catalog" ? preferences.DefaultArchitecture : "All architectures";
        if (destination == "catalog") distributionFilter = preferences.CatalogPackageType ?? "Standard";
        if (SystemAppearancePending || MaterialColorsPending && ActiveDesign == "Material") ApplyAppearance();
        else RenderPage();
        if (destination == "catalog" && !OfflineSource && catalog is null && connected && CanWork(WorkKind.Catalog)) _ = LoadCatalogAsync();
    }
    private void RenderPage()
    {
        RememberSettingsScroll();
        CancelSearchRefresh();
        availability.Clear();
        settingsSections.Clear();
        RefreshDatabaseButton.IsEnabled = CanWork(WorkKind.Runtimes) && CanWork(WorkKind.Catalog);
        if (settingsScroll?.IsLoaded == true) settingsOffset = settingsScroll.VerticalOffset;
        if (buildScroll?.IsLoaded == true) buildOffset = buildScroll.VerticalOffset;
        buildScroll = null;
        settingsScroll = null;
        activityList = null; activityEmpty = null;
        runtimeRows = null;
        resultLabel = null;
        presentation.SelectPage(page, palette);
        PageHost.Children.Clear();
        PageHost.Children.Add(page switch
        {
            "build" => BuildPythonPage(), "catalog" => BuildCatalogPage(), "activity" => BuildActivityPage(),
            "settings" => BuildSettingsPage(), "environments" => BuildEnvironmentsPage(), "components" => BuildComponentGallery(), _ => BuildRuntimesPage()
        });
    }

    private async Task ConnectAsync()
    {
        if (confirmationOpen) return;
        using var work = StartWork(WorkKind.Connection);
        if (work is null) return;

        StatusText.Text = T("Connecting to Python Install Manager…");
        try
        {
            connected = await client.DiscoverAsync(preferences.ManagerPath);
            if (!connected) throw new InvalidOperationException(client.LastDiscoveryError);
            installed = await ListInstalledAsync();
            MessageBar.IsOpen = false;
            StatusText.Text = T("Up to date · {0}", DateTime.Now.ToString("t"));
            Log($"Connected. Found {installed.Count} installed Python runtimes.");
        }
        catch (Exception ex)
        {
            connected = false;
            installed = localRuntimes;
            ShowError(ex);
        }
        finally { UpdateConnection(); RefreshWorkPage("runtimes", "catalog"); }
    }

    private async Task RefreshInstalledAsync()
    {
        if (confirmationOpen) return;
        if (!connected) { await ConnectAsync(); return; }
        using var work = StartWork(WorkKind.Runtimes);
        if (work is null) return;

        StatusText.Text = T("Refreshing your Python versions…");
        try { installed = await ListInstalledAsync(); MessageBar.IsOpen = false; StatusText.Text = T("Up to date · {0}", DateTime.Now.ToString("t")); }
        catch (Exception ex) { ShowError(ex); }
        finally { UpdateConnection(); RefreshWorkPage("runtimes", "catalog"); }
    }

    private Task LoadCatalogAsync()
    {
        if (!CanWork(WorkKind.Catalog) || !connected || OfflineSource) return Task.CompletedTask;
        return catalogLoading = LoadCatalogCoreAsync();
    }
    private async Task LoadCatalogCoreAsync()
    {
        if (confirmationOpen) return;
        using var work = StartWork(WorkKind.Catalog);
        if (work is null) return;
        using var cancellation = new CancellationTokenSource();
        catalogCancellation = cancellation;
        StatusText.Text = T("Checking available Python releases…");
        try
        {
            catalog = await client.ListCatalogAsync(cancellationToken: cancellation.Token);
            Log($"Catalog refreshed. {catalog.Count} releases available.");
            StatusText.Text = T("Catalog updated · {0}", DateTime.Now.ToString("t"));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { ShowError(ex); }
        finally { catalogCancellation = null; RefreshWorkPage("catalog"); }
    }

    private async Task ChangeRuntimeAsync(RuntimeAction action, PythonRuntime runtime)
    {
        if (confirmationOpen) return;
        using var work = StartWork(WorkKind.RuntimeMutation);
        if (work is null) return;
        if (!connected || confirmationOpen || runtime.IsLocalBuild) return;
        string usageReview;
        int dependentEnvironments;
        try { usageReview = RuntimeUsageText(runtime); dependentEnvironments = RuntimeUsage.Find(runtime, Environments.Read()).Count; }
        catch (Exception ex) { ShowError(ex); return; }
        string? expectedInstalledVersion = null;
        if (action == RuntimeAction.Install)
        {
            try
            {
                installed = await ListInstalledAsync();
                var previous = installed.SingleOrDefault(r => r.Id.Equals(runtime.Id, StringComparison.OrdinalIgnoreCase));
                if (previous is not null && previous.Version != runtime.Version)
                {
                    if (await ShowGuardedDialogAsync(Dialog("Replace this Python version?",
                        T("Python {0} will be replaced with Python {1}. These versions share an installation folder", previous.Version, runtime.Version)
                        + "\n\n" + T("Packages in that interpreter may need to be reinstalled. Existing virtual environments may need attention"), "Replace")) != ContentDialogResult.Primary) return;
                    expectedInstalledVersion = previous.Version;
                }
            }
            catch (Exception ex) { ShowError(ex); return; }
        }
        if ((action == RuntimeAction.Uninstall && (preferences.ConfirmBeforeUninstall || dependentEnvironments > 0)) || action == RuntimeAction.Repair)
        {
            confirmationOpen = true;
            try
            {
                var dialog = action == RuntimeAction.Repair
                    ? Dialog("Reinstall to repair", T("PIM will replace this interpreter. Packages installed in its folder may be removed. Projects outside that folder are not removed") + "\n\n" + runtime.Prefix, "Repair")
                    : Dialog(T("Uninstall {0}?", RuntimeTitle(runtime)), T("This removes this Python runtime and its installed packages. Projects outside its installation folder are not removed.") + "\n\n" + runtime.Prefix + "\n\n" + usageReview, "Uninstall");
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }
            catch (Exception ex) { ShowError(ex); return; }
            finally { confirmationOpen = false; }
        }
        var verb = action switch { RuntimeAction.Install => "Installing", RuntimeAction.Update => "Updating", RuntimeAction.Repair => "Repair", _ => "Uninstalling" };
        MessageBar.IsOpen = false;
        var operation = action == RuntimeAction.Uninstall ? null : BeginOperation(T(action switch { RuntimeAction.Install => "Installing {0}…", RuntimeAction.Repair => "Repairing {0}…", _ => "Updating {0}…" }, RuntimeTitle(runtime)));
        StatusText.Text = T(action switch { RuntimeAction.Install => "Installing {0}…", RuntimeAction.Update => "Updating {0}…", RuntimeAction.Repair => "Repairing {0}…", _ => "Uninstalling {0}…" }, RuntimeTitle(runtime));
        Log($"{verb} {runtime.DisplayName} ({runtime.Id}).");
        try
        {
            var environmentLock = Path.Combine(store.DirectoryPath, "environments.lock"); SafeFiles.RequireNoLinks(environmentLock);
            using var environmentLease = new FileStream(environmentLock, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            if (RuntimeUsageText(runtime) != usageReview) throw new IOException("Environment usage changed; review the operation again");
            var result = await client.ChangeAsync(action, runtime, CreateOutputLogger(ActivityOrigin.PythonManager), operation, expectedInstalledVersion: expectedInstalledVersion);
            if (operation is not null) FinalizingOperation(operation);
            Log($"Process finished. Exit code: {result.ExitCode}.");
            await VerifyOperationAsync(action, client.LastExpectedRuntime ?? runtime);
        }
        catch (OperationCanceledException) when (operation?.IsCancellationRequested == true) { await ReconcileCancelledOperationAsync(target: runtime); }
        catch (Exception ex) { try { installed = await ListInstalledAsync(); } catch { installed = localRuntimes; } ShowError(ex); }
        finally { FinishOperation(operation); UpdateConnection(); RefreshWorkPage("runtimes", "catalog"); }
    }


    private void UpdateConnection()
    {
        ConnectionText.Text = T(connected ? "PIM connected" : "PIM not connected");
        ConnectionDot.Fill = Palette.Brush(connected ? palette.Green : palette.Muted);
        if (managerStateLabel?.IsLoaded == true) managerStateLabel.Text = T(connected ? "Connected on this computer" : "Not connected");
        if (managerPathLabel?.IsLoaded == true) managerPathLabel.Text = client.Executable ?? T("Not found. Choose a location or install Python Install Manager.");
    }
    private void Log(string message, ActivityLevel? level = null, ActivityOrigin origin = ActivityOrigin.Application)
    {
        string? secret = null; try { secret = ProxyPassword(); } catch { }
        activityLog.Add(SensitiveText.Redact(message, secret), level, origin);
        activityRefresh.Request();
    }
    private Action<string> CreateOutputLogger(ActivityOrigin origin = ActivityOrigin.Application)
    {
        // Capture credentials once for the operation; never access settings/credential
        // storage from every output line or send one dispatcher item per line.
        var secret = ProxyPassword();
        return message =>
        {
            activityLog.Add(SensitiveText.Redact(message, secret), origin: origin);
            activityRefresh.Request();
        };
    }
    private void Notify(string message, InfoBarSeverity severity)
    {
        MessageBar.Severity = severity;
        MessageBar.Title = T(severity switch { InfoBarSeverity.Error => "Something needs attention", InfoBarSeverity.Success => "All set", _ => "A quick note" });
        message = T(message);
        Log(message, severity switch { InfoBarSeverity.Error => ActivityLevel.Error, InfoBarSeverity.Warning => ActivityLevel.Warning, _ => ActivityLevel.Information });
        MessageBar.Message = message.Length > 700 ? message[..700] + "… " + T("See Activity for details.") : message;
        MessageBar.IsOpen = true;
    }
    private void ShowError(Exception ex)
    {
        var message = ex is OperationCanceledException ? "The request timed out. Check your connection and try again." : ex.Message;
        string? secret = null; try { secret = ProxyPassword(); } catch { }
        message = SensitiveText.Redact(message, secret);
        Notify(message, InfoBarSeverity.Error);
        StatusText.Text = T("Action needed · see Activity for details");
    }
    private ContentDialog Dialog(string title, string content, string? primary = null) => new()
    {
        XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme,
        Title = T(title), Content = new TextBlock { Text = T(content), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
        PrimaryButtonText = T(primary ?? ""), CloseButtonText = T(primary is null ? "OK" : "Cancel"),
        DefaultButton = ContentDialogButton.Close
    };
    private async Task<ContentDialogResult> ShowGuardedDialogAsync(ContentDialog dialog)
    {
        if (confirmationOpen || cancelDialogOpen || closeDialogOpen) return ContentDialogResult.None;
        confirmationOpen = true;
        try { return await dialog.ShowAsync(); }
        finally { confirmationOpen = false; }
    }
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (restartInProgress) { explicitWindowExit = false; exitAfterSettingsFlush = false; args.Cancel = true; return; }
        // Minimizing keeps the process and its operations alive. Only a real exit
        // must wait for active Python/build work to finish.
        if (!exitAfterSettingsFlush && TryApplyClosePreference()) { args.Cancel = true; return; }
        if (!RestartBlockedByWork)
        {
            if (exitAfterSettingsFlush) { exitAfterSettingsFlush = false; return; }
            args.Cancel = false;
            if (!args.Cancel && smokeDirectory is null && !store.FlushAsync().IsCompletedSuccessfully)
            {
                args.Cancel = true;
                await CloseAfterSettingsSavedAsync();
            }
            return;
        }
        explicitWindowExit = false;
        exitAfterSettingsFlush = false;
        args.Cancel = true;
        if (closeDialogOpen || confirmationOpen || cancelDialogOpen) return;
        closeDialogOpen = true;
        try { await Dialog("An operation is still running", buildingPython ? "You can stop the build with Cancel or follow its output in Build Python" : "Keep PyDeck open until the current operation finishes. You can follow its output in Activity.").ShowAsync(); }
        catch (Exception ex) { ShowError(ex); }
        finally { closeDialogOpen = false; }
    }
    private bool Copy(string text)
    {
        try { var package = new DataPackage(); package.SetText(text); Clipboard.SetContent(package); return true; }
        catch (Exception ex) { ShowError(ex); return false; }
    }
    private void OpenUrl(string url)
    {
        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
                !(new[] { "www.python.org", "docs.python.org", "learn.microsoft.com", "visualstudio.microsoft.com" }.Contains(uri.Host) ||
                  (uri.Host == "github.com" &&
                   (uri.AbsolutePath is "/DM10cn/PyDeck" or "/DM10cn/PyDeck/issues" or "/DM10cn/PyDeck/blob/main/LICENSE" or "/DM10cn/PyDeck/blob/main/THIRD-PARTY-NOTICES.md" ||
                    uri.AbsolutePath == "/DM10cn/PyDeck/releases" || uri.AbsolutePath.StartsWith("/DM10cn/PyDeck/releases/", StringComparison.Ordinal)) && uri.UserInfo.Length == 0 && uri.IsDefaultPort)))
                throw new ArgumentException("This link is not a supported documentation address.");
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void OpenFolder(PythonRuntime runtime)
    {
        try
        {
            var path = ExecutionPaths.LocalPath(runtime.Prefix);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("This installation folder is no longer available. Refresh the version list.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void OpenTerminal(PythonRuntime runtime)
    {
        try
        {
            ExecutionPaths.Runtime(runtime);
            // A constant PowerShell program plus environment values avoids interpreting paths as shell code.
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            { UseShellExecute = false, WorkingDirectory = runtime.Prefix };
            start.Environment["PATH"] = runtime.Prefix + Path.PathSeparator + Path.Combine(runtime.Prefix, "Scripts") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            start.Environment["PYDECK_PYTHON"] = runtime.Executable;
            start.ArgumentList.Add("-NoExit"); start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-Command"); start.ArgumentList.Add("function global:python { & $env:PYDECK_PYTHON @args }; Write-Host 'Python executable:' $env:PYDECK_PYTHON; Write-Host 'Use python to start this interpreter.'");
            Process.Start(start);
        }
        catch (Exception ex) { ShowError(ex); }
    }
}
