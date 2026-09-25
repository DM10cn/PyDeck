using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;
using System.Diagnostics;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace PimGui.App;

public sealed partial class MainWindow : Window
{
    private PimClient client = new(new ProcessRunner());
    private readonly SettingsStore store;
    private AppSettings preferences;
    private Palette palette = new(DesignTokens.For("Material", false));
    private IReadOnlyList<PythonRuntime> installed = [];
    private IReadOnlyList<PythonRuntime>? catalog;
    private CancellationTokenSource? catalogCancellation;
    private Task catalogLoading = Task.CompletedTask;
    private OfflineBundle? offlineBundle;
    private bool OfflineSource => preferences.CatalogSource == "Offline";
    private readonly ActivityLog activityLog = new();
    private ActivityLevel? activityFilter;
    private string page = "runtimes";
    private string search = "";
    private string architecture = "All architectures";
    private bool busy;
    private bool initialized;
    private bool connected;
    private bool closeDialogOpen;
    private bool confirmationOpen;
    private StackPanel? runtimeRows;
    private TextBlock? resultLabel;
    private ScrollViewer? settingsScroll;
    private double settingsOffset;
    private readonly string? smokeDirectory;
    internal int VisibleRuntimeCount { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        var args = Environment.GetCommandLineArgs();
        var smokeIndex = Array.IndexOf(args, "--smoke-test");
        if (smokeIndex >= 0 && smokeIndex + 1 < args.Length) smokeDirectory = Path.GetFullPath(args[smokeIndex + 1]);
        store = new(smokeDirectory is null
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PyDeck")
            : Path.Combine(smokeDirectory, "preferences"));
        preferences = store.Load();
        if (smokeDirectory is null && !File.Exists(store.FilePath))
            preferences = new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PimGui")).Load();
        client = CreateClient();
        InitializeSystemAppearance();
        ExtendsContentIntoTitleBar = true;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        SetTitleBar(TitleBar);
        AppWindow.Resize(new SizeInt32(1200, 820));
        AppWindow.Closing += OnClosing;
        Root.Loaded += async (_, _) =>
        {
            if (initialized) return;
            initialized = true;
            CollectShellLabels(Root);
            ApplyLanguage();
            SizeForDisplay();
            ApplyAppearance();
            await ConnectAsync();
            if (store.LoadWarning is { } warning) Notify(warning, InfoBarSeverity.Warning);
            if (smokeDirectory is not null) await RunSmokeTestAsync(smokeDirectory);
        };
        Root.ActualThemeChanged += (_, _) => { if (initialized) ApplyColors(); };
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
        Root.RequestedTheme = preferences.Theme switch { "Light" => ElementTheme.Light, "System" => ElementTheme.Default, _ => ElementTheme.Dark };
        ApplyBackdrop();
        ApplyColors();
    }

    private void ApplyColors()
    {
        palette = new(AccessibleTokens(DesignTokens.For(preferences.Design, Root.ActualTheme == ElementTheme.Light).WithBackdrop(SystemBackdrop is not null)));
        ApplyPopupResources();
        Root.Background = SystemBackdrop is null ? Palette.Brush(palette.Shell) : new SolidColorBrush(Colors.Transparent);
        TitleBar.RequestedTheme = Root.RequestedTheme;
        PageSurface.Background = Palette.Brush(palette.Surface);
        PageSurface.CornerRadius = new(palette.Tokens.SurfaceRadius);
        ContentColumn.MaxWidth = palette.Tokens.ContentWidth;
        BrandMark.Background = new SolidColorBrush(Colors.Transparent);
        BrandMark.CornerRadius = new(palette.Tokens.IconRadius);
        ConnectionCard.Background = new SolidColorBrush(Colors.Transparent);
        ConnectionCard.CornerRadius = new(palette.Tokens.IconRadius);
        ConnectionDot.Fill = Palette.Brush(connected ? palette.Green : palette.Muted);
        StyleStatus.Text = palette.Tokens.Name + "  ·  PyDeck " + typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonForegroundColor = palette.Text;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = palette.Muted;
        UpdateOperationPanel();
        RenderPage();
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string destination }) Navigate(destination);
    }
    private void Navigate(string destination)
    {
        page = destination;
        search = "";
        architecture = destination == "catalog" ? preferences.DefaultArchitecture : "All architectures";
        if (destination == "catalog") distributionFilter = preferences.CatalogPackageType ?? "Standard";
        RenderPage();
        if (destination == "catalog" && !OfflineSource && catalog is null && connected && !busy) _ = LoadCatalogAsync();
    }
    private void RenderPage()
    {
        RefreshDatabaseButton.IsEnabled = !busy && !confirmationOpen;
        if (settingsScroll?.IsLoaded == true) settingsOffset = settingsScroll.VerticalOffset;
        settingsScroll = null;
        activityList = null; activityEmpty = null;
        runtimeRows = null;
        resultLabel = null;
        foreach (var button in new[] { RuntimesNav, CatalogNav, EnvironmentsNav, ActivityNav, SettingsNav })
        {
            bool selected = (string)button.Tag == page;
            button.Background = selected ? Palette.Brush(palette.Tokens.NavigationSelected) : new SolidColorBrush(Colors.Transparent);
            button.Foreground = Palette.Brush(selected ? palette.Accent : palette.Muted);
            button.BorderThickness = new(selected ? palette.Tokens.NavigationIndicator : 0, 0, 0, 0);
            button.BorderBrush = Palette.Brush(palette.Accent);
            button.CornerRadius = new(palette.Tokens.NavigationRadius);
            button.Padding = new(12, 8, 12, 8); button.MinHeight = 40; button.FontSize = palette.Tokens.ControlFontSize;
            button.FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
        PageHost.Children.Clear();
        PageHost.Children.Add(page switch
        {
            "catalog" => BuildCatalogPage(), "activity" => BuildActivityPage(),
            "settings" => BuildSettingsPage(), "environments" => BuildEnvironmentsPage(), _ => BuildRuntimesPage()
        });
    }

    private async Task ConnectAsync()
    {
        if (busy) return;
        SetBusy(true, "Connecting to Python Install Manager…");
        try
        {
            connected = await client.DiscoverAsync(preferences.ManagerPath);
            if (!connected) throw new InvalidOperationException(client.LastDiscoveryError);
            installed = await client.ListAsync();
            MessageBar.IsOpen = false;
            StatusText.Text = T("Up to date · {0}", DateTime.Now.ToString("t"));
            Log($"Connected. Found {installed.Count} installed Python runtimes.");
        }
        catch (Exception ex)
        {
            connected = false;
            installed = [];
            ShowError(ex);
        }
        finally { SetBusy(false); UpdateConnection(); RenderPage(); }
    }

    private async Task RefreshInstalledAsync()
    {
        if (busy) return;
        if (!connected) { await ConnectAsync(); return; }
        SetBusy(true, "Refreshing your Python versions…");
        try { installed = await client.ListAsync(); MessageBar.IsOpen = false; StatusText.Text = T("Up to date · {0}", DateTime.Now.ToString("t")); }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); UpdateConnection(); RenderPage(); }
    }

    private Task LoadCatalogAsync()
    {
        if (busy || !connected || OfflineSource) return Task.CompletedTask;
        return catalogLoading = LoadCatalogCoreAsync();
    }
    private async Task LoadCatalogCoreAsync()
    {
        using var cancellation = new CancellationTokenSource();
        catalogCancellation = cancellation;
        SetBusy(true, "Checking available Python releases…");
        try
        {
            catalog = await client.ListCatalogAsync(cancellationToken: cancellation.Token);
            Log($"Catalog refreshed. {catalog.Count} releases available.");
            StatusText.Text = T("Catalog updated · {0}", DateTime.Now.ToString("t"));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { ShowError(ex); }
        finally { catalogCancellation = null; SetBusy(false); RenderPage(); }
    }

    private async Task ChangeRuntimeAsync(RuntimeAction action, PythonRuntime runtime)
    {
        if (busy || !connected || confirmationOpen) return;
        string? expectedInstalledVersion = null;
        if (action == RuntimeAction.Install)
        {
            confirmationOpen = true;
            try
            {
                installed = await client.ListAsync();
                var previous = installed.SingleOrDefault(r => r.Id.Equals(runtime.Id, StringComparison.OrdinalIgnoreCase));
                if (previous is not null && previous.Version != runtime.Version)
                {
                    if (await Dialog("Replace this Python version?",
                        T("Python {0} will be replaced with Python {1}. These versions share an installation folder", previous.Version, runtime.Version)
                        + "\n\n" + T("Packages in that interpreter may need to be reinstalled. Existing virtual environments may need attention"), "Replace").ShowAsync() != ContentDialogResult.Primary) return;
                    expectedInstalledVersion = previous.Version;
                }
            }
            catch (Exception ex) { ShowError(ex); return; }
            finally { confirmationOpen = false; }
        }
        if ((action == RuntimeAction.Uninstall && preferences.ConfirmBeforeUninstall) || action == RuntimeAction.Repair)
        {
            confirmationOpen = true;
            try
            {
                var dialog = action == RuntimeAction.Repair
                    ? Dialog("Reinstall to repair", T("PIM will replace this interpreter. Packages installed in its folder may be removed. Projects outside that folder are not removed") + "\n\n" + runtime.Prefix, "Repair")
                    : Dialog(T("Uninstall {0}?", RuntimeTitle(runtime)), T("This removes this Python runtime and its installed packages. Projects outside its installation folder are not removed.") + "\n\n" + runtime.Prefix, "Uninstall");
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || busy) return;
            }
            catch (Exception ex) { ShowError(ex); return; }
            finally { confirmationOpen = false; }
        }
        var verb = action switch { RuntimeAction.Install => "Installing", RuntimeAction.Update => "Updating", RuntimeAction.Repair => "Repair", _ => "Uninstalling" };
        MessageBar.IsOpen = false;
        var operation = action == RuntimeAction.Uninstall ? null : BeginOperation(T(action switch { RuntimeAction.Install => "Installing {0}…", RuntimeAction.Repair => "Repairing {0}…", _ => "Updating {0}…" }, RuntimeTitle(runtime)));
        SetBusy(true, T(action switch { RuntimeAction.Install => "Installing {0}…", RuntimeAction.Update => "Updating {0}…", RuntimeAction.Repair => "Repairing {0}…", _ => "Uninstalling {0}…" }, RuntimeTitle(runtime)));
        Log($"{verb} {runtime.DisplayName} ({runtime.Id}).");
        try
        {
            var result = await client.ChangeAsync(action, runtime, line => DispatcherQueue.TryEnqueue(() => Log(line, origin: ActivityOrigin.PythonManager)), operation, expectedInstalledVersion: expectedInstalledVersion);
            if (operation is not null) FinalizingOperation(operation);
            Log($"Process finished. Exit code: {result.ExitCode}.");
            await VerifyOperationAsync(action, client.LastExpectedRuntime ?? runtime);
        }
        catch (OperationCanceledException) when (operation?.IsCancellationRequested == true) { await ReconcileCancelledOperationAsync(target: runtime); }
        catch (Exception ex) { try { installed = await client.ListAsync(); } catch { installed = []; } ShowError(ex); }
        finally { FinishOperation(operation); SetBusy(false); UpdateConnection(); RenderPage(); }
    }

    private void SetBusy(bool value, string? status = null)
    {
        busy = value;
        UpdateOperationPanel();
        if (status is not null) StatusText.Text = T(status);
        RenderPage();
    }
    private void UpdateConnection()
    {
        ConnectionText.Text = T(connected ? "PIM connected" : "PIM not connected");
        ConnectionDot.Fill = Palette.Brush(connected ? palette.Green : palette.Muted);
    }
    private void Log(string message, ActivityLevel? level = null, ActivityOrigin origin = ActivityOrigin.Application)
    {
        string? secret = null; try { secret = ProxyPassword(); } catch { }
        activityLog.Add(SensitiveText.Redact(message, secret), level, origin);
        RefreshActivityOutput();
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
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!busy) return;
        args.Cancel = true;
        if (closeDialogOpen || confirmationOpen || cancelDialogOpen) return;
        closeDialogOpen = true;
        try { await Dialog("An operation is still running", "Keep PyDeck open until the current operation finishes. You can follow its output in Activity.").ShowAsync(); }
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
                !(new[] { "www.python.org", "docs.python.org", "learn.microsoft.com" }.Contains(uri.Host) ||
                  (uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/DM10cn/PyDeck/releases", StringComparison.Ordinal) && uri.UserInfo.Length == 0 && uri.IsDefaultPort)))
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
