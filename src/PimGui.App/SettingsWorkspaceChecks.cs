using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;
using System.Text.Json;
using Windows.Foundation;
using Windows.Graphics;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private static readonly string[] SmokeSettingsCategories = ["appearance", "interface", "python", "network", "storage", "about"];

    private void OpenSettingsCategoryForSmoke(string category)
    {
        Navigate("settings"); SelectSettingsCategory(category); Root.UpdateLayout();
    }

    private FrameworkElement SettingsElement(string tag) => Descendants(PageHost).OfType<FrameworkElement>()
        .Single(element => element.Tag as string == tag);

    private static bool SettingsElementVisible(FrameworkElement element)
    {
        if (!element.IsLoaded || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        for (DependencyObject? ancestor = element; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
            if (ancestor is UIElement visual && (visual.Visibility != Visibility.Visible || visual.Opacity <= 0)) return false;
        return true;
    }

    private void RequireSettingsWorkspaceGeometry(string context)
    {
        var workspace = SettingsElement("SettingsWorkspace");
        var navigation = SettingsElement("SettingsCategoryNavigation");
        var detail = SettingsElement("SettingsDetail");
        var back = SettingsElement("SettingsBack");
        var narrow = workspace.ActualWidth < 900;
        if (!SettingsElementVisible(workspace) || !SettingsElementVisible(back)) throw new IOException("Settings workspace/back is not visible: " + context);
        RequireHorizontalBounds(workspace, PageHost, context);
        RequireHorizontalBounds(back, workspace, context);
        var showNavigation = !narrow || !settingsDetailSelected;
        var showDetail = !narrow || settingsDetailSelected;
        if (SettingsElementVisible(navigation) != showNavigation || SettingsElementVisible(detail) != showDetail)
            throw new IOException($"Settings category/detail visibility disagrees with its width and navigation state: {context}; width={workspace.ActualWidth:F1}, selected={settingsDetailSelected}");
        if (showNavigation)
        {
            RequireHorizontalBounds(navigation, workspace, context);
            foreach (var id in SmokeSettingsCategories)
            {
                var item = SettingsElement("SettingsCategory:" + id);
                if (!SettingsElementVisible(item) || item.ActualHeight < 32 || string.IsNullOrWhiteSpace(AutomationProperties.GetName(item)))
                    throw new IOException("Settings category lacks a visible accessible target: " + context + "/" + id);
                RequireHorizontalBounds(item, navigation, context + "/" + id);
                foreach (var label in Descendants(item).OfType<TextBlock>().Where(SettingsElementVisible))
                    if (label.IsTextTrimmed) throw new IOException("Settings category label is trimmed: " + context + "/" + id);
            }
        }
        if (showDetail)
        {
            RequireHorizontalBounds(detail, workspace, context);
            if (settingsScroll is null || !SettingsElementVisible(settingsScroll) || settingsScroll.ViewportHeight <= 0 ||
                settingsScroll.ViewportHeight > detail.ActualHeight + 1)
                throw new IOException("Settings detail lacks a finite scroll viewport: " + context);
            RequireHorizontalBounds(settingsScroll, detail, context);
            foreach (var row in Descendants(detail).OfType<Grid>().Where(row => row.Tag as string == "SettingsRow" && SettingsElementVisible(row)))
            {
                RequireHorizontalBounds(row, settingsScroll, context);
                foreach (var child in row.Children.OfType<FrameworkElement>().Where(SettingsElementVisible)) RequireHorizontalBounds(child, row, context);
            }
        }
        if (!narrow)
        {
            var left = navigation.TransformToVisual(workspace).TransformBounds(new Rect(0, 0, navigation.ActualWidth, navigation.ActualHeight));
            var right = detail.TransformToVisual(workspace).TransformBounds(new Rect(0, 0, detail.ActualWidth, detail.ActualHeight));
            if (left.Right > right.Left + 1 || Math.Abs(left.Top - right.Top) > 2)
                throw new IOException("Wide settings navigation and detail overlap or fail to align: " + context);
        }
        if (ActiveDesign == "Material" && Descendants(ShellHost).OfType<Control>().Any(control =>
            (control.Tag as string is "runtimes" or "catalog" or "environments" or "build" or "activity") && SettingsElementVisible(control)))
            throw new IOException("Material settings retained visible primary navigation: " + context);
    }

    private async Task SelectSettingsCategoryThroughAutomationAsync(string category)
    {
        var workspace = SettingsElement("SettingsWorkspace");
        if (workspace.ActualWidth < 900 && settingsDetailSelected) await BackFromSettingsThroughAutomationAsync();
        var item = SettingsElement("SettingsCategory:" + category);
        await WaitForSmokeConditionAsync(() => SettingsElementVisible(item), "Settings category did not load: " + category);
        if (item is not Control control || !control.IsEnabled || !control.Focus(FocusState.Keyboard))
            throw new IOException("Settings category cannot receive keyboard focus: " + category);
        var peer = FrameworkElementAutomationPeer.FromElement(item) ?? FrameworkElementAutomationPeer.CreatePeerForElement(item)
            ?? throw new IOException("Settings category lacks a native automation peer: " + category);
        if (peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selection) selection.Select();
        else if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke) invoke.Invoke();
        else throw new IOException("Settings category lacks native select/invoke semantics: " + category);
        await WaitForSmokeConditionAsync(() => settingsCategory == category && settingsDetailSelected && settingsScroll?.IsLoaded == true,
            "Settings category selection did not open its detail: " + category);
        Root.UpdateLayout();
    }

    private async Task BackFromSettingsThroughAutomationAsync()
    {
        var back = SettingsElement("SettingsBack") as Button ?? throw new IOException("Settings back action is not a native button");
        await WaitForSmokeConditionAsync(() => back.IsLoaded && back.IsEnabled, "Settings back action did not load");
        InvokeButton(back); Root.UpdateLayout(); await Task.Delay(60); Root.UpdateLayout();
    }

    private async Task CheckSettingsWorkspacesAsync(string directory, List<string> checks)
    {
        if (smokeDirectory is null) throw new IOException("Settings checks require an isolated smoke profile");
        var original = preferences; var originalPage = page; var originalCategory = settingsCategory;
        var originalDetailSelected = settingsDetailSelected; var originalSize = AppWindow.Size;
        var originalExpanded = expandedSettings.ToArray();
        var measurements = new List<object>(); var passed = false;
        try
        {
            // Avoid switching the native presentation during the first Loaded callback.
            await Task.Delay(250);
            var scenarios = (from design in new[] { "Fluent", "Material" }
                             from theme in new[] { "Light", "Dark" }
                             from width in new[] { 820, 1180 }
                             select (design, theme, width, language: "en-US"))
                .Concat(from design in new[] { "Fluent", "Material" }
                        from language in Strings.Languages.Where(language => language != "en-US")
                        select (design, theme: "Dark", width: 820, language))
                .Concat(from design in new[] { "Fluent", "Material" }
                        select (design, theme: "Dark", width: 1680, language: "zh-CN"));
            foreach (var scenario in scenarios)
            {
                Navigate("activity");
                ApplySmokePreferences(preferences with { Design = scenario.design, Theme = scenario.theme, Language = scenario.language,
                    Transparency = "Off", OledBlack = false, MaterialColorSource = "Custom", MaterialColorStyle = "TonalSpot" });
                var scale = Root.XamlRoot.RasterizationScale;
                AppWindow.Resize(new SizeInt32((int)(scenario.width * scale), (int)(850 * scale)));
                Navigate("settings"); Root.UpdateLayout(); await Task.Delay(120); Root.UpdateLayout();
                var workspace = SettingsElement("SettingsWorkspace");
                var narrow = workspace.ActualWidth < 900;
                if (narrow != (scenario.width == 820)) throw new IOException("Settings fixture did not reach its requested width class");
                var context = $"{scenario.design}/{scenario.theme}/{scenario.language}/{scenario.width}";
                if (narrow && settingsDetailSelected) await BackFromSettingsThroughAutomationAsync();
                RequireSettingsWorkspaceGeometry(context + "/categories");
                if (narrow) await CaptureAsync(Path.Combine(directory, $"settings-{scenario.design}-{scenario.theme}-{scenario.language}-{scenario.width}-categories.png"));
                // Select a different initial item: SelectionItem.Select on an already
                // selected native item intentionally does not generate a fresh click.
                foreach (var category in new[] { "interface", "appearance", "python", "network", "storage", "about" })
                {
                    await SelectSettingsCategoryThroughAutomationAsync(category);
                    RequireSettingsWorkspaceGeometry(context + "/" + category);
                    RequireSettingsCategoryContent(category);
                    RequireCatalogFiltersAbsentFromSettings();
                    var screenshot = $"settings-{scenario.design}-{scenario.theme}-{scenario.language}-{scenario.width}-{category}.png";
                    if (category is "appearance" or "interface" or "about") await CaptureAsync(Path.Combine(directory, screenshot));
                    else screenshot = "";
                    measurements.Add(new { context, category, actualWorkspaceWidthDip = workspace.ActualWidth,
                        actualWorkspaceHeightDip = workspace.ActualHeight, rasterizationScale = scale, textScaleFactor = systemUi.TextScaleFactor,
                        narrow, viewportHeightDip = settingsScroll!.ViewportHeight, screenshot });
                }
                if (narrow)
                {
                    await BackFromSettingsThroughAutomationAsync();
                    if (page != "settings" || settingsDetailSelected) throw new IOException("Narrow detail back did not return to categories");
                    var selected = SettingsElement("SettingsCategory:about");
                    var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
                    if (!ReferenceEquals(selected, focused) && !Descendants(selected).Any(child => ReferenceEquals(child, focused)))
                        throw new IOException("Narrow settings back lost the selected category's keyboard focus");
                }
                await BackFromSettingsThroughAutomationAsync();
                if (page != "activity") throw new IOException("Settings back did not restore the entering page");
            }
            foreach (var design in new[] { "Fluent", "Material" })
            {
                ApplySmokePreferences(preferences with { Design = design, Theme = "Dark", Language = "en-US", Transparency = "Off" });
                var scale = Root.XamlRoot.RasterizationScale;
                AppWindow.Resize(new SizeInt32((int)(1180 * scale), (int)(700 * scale)));
                await CheckSettingsDraftAndScrollAsync();
                await CheckSettingsPreferenceControlsAsync();
                measurements.Add(await CheckRapidSettingsTogglesAsync());
            }
            passed = true;
            checks.Add("Settings workspace: six native-accessible categories, wide split view and narrow category/detail navigation, independent Material shell, geometry, localized content and back focus passed in both designs; 16 layout/language scenarios, including 1680 DIP Chinese wide windows.");
            checks.Add("Settings category changes preserve the Network editor instance/draft and appearance scroll; real controls save startup, close, titlebar, font and OLED preferences. Rapid OLED then titlebar toggles retain both saved values and the black canvas. Smoke close interception remains disabled; native tray/restart/startup execution is not exercised.");
        }
        finally
        {
            await store.FlushAsync();
            await File.WriteAllTextAsync(Path.Combine(directory, "settings-workspace.json"), JsonSerializer.Serialize(new
            {
                passed, measurements,
                scope = "In-process native UI Automation and rendered layout; no network actions, process restart, native caption restart, tray lifecycle or startup relaunch. Screenshots capture the XAML root, not compositor materials."
            }, new JsonSerializerOptions { WriteIndented = true }));
            expandedSettings.Clear(); foreach (var title in originalExpanded) expandedSettings.Add(title);
            AppWindow.Resize(originalSize); ApplySmokePreferences(original);
            settingsCategory = originalCategory; settingsDetailSelected = originalDetailSelected; Navigate(originalPage); Root.UpdateLayout();
        }
    }

    private void RequireSettingsCategoryContent(string category)
    {
        var detail = SettingsElement("SettingsDetail");
        bool Named(string name) => Descendants(detail).OfType<Control>().Any(control => AutomationProperties.GetName(control) == T(name));
        foreach (var (owner, name) in new[] { ("appearance", "App theme"), ("interface", "Language"), ("python", "Confirm before uninstall"), ("network", "Network") })
            if (Named(name) != (owner == category)) throw new IOException($"Settings content leaked or is missing: {category}/{name}");
        if (category == "appearance" && Descendants(detail).OfType<FrameworkElement>().Any(element => element.Tag as string == "MaterialColorSettings") != (ActiveDesign == "Material"))
            throw new IOException("Dynamic color settings do not match the selected presentation");
        if (category == "python" && !Descendants(detail).OfType<FrameworkElement>().Any(element => element.Tag as string == "ManagerLocation"))
            throw new IOException("Python settings lacks manager location");
        if (category == "storage" && !Descendants(detail).OfType<Expander>().Any()) throw new IOException("Storage settings lacks its management section");
        if (category == "about")
        {
            var text = Descendants(detail).OfType<TextBlock>().Select(label => label.Text).ToArray();
            if (!text.Any(value => value.Contains("PyDeck", StringComparison.Ordinal)) || !text.Any(value => value.Contains(AppVersionLabel, StringComparison.Ordinal)))
                throw new IOException("About page lacks product identity or actual application version");
            foreach (var name in new[] { "Check for updates", "Release notes", "Source code", "Report an issue", "MIT license", "Third-party notices" })
                if (!Named(name)) throw new IOException("About page lacks its action: " + name);
        }
    }

    private async Task CheckSettingsDraftAndScrollAsync()
    {
        expandedSettings.Add("Network"); OpenSettingsCategoryForSmoke("network");
        var section = settingsSections["Network"].Section; section.IsExpanded = true; Root.UpdateLayout();
        var draft = Descendants(section).OfType<TextBox>().First();
        const string value = "http://settings-unsaved.invalid:8123"; draft.Text = value;
        SelectSettingsCategory("appearance"); Root.UpdateLayout();
        await WaitForSmokeConditionAsync(() => settingsScroll?.IsLoaded == true, "Appearance scroll did not load");
        var scroll = settingsScroll!;
        if (scroll.ScrollableHeight <= 0) throw new IOException("Settings scroll-retention fixture did not overflow");
        var target = Math.Min(160, scroll.ScrollableHeight); scroll.ChangeView(null, target, null, true);
        await WaitForSmokeConditionAsync(() => Math.Abs(scroll.VerticalOffset - target) < 2, "Settings scroll did not reach the fixture position");
        SelectSettingsCategory("network"); Root.UpdateLayout();
        if (!ReferenceEquals(section, settingsSections["Network"].Section) || draft.Text != value || !draft.IsLoaded)
            throw new IOException("Category navigation replaced the unsaved Network editor");
        SelectSettingsCategory("appearance"); Root.UpdateLayout();
        await WaitForSmokeConditionAsync(() => ReferenceEquals(scroll, settingsScroll) && Math.Abs(scroll.VerticalOffset - target) < 2,
            "Returning to a category lost its scroll control or offset");
    }

    private async Task CheckSettingsPreferenceControlsAsync()
    {
        ComboBox ChoiceNamed(string name) => Descendants(SettingsElement("SettingsDetail")).OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == T(name));
        void Choose(string name, string id)
        {
            var box = ChoiceNamed(name); box.SelectedItem = box.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == id); Root.UpdateLayout();
        }
        async Task ToggleNamed(string name, Func<AppSettings, bool> read)
        {
            var toggle = Descendants(SettingsElement("SettingsDetail")).OfType<ToggleSwitch>().Single(item => AutomationProperties.GetName(item) == T(name));
            var expected = !read(preferences);
            await WaitForSmokeConditionAsync(() => toggle.IsLoaded && toggle.IsEnabled, "Settings toggle did not load: " + name);
            var peer = FrameworkElementAutomationPeer.FromElement(toggle) ?? FrameworkElementAutomationPeer.CreatePeerForElement(toggle);
            ((IToggleProvider)peer.GetPattern(PatternInterface.Toggle)).Toggle();
            await store.FlushAsync();
            await WaitForSmokeConditionAsync(() => pendingToggleSaves == 0 && read(preferences) == expected && read(store.Load()) == expected,
                "Settings toggle failed to persist or finish its appearance update: " + name);
            Root.UpdateLayout();
        }
        OpenSettingsCategoryForSmoke("interface");
        Choose("Startup page", "activity");
        foreach (var behavior in new[] { "Exit", "Minimize", "Tray" })
        {
            Choose("When closing the window", behavior); await store.FlushAsync();
            if (store.Load().CloseBehavior != behavior || TryApplyClosePreference()) throw new IOException("Close preference did not persist or intercepted the isolated probe");
        }
        if (store.Load().StartupPage != "activity") throw new IOException("Startup-page choice did not persist");
        var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter;
        var originalTitleMode = presenter.HasTitleBar;
        var originalBorderMode = presenter.HasBorder;
        await ToggleNamed("Use system title bar", settings => settings.UseSystemTitleBar);
        if (presenter.HasTitleBar != originalTitleMode || presenter.HasBorder != originalBorderMode ||
            ExtendsContentIntoTitleBar || TitleBar.Visibility != Visibility.Collapsed || Root.RowDefinitions[0].ActualHeight > 0)
            throw new IOException("Native frame changed before restart or custom titlebar reappeared");
        SelectSettingsCategory("appearance"); Root.UpdateLayout();
        await ToggleNamed("Use system font", settings => settings.UseSystemFont);
        await WaitForSmokeConditionAsync(() => preferences.UseSystemFont ? palette.Tokens.FontFamily == FontFamily.XamlAutoFontFamily.Source : palette.Tokens.FontFamily.StartsWith("Segoe UI Variable", StringComparison.Ordinal),
            "Font preference did not update the rendered palette");
        if (!preferences.OledBlack) await ToggleNamed("OLED optimization", settings => settings.OledBlack);
        if (!IsHighContrast)
        {
            await WaitForSmokeConditionAsync(() => palette.Surface == Microsoft.UI.Colors.Black, "OLED did not produce a black dark-mode canvas");
            if (palette.Card == palette.Surface) throw new IOException("OLED erased the distinction between canvas and cards");
        }
        Choose("App theme", "Light");
        await WaitForSmokeConditionAsync(() => Root.ActualTheme == ElementTheme.Light && (IsHighContrast || palette.Surface != Microsoft.UI.Colors.Black), "OLED changed the light-mode canvas to black");
    }

    private async Task<object> CheckRapidSettingsTogglesAsync()
    {
        ApplySmokePreferences(preferences with { Theme = "Dark", OledBlack = false });
        OpenSettingsCategoryForSmoke("interface");
        var titlebar = Descendants(PageHost).OfType<ToggleSwitch>().Single(toggle => AutomationProperties.GetName(toggle) == T("Use system title bar"));
        await WaitForSmokeConditionAsync(() => titlebar.IsLoaded, "Rapid-toggle titlebar fixture did not load");
        var titlePeer = FrameworkElementAutomationPeer.FromElement(titlebar) ?? FrameworkElementAutomationPeer.CreatePeerForElement(titlebar);
        var titleProvider = (IToggleProvider)titlePeer.GetPattern(PatternInterface.Toggle);
        var expectedTitlebar = !preferences.UseSystemTitleBar;
        SelectSettingsCategory("appearance"); Root.UpdateLayout();
        var oled = Descendants(PageHost).OfType<ToggleSwitch>().Single(toggle => AutomationProperties.GetName(toggle) == T("OLED optimization"));
        await WaitForSmokeConditionAsync(() => oled.IsLoaded && Root.ActualTheme == ElementTheme.Dark, "Rapid-toggle OLED fixture did not load in dark mode");
        var oledPeer = FrameworkElementAutomationPeer.FromElement(oled) ?? FrameworkElementAutomationPeer.CreatePeerForElement(oled);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        ((IToggleProvider)oledPeer.GetPattern(PatternInterface.Toggle)).Toggle();
        // No await between inputs: the non-appearance save can supersede the pending
        // OLED save before its dispatcher continuation updates the rendered palette.
        SelectSettingsCategory("interface"); Root.UpdateLayout();
        if (!Descendants(PageHost).Any(element => ReferenceEquals(element, titlebar)))
            throw new IOException("Rapid-toggle fixture lost its cached interface control");
        titleProvider.Toggle(); elapsed.Stop();
        await store.FlushAsync();
        await WaitForSmokeConditionAsync(() => pendingToggleSaves == 0 && preferences.OledBlack && preferences.UseSystemTitleBar == expectedTitlebar &&
            (IsHighContrast || palette.Surface == Microsoft.UI.Colors.Black), "Rapid settings toggles lost their final value or pending OLED appearance");
        var persisted = store.Load();
        if (!persisted.OledBlack || persisted.UseSystemTitleBar != expectedTitlebar || !IsHighContrast && palette.Card == palette.Surface)
            throw new IOException("Rapid OLED/titlebar changes were not both saved or erased the card hierarchy");
        return new { context = ActiveDesign + "/rapid-toggles", inputSequence = "OLED off to on, then system-titlebar toggle, without awaiting between inputs",
            inputSequenceMilliseconds = elapsed.Elapsed.TotalMilliseconds, oled = persisted.OledBlack, useSystemTitleBar = persisted.UseSystemTitleBar,
            surface = palette.Surface.ToString(), card = palette.Card.ToString(), highContrast = IsHighContrast };
    }
}
