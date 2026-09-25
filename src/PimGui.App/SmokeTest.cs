using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PimGui.Core;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task RunSmokeTestAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        try
        {
            if (!connected) throw new InvalidOperationException("PIM was not connected: " + client.LastDiscoveryError);
            checks.Add($"PIM connected; {installed.Count} installed runtimes loaded.");
            connected = false;
            foreach (var language in Strings.Languages)
            {
                Strings.Language = language;
                Navigate("runtimes"); RenderPage(); Root.UpdateLayout();
                var setup = Descendants(PageHost).OfType<StackPanel>().Single(panel => panel.Tag as string == "ManagerSetup");
                if (!setup.Children.OfType<Button>().Any(button => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == T("Download Python Install Manager")) ||
                    !setup.Children.OfType<Button>().Any(button => button.Tag as string == "ReconnectManager" && button.IsEnabled))
                    throw new InvalidOperationException("Missing manager recovery actions: " + language);
            }
            Strings.Language = preferences.Language;
            await ChangeManagerAsync("");
            if (!connected) throw new InvalidOperationException("Manager reconnect failed without app restart");
            checks.Add("Four-language missing-manager download / reconnect actions; reconnect succeeds without app restart.");
            if (preferences.Language != "en-US") throw new InvalidOperationException("English is not the default language.");
            await CheckNotificationTextAsync(directory);
            checks.Add("Notification titles and messages render in four languages and three severity states.");
            await CheckRuleEntryAndActivityAsync(directory);
            checks.Add("Four-language rule entry rejects blanks inline and accepts all interpreter targets; activity filters explicit severity, live output and navigation without duplicate errors.");
            await CheckCancelledInlineEditsAsync(directory);
            checks.Add("PIM and Shebang reviews can be cancelled and reopened without losing drafts; invalid source validation preserves input; user PIM configuration remains unchanged.");
            Navigate("runtimes");
            await CaptureAsync(Path.Combine(directory, "01-material-dark-runtimes.png"));
            if (BrandMark.Child is not Image { Source: BitmapImage { PixelWidth: > 0 } } ||
                Descendants(TitleBar).OfType<Image>().Any() ||
                !File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")))
                throw new InvalidOperationException("Selected app icon was not loaded.");
            checks.Add("Sidebar icon decoded; title bar has no redundant icon; Windows ICO is present.");
            search = "no-such-python-version-19d7"; PopulateRuntimes();
            if (VisibleRuntimeCount != 0) throw new InvalidOperationException("Runtime search did not filter the list.");
            checks.Add("Runtime search: no-match state verified.");
            search = ""; PopulateRuntimes();
            await LoadCatalogAsync();
            if (catalog is null || catalog.Count == 0) throw new InvalidOperationException("The online catalog did not load.");
            page = "catalog"; architecture = "x64"; RenderPage();
            await CaptureAsync(Path.Combine(directory, "02-material-dark-catalog.png"));
            checks.Add($"Online catalog: {catalog.Count} releases; {VisibleRuntimeCount} stable x64 results.");
            var distributions = Descendants(PageHost).OfType<ComboBox>().Single(box =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(box) == T("Package type"));
            if (distributionFilter != "Standard") throw new InvalidOperationException("Catalog must default to standard packages");
            distributions.SelectedItem = distributions.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == "All");
            Root.UpdateLayout();
            if (distributionFilter != "All" || VisibleRuntimeCount != RuntimeCatalog.Filter(catalog, "x64", false, "").Count)
                throw new InvalidOperationException("Package type filter did not include specialized packages");
            var releaseSeries = Descendants(PageHost).OfType<Expander>().Where(e => e.Tag is string tag && tag.StartsWith("CatalogSeries:")).ToArray();
            if (releaseSeries.Length == 0 || releaseSeries.Any(series => Descendants(series).OfType<Expander>().Any()))
                throw new InvalidOperationException("Catalog series must have a single expandable level");
            releaseSeries[0].IsExpanded = true;
            await CaptureAsync(Path.Combine(directory, "02b-all-distributions.png"));
            checks.Add("Recommended stable release, global package-type filter and single-level minor series verified.");
            Navigate("settings");
            await CaptureAsync(Path.Combine(directory, "03-material-dark-settings.png"));
            var managerSection = Descendants(PageHost).OfType<StackPanel>().Single(panel => panel.Tag as string == "ManagerLocation");
            if (Descendants(managerSection).OfType<TextBox>().Any(box => !box.IsReadOnly)) throw new InvalidOperationException("Manager settings exposes an editable path.");
            if (Descendants(PageHost).OfType<RadioButton>().Any(radio => radio.IsEnabled)) throw new InvalidOperationException("Material transparency controls are not disabled.");
            await ScrollSettingsAsync(bottom: true);
            await CaptureAsync(Path.Combine(directory, "03b-python-settings-about.png"));
            await ScrollSettingsAsync();
            SavePreferences(preferences with { ShowPreviewReleases = true, CatalogPackageType = "All", DefaultArchitecture = "ARM64" });
            Navigate("catalog"); Root.UpdateLayout();
            if (architecture != "ARM64" || VisibleRuntimeCount != RuntimeCatalog.Filter(catalog, "ARM64", true, "").Count)
                throw new InvalidOperationException("Saved catalog filters did not affect the catalog.");
            if (distributionFilter != "All") throw new InvalidOperationException("Package-type preference was not applied.");
            SavePreferences(preferences with { ShowPreviewReleases = false, CatalogPackageType = "Standard", DefaultArchitecture = "x64" });
            await CheckCatalogFilterPreferencesAsync(directory);
            checks.Add("Four-language catalog architecture, package type and preview controls persist across navigation and profile reload; live changes preserve focus; My Python filters stay independent; manager location is read-only.");
            await CheckManagementSettingsAsync(directory);
            checks.Add("Four-language inline management settings, read-only PATH diagnostics, virtual environments, runtime badges, catalog grouping and actual byte progress rendered without saving user settings.");
            await CheckPageLayoutsAsync(directory);
            checks.Add("Four languages and both designs at normal/compact widths: shared header geometry, action metrics, settings rows, environment actions, structured activity and historical micro identities verified.");
            SaveAppearance("Fluent", "Dark"); Navigate("runtimes"); architecture = "All architectures"; RenderPage();
            await CaptureAsync(Path.Combine(directory, "04-fluent-dark-runtimes.png"));
            SavePreferences(preferences with { Transparency = "Off" });
            if (SystemBackdrop is not null || PopupBrush() is not SolidColorBrush) throw new InvalidOperationException("Off retained a transparent surface.");
            if (palette.Surface.A != 255 || palette.Card.A != 255) throw new InvalidOperationException("Off retained translucent content.");
            await CaptureAsync(Path.Combine(directory, "04b-fluent-off.png"));
            Navigate("settings");
            await ScrollSettingsAsync();
            await CaptureAsync(Path.Combine(directory, "04c-fluent-settings.png"));
            Navigate("runtimes");
            SavePreferences(preferences with { Transparency = "On", Backdrop = "Acrylic" });
            if (backdropKind == "Acrylic" && SystemBackdrop is not DesktopAcrylicBackdrop) throw new InvalidOperationException("Acrylic request not applied.");
            await CheckBackdropPersistenceAsync("Acrylic", directory);
            SavePreferences(preferences with { Backdrop = "Mica" });
            if (backdropKind == "Mica" && SystemBackdrop is not MicaBackdrop) throw new InvalidOperationException("Mica request not applied.");
            await CheckBackdropPersistenceAsync("Mica", directory);
            checks.Add("Acrylic / Mica retain the same native controller across language, architecture, toggle and theme changes; content/card layers are translucent.");
            SavePreferences(preferences with { Transparency = "System" });
            checks.Add("Fluent Off, On / Acrylic, On / Mica, and Use Windows setting applied; compositor visuals require manual inspection.");
            SaveAppearance("Fluent", "Light");
            await CaptureAsync(Path.Combine(directory, "04d-fluent-light.png"));
            SaveAppearance("Material", "Light");
            if (SystemBackdrop is not null || PopupBrush() is not SolidColorBrush) throw new InvalidOperationException("Material retained a backdrop.");
            if (palette.Surface.A != 255 || palette.Card.A != 255) throw new InvalidOperationException("Material retained translucent content.");
            await CaptureAsync(Path.Combine(directory, "05-material-light-runtimes.png"));
            checks.Add("Style changed to Fluent and back to Material; light and dark themes rendered.");
            SaveAppearance("Material", "Dark"); Navigate("activity");
            await CaptureAsync(Path.Combine(directory, "06-activity.png"));
            checks.Add("Activity navigation rendered.");
            await CheckOperationPanelAsync(directory);
            checks.Add("Four-language stage progress persists across navigation; cancel confirmation can be dismissed or accepted; stopping disables repeated cancellation.");
            foreach (var language in new[] { "zh-CN", "zh-TW", "ja-JP" })
            {
                Navigate("settings"); Root.UpdateLayout();
                await ScrollSettingsAsync();
                var languageChoice = Descendants(PageHost).OfType<ComboBox>().Single(box => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(box) == T("Language"));
                languageChoice.SelectedItem = languageChoice.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == language);
                Root.UpdateLayout();
                if (!Descendants(PageHost).OfType<TextBlock>().Any(label => label.Text == T("App theme"))) throw new InvalidOperationException("Settings not localized: " + language);
                if (!shellLabels.Any(pair => pair.Key == "My Python" && pair.Element.Text == T("My Python"))) throw new InvalidOperationException("Navigation not localized: " + language);
                await CaptureAsync(Path.Combine(directory, "07-settings-" + language + ".png"));
                await ScrollSettingsAsync(bottom: true);
                await CaptureAsync(Path.Combine(directory, "07b-settings-about-" + language + ".png"));
                await ScrollSettingsAsync();
                Navigate("runtimes");
                await CaptureAsync(Path.Combine(directory, "08-runtimes-" + language + ".png"));
                checks.Add("Live language switch and localized settings / navigation rendered: " + language);
            }
            var arguments = Environment.GetCommandLineArgs();
            var fixtureArgument = Array.IndexOf(arguments, "--offline-fixture");
            if (fixtureArgument >= 0 && fixtureArgument + 1 < arguments.Length)
            {
                SavePreferences(preferences with { Language = "en-US" });
                var pendingCatalog = LoadCatalogAsync();
                await SwitchCatalogSourceAsync("Offline"); await pendingCatalog;
                if (busy || catalogCancellation is not null || MessageBar.IsOpen) throw new InvalidOperationException("Online lookup did not cancel cleanly when switching offline.");
                Navigate("catalog");
                var savedPackageType = preferences.CatalogPackageType;
                await LoadOfflineFolderAsync(arguments[fixtureArgument + 1]);
                if (offlineBundle is null || distributionFilter != savedPackageType || store.Load().CatalogPackageType != savedPackageType)
                    throw new InvalidOperationException("Opening an offline bundle changed the package-type preference.");
                Root.UpdateLayout();
                var offlineType = Descendants(PageHost).OfType<ComboBox>().Single(box =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(box) == T("Package type"));
                offlineType.SelectedItem = offlineType.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == "All");
                if (VisibleRuntimeCount == 0) throw new InvalidOperationException("Offline catalog failed to load.");
                foreach (var language in Strings.Languages)
                {
                    SavePreferences(preferences with { Language = language });
                    await CaptureAsync(Path.Combine(directory, "12-offline-" + language + ".png"));
                }
                if (store.Load().CatalogSource != "Offline") throw new InvalidOperationException("Offline preference not saved.");
                checks.Add("Offline bundle loaded with real metadata; four languages rendered; source preference persisted.");
                await SwitchCatalogSourceAsync("Online");
            }
            SavePreferences(preferences with { Language = "en-US", Design = "Fluent", Transparency = "Off" }); Navigate("runtimes");
            AppWindow.Resize(new Windows.Graphics.SizeInt32(2560, 1600));
            await CaptureAsync(Path.Combine(directory, "09-fluent-2560x1600.png"));
            if (ContentColumn.ActualWidth > palette.Tokens.ContentWidth + 1) throw new InvalidOperationException("Wide window content is not constrained.");
            var scale = Root.XamlRoot.RasterizationScale;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(930 * scale), (int)(620 * scale)));
            await CaptureAsync(Path.Combine(directory, "10-fluent-compact.png"));
            checks.Add("Wide 2560 × 1600 and compact window layouts rendered; content width remains bounded.");
            SavePreferences(preferences with { Language = "ja-JP" }); Navigate("settings");
            await ScrollSettingsAsync();
            await CaptureAsync(Path.Combine(directory, "11-japanese-compact-settings.png"));
            SavePreferences(preferences with { Design = "Material", Theme = "Dark" });
            SavePreferences(preferences with { Language = "en-US" });
            var persisted = store.Load();
            if (persisted.Theme != "Dark" || persisted.Design != "Material" || persisted.Language != "en-US") throw new InvalidOperationException("Preferences did not persist.");
            checks.Add("Smoke-only preferences round-trip passed; user preferences were not modified.");
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = false, checks, error = ex.ToString(), preferences, uiMessage = MessageBar.Message, recentActivity = activityLog.Entries.TakeLast(8).ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Close(); }
    }

    private async Task CheckOperationPanelAsync(string directory)
    {
        foreach (var language in Strings.Languages)
        {
            SavePreferences(preferences with { Language = language }); Navigate("catalog");
            var operation = BeginOperation(T("Installing {0}…", "Python 3.14.7"));
            SetBusy(true);
            operation.Observe(new("Downloading: " + new string('.', 33), false));
            UpdateOperationPanel(); Root.UpdateLayout();
            if (OperationPanel.Visibility != Visibility.Visible || OperationProgressBar.IsIndeterminate || OperationProgressBar.Value != 50 || !CancelOperationButton.IsEnabled)
                throw new InvalidOperationException("Live progress UI not applied: " + language);
            if (OperationPhaseText.Text != T("{0} · about {1}%", T("Downloading"), 50))
                throw new InvalidOperationException("Progress label not localized: " + language);
            await CaptureAsync(Path.Combine(directory, "13-progress-" + language + ".png"));
            Navigate("activity");
            if (OperationPanel.Visibility != Visibility.Visible || OperationProgressBar.Value != 50)
                throw new InvalidOperationException("Progress disappeared on navigation.");
            if (language == "en-US")
            {
                InvokeButton(CancelOperationButton);
                var dialog = await WaitForCancellationDialogAsync();
                if (dialog.DefaultButton != ContentDialogButton.Close) throw new InvalidOperationException("Stop dialog has unsafe default.");
                InvokeButton(Descendants(dialog).OfType<Button>().Single(b => b.Content as string == T("Cancel")));
                await WaitForSmokeConditionAsync(() => !cancelDialogOpen, "Stop confirmation did not close.");
                if (operation.IsCancellationRequested) throw new InvalidOperationException("Dismissing confirmation stopped the installation.");
                InvokeButton(CancelOperationButton);
                dialog = await WaitForCancellationDialogAsync();
                InvokeButton(Descendants(dialog).OfType<Button>().Single(b => b.Content as string == T("Stop installation")));
                await WaitForSmokeConditionAsync(() => operation.IsCancellationRequested && !cancelDialogOpen, "Stop confirmation did not finish.");
                if (!operation.IsCancellationRequested || CancelOperationButton.IsEnabled || OperationPhaseText.Text != T("Stopping…"))
                    throw new InvalidOperationException("Stop confirmation did not cancel exactly this operation.");
                await CaptureAsync(Path.Combine(directory, "14-stopping.png"));
                await ReconcileCancelledOperationAsync(); // Read-only real PIM refresh; no runtime was changed by this UI probe.
            }
            else
            {
                operation.Report(OperationPhase.Verifying); UpdateOperationPanel();
                if (!OperationProgressBar.IsIndeterminate) throw new InvalidOperationException("Unknown progress must be indeterminate.");
                FinalizingOperation(operation);
                if (CancelOperationButton.IsEnabled) throw new InvalidOperationException("Completed process still has an enabled stop button.");
            }
            FinishOperation(operation); SetBusy(false); MessageBar.IsOpen = false;
            if (OperationPanel.Visibility != Visibility.Collapsed) throw new InvalidOperationException("Finished progress panel remained visible.");
        }
        SavePreferences(preferences with { Language = "en-US" });
    }
    private async Task CheckManagementSettingsAsync(string directory)
    {
        foreach (var language in Strings.Languages)
        {
            SavePreferences(preferences with { Language = language }); Navigate("settings");
            foreach (var title in new[] { "Network", "PIM configuration", "Installation source", "Shebang rules" })
            {
                expandedSettings.Clear(); expandedSettings.Add(title); RenderPage(); Root.UpdateLayout();
                var section = Descendants(PageHost).OfType<Expander>().Single(e => e.Tag as string == "Management:" + title);
                if (!section.IsExpanded || section.Content is not StackPanel) throw new IOException("Inline settings not rendered: " + title);
                if (title == "Network" && !Descendants(section).OfType<PasswordBox>().Any()) throw new IOException("Protected credential UI missing");
                if (title == "PIM configuration" && Descendants(section).OfType<ComboBox>().Count() < 4) throw new IOException("Configuration choices missing");
                await CaptureAsync(Path.Combine(directory, "15-" + title.Replace(" ", "-") + "-" + language + ".png"), section);
            }
            Navigate("environments");
            await CaptureAsync(Path.Combine(directory, "16-environments-" + language + ".png"));
            var operation = BeginOperation("Download", download: true);
            operation.Transfer(2 * 1024 * 1024, 4 * 1024 * 1024, 1024 * 1024, TimeSpan.FromSeconds(2));
            UpdateOperationPanel();
            if (!OperationPhaseText.Text.Contains("MB") || OperationProgressBar.Value != 50) throw new IOException("Measured transfer not displayed");
            FinishOperation(operation);
        }
        SavePreferences(preferences with { Language = "en-US" });
        lastPathReport = await PathDiagnostics.ProbeKnownAsync(PathDiagnostics.Inspect(installed, client.Executable), installed, client.Executable);
        expandedSettings.Clear(); expandedSettings.Add("PATH and aliases"); Navigate("settings"); Root.UpdateLayout();
        await CaptureAsync(Path.Combine(directory, "17-path-diagnostics.png"), Descendants(PageHost).OfType<Expander>().Single(e => e.Tag as string == "Management:PATH and aliases"));
        expandedSettings.Clear();
        var sample = installed.First();
        var gallery = new StackPanel { Spacing = 8, Width = 480 };
        foreach (var r in new[] { sample, sample with { Version = "3.15.0rc1" }, sample with { Company = "PythonEmbed" }, sample with { Tag = "3.14t-64" }, sample with { Company = "PythonTest", Version = "3.15.0b1" } })
        {
            var icon = RuntimeIcon(r);
            if (Descendants(icon).OfType<Border>().Any(b => b.Tag as string == "EapBadge") != r.IsPrerelease) throw new IOException("EAP badge mismatch");
            gallery.Children.Add(icon);
        }
        PageHost.Children.Clear(); PageHost.Children.Add(gallery);
        await CaptureAsync(Path.Combine(directory, "18-runtime-icons.png"));
        Navigate("catalog"); distributionFilter = "All"; RenderPage(); Root.UpdateLayout();
        var filter = Descendants(PageHost).OfType<ComboBox>().Single(box =>
            Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(box) == T("Package type"));
        filter.SelectedItem = filter.Items.Cast<ComboBoxItem>().Single(i => i.Tag as string == "Embedded");
        var series = Descendants(PageHost).OfType<Expander>().First(e => e.Tag is string tag && tag.StartsWith("CatalogSeries:")); series.IsExpanded = true; Root.UpdateLayout();
        if (!Descendants(series).OfType<TextBlock>().Any(t => t.Text.Contains("Embeddable", StringComparison.OrdinalIgnoreCase))) throw new IOException("Distribution filter did not populate versions");
        await CaptureAsync(Path.Combine(directory, "19-filtered-minor-series.png"));
        SavePreferences(preferences with { CatalogPackageType = "Standard" });
    }
    private async Task<ContentDialog> WaitForCancellationDialogAsync(string title = "Stop this installation?")
    {
        ContentDialog? dialog = null;
        await WaitForSmokeConditionAsync(() =>
        {
            dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot)
                .SelectMany(popup => new[] { popup.Child }.Concat(Descendants(popup.Child)))
                .OfType<ContentDialog>().SingleOrDefault(item => item.Title as string == T(title));
            return dialog is not null;
        }, "Stop confirmation did not open.");
        return dialog!;
    }
    private async Task WaitForSmokeConditionAsync(Func<bool> condition, string message)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(8))
        {
            Root.UpdateLayout();
            if (condition()) return;
            await Task.Delay(50);
        }
        throw new InvalidOperationException($"{message} Cancellation dialog: {cancelDialogOpen}; other confirmation: {confirmationOpen}; UI message: {MessageBar.Message}");
    }
    private static void InvokeButton(Button button)
    {
        var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button);
        ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
    }

    private async Task ScrollSettingsAsync(bool bottom = false)
    {
        // Wait for the new ScrollViewer to load before changing its view (ChangeView otherwise returns false).
        Root.UpdateLayout(); await Task.Delay(100);
        settingsOffset = bottom ? settingsScroll!.ScrollableHeight : 0;
        settingsScroll!.ChangeView(null, settingsOffset, null, true);
        await Task.Delay(200);
        if (Math.Abs(settingsScroll.VerticalOffset - settingsOffset) > 2)
            throw new InvalidOperationException("Settings scroll view did not reach the requested position.");
    }

    private async Task CheckBackdropPersistenceAsync(string material, string directory)
    {
        if (SystemBackdrop is null) throw new InvalidOperationException(material + " is unavailable on this smoke-test host.");
        var controller = SystemBackdrop;
        Navigate("settings"); Root.UpdateLayout();
        var language = Descendants(PageHost).OfType<ComboBox>().Single(box => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(box) == T("Language"));
        language.SelectedItem = language.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == "zh-CN");
        Root.UpdateLayout();
        Navigate("catalog"); Root.UpdateLayout();
        var preview = Descendants(PageHost).OfType<ToggleSwitch>().Single(toggle => Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(toggle) == T("Show preview releases"));
        preview.IsOn = !preview.IsOn; Root.UpdateLayout();
        SavePreferences(preferences with { DefaultArchitecture = "ARM64", CatalogPackageType = "All", ConfirmBeforeUninstall = false });
        SavePreferences(preferences with { Theme = "Light" });
        Root.UpdateLayout();
        if (!ReferenceEquals(controller, SystemBackdrop)) throw new InvalidOperationException(material + " controller replaced by unrelated settings.");
        if (((SolidColorBrush)PageSurface.Background).Color.A == 255 || ((SolidColorBrush)ConnectionCard.Background).Color.A == 255 || palette.Card.A == 255)
            throw new InvalidOperationException(material + " covered by an opaque content layer.");
        SavePreferences(preferences with { Language = "en-US", Theme = "Dark", DefaultArchitecture = "x64", ShowPreviewReleases = false, CatalogPackageType = "Standard", ConfirmBeforeUninstall = true });
        Navigate("settings");
        await ScrollSettingsAsync();
        await CaptureAsync(Path.Combine(directory, "04-effects-" + material + ".png"));
        if (!ReferenceEquals(controller, SystemBackdrop)) throw new InvalidOperationException(material + " controller replaced after restoring settings.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private async Task CaptureAsync(string path, UIElement? element = null)
    {
        Root.UpdateLayout();
        await Task.Delay(500);
        var target = new RenderTargetBitmap();
        await target.RenderAsync(element ?? Root);
        if (target.PixelWidth == 0 || target.PixelHeight == 0) throw new InvalidOperationException("The window did not render.");
        var pixels = await target.GetPixelsAsync();
        using var reader = DataReader.FromBuffer(pixels);
        var bytes = new byte[pixels.Length]; reader.ReadBytes(bytes);
        var file = await StorageFile.GetFileFromPathAsync(CreateFile(path));
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)target.PixelWidth, (uint)target.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
    }
    private static string CreateFile(string path) { File.WriteAllBytes(path, []); return path; }
}
