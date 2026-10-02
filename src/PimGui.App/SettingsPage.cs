using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;
using Microsoft.Win32;
using Windows.Storage.Pickers;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private TextBlock? managerStateLabel;
    private TextBlock? managerPathLabel;
    private InfoBar? designRestartNotice;
    private readonly Dictionary<string, Microsoft.UI.Xaml.Controls.Primitives.ToggleButton> designChoices = [];
    private UIElement BuildSettingsPage()
    {
        var layout = PageGrid(GridLength.Auto, new(1, GridUnitType.Star));
        At(layout, Header("MAKE IT YOURS", "Settings", "Appearance, language, and Python preferences."), 0);
        var body = new StackPanel { Spacing = palette.Tokens.SectionSpacing };
        var appearance = palette.Section("Appearance");
        designChoices.Clear();
        var choices = new Grid { ColumnSpacing = 8, MinWidth = 300, MaxWidth = 380 };
        choices.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); choices.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        foreach (var (id, title, column) in new[] { ("Material", "Material 3 Expressive", 0), ("Fluent", "Windows Fluent", 1) })
        {
            var previewTokens = DesignTokens.For(id, Root.ActualTheme == ElementTheme.Light, MaterialSeedForRender, preferences.MaterialColorStyle, MaterialSecondSeedForRender);
            var preview = new StackPanel { Spacing = 6 };
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            const double swatchHeight = 10;
            var swatchRadius = swatchHeight * previewTokens.ActionRadius / previewTokens.ControlHeight;
            foreach (var color in new[] { previewTokens.Accent, previewTokens.Hero, previewTokens.Card })
                swatches.Children.Add(new Border { Width = 24, Height = swatchHeight, CornerRadius = new(swatchRadius), Background = Palette.Brush(color) });
            preview.Children.Add(swatches);
            preview.Children.Add(palette.Label(T(title) + (preferences.Design == id ? "  ✓" : ""), 14, true));
            var button = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { Content = preview, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new(12, 8, 12, 8), MinHeight = 56, CornerRadius = new(palette.Tokens.ActionRadius), BorderThickness = new(preferences.Design == id ? 2 : 1),
                BorderBrush = Palette.Brush(preferences.Design == id ? palette.Accent : palette.Line), IsChecked = preferences.Design == id, IsEnabled = true };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, T(title));
            palette.ConfigureDesignChoice(button, preferences.Design == id);
            button.Click += async (_, _) => await SelectDesignAsync(id);
            designChoices.Add(id, button);
            Grid.SetColumn(button, column); choices.Children.Add(button);
        }
        appearance.Children.Add(SettingRow("Interface style", "Changes to interface style apply the next time PyDeck starts.", choices));
        designRestartNotice = new InfoBar { Severity = InfoBarSeverity.Informational, IsClosable = false,
            Title = T("Restart to apply interface style"), Message = T("Your selection is saved. Current tasks and the active interface will continue until you close PyDeck.") };
        var restart = palette.Action("Restart now", compact: true, role: ActionRole.Quiet);
        restart.Click += async (_, _) => await PromptDesignRestartAsync();
        designRestartNotice.ActionButton = restart;
        appearance.Children.Add(designRestartNotice);
        UpdateDesignSelection();
        appearance.Children.Add(SettingRow("App theme", null, Choice([("System", "System"), ("Light", "Light"), ("Dark", "Dark")], preferences.Theme,
            value => SaveAppearance(preferences.Design, value), "App theme")));
        appearance.Children.Add(SettingRow("Language", "Changes apply immediately.", Choice([("en-US", "English (US)"), ("zh-CN", "简体中文"), ("zh-TW", "繁體中文（台灣）"), ("ja-JP", "日本語")], preferences.Language,
            value => SavePreferences(preferences with { Language = value }), "Language")));

        var radios = new StackPanel { Spacing = 2 };
        foreach (var (id, label) in new[] { ("System", "Use Windows setting"), ("On", "On"), ("Off", "Off") })
        {
            var radio = new RadioButton { Content = T(label), GroupName = "Transparency", FontSize = palette.Tokens.ControlFontSize,
                MinHeight = palette.Tokens.ControlHeight, IsChecked = preferences.Transparency == id, IsEnabled = palette.Tokens.SupportsEffects };
            palette.ApplyAccentResources(radio);
            radio.Checked += (_, _) => SavePreferences(preferences with { Transparency = id });
            radios.Children.Add(radio);
        }
        appearance.Children.Add(SettingRow("Transparency effects", null, radios));
        var material = Choice([("Mica", "Mica"), ("Acrylic", "Acrylic")], preferences.Backdrop,
            value => SavePreferences(preferences with { Backdrop = value }), "Window material");
        material.IsEnabled = palette.Tokens.SupportsEffects && preferences.Transparency != "Off";
        appearance.Children.Add(SettingRow("Window material", "Mica uses your wallpaper colors. Acrylic blurs what is behind the window.", material));
        appearance.Children.Add(palette.Label(BackdropDescription(), palette.Tokens.CaptionFontSize, muted: true));
        var gallery = palette.Action("Component gallery", "\uE8A9", compact: true, role: ActionRole.Quiet);
        gallery.Click += (_, _) => Navigate("components");
        appearance.Children.Add(gallery);
        body.Children.Add(appearance);
        if (palette.Tokens.SupportsDynamicColor) body.Children.Add(MaterialColorSettings());

        var python = palette.Section("Python");
        python.Children.Add(SettingRow("Confirm before uninstall", null, Toggle(preferences.ConfirmBeforeUninstall,
            value => SavePreferences(preferences with { ConfirmBeforeUninstall = value }), "Confirm before uninstall")));
        body.Children.Add(python);
        body.Children.Add(ManagementSettings());
        body.Children.Add(StorageSettings());

        var manager = palette.Section("Python Install Manager location");
        manager.Tag = "ManagerLocation";
        managerStateLabel = palette.Label(connected ? "Connected on this computer" : "Not connected", 12, muted: true);
        manager.Children.Add(managerStateLabel);
        var path = palette.Label(client.Executable ?? T("Not found. Choose a location or install Python Install Manager."), 12, muted: true);
        managerPathLabel = path;
        path.IsTextSelectionEnabled = true; path.TextWrapping = TextWrapping.Wrap;
        manager.Children.Add(path);
        var browse = palette.Action("Change…", "\uE8B7", compact: true); BindAvailability(browse, () => CanWork(WorkKind.Connection));
        browse.Click += async (_, _) =>
        {
            if (!CanWork(WorkKind.Connection) || confirmationOpen) return;
            confirmationOpen = true;
            var ownsConfirmation = true;
            try
            {
                var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".exe");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var selected = await picker.PickSingleFileAsync();
                confirmationOpen = false; ownsConfirmation = false;
                if (selected is not null) await ChangeManagerAsync(selected.Path);
            }
            catch (Exception ex) { ShowError(ex); }
            finally { if (ownsConfirmation) confirmationOpen = false; }
        };
        var detect = palette.Action("Auto-detect", "\uE72C", compact: true); BindAvailability(detect, () => CanWork(WorkKind.Connection));
        detect.Click += async (_, _) => await ChangeManagerAsync("");
        var download = palette.Action("Download Python Install Manager", "\uE8A7", compact: true); download.HorizontalAlignment = HorizontalAlignment.Left; download.Click += (_, _) => OpenManagerDownload();
        manager.Children.Add(Toolbar(browse, detect));
        manager.Children.Add(download); body.Children.Add(manager);
        manager.Children.Add(palette.Label("After installing the manager, choose Auto-detect to connect without restarting PyDeck.", 12, muted: true));

        var about = palette.Section("About");
        about.Children.Add(palette.Label("PyDeck", 16, true));
        about.Children.Add(palette.Label(T("Version {0}", AppVersionLabel), 12, muted: true));
        about.Children.Add(palette.Label("A desktop companion for managing Python installations with Python Install Manager.", 13));
        about.Children.Add(palette.Label("Independent project. Not affiliated with the Python Software Foundation.", 12, muted: true));
        about.Children.Add(palette.Label("Built with WinUI 3. .NET and Windows App Runtime are installed separately.", 12, muted: true));
        about.Children.Add(palette.Label("No analytics. Preferences stay on this computer; activity logs stay in this session.", 12, muted: true));
        var update = palette.Action("Check PyDeck updates", "\uE895", compact: true); BindAvailability(update, () => CanWork(WorkKind.AppUpdate));
        update.Click += async (_, _) => await CheckAppUpdateAsync();
        var releases = palette.Action("GitHub releases", "\uE8A7", compact: true); releases.Click += (_, _) => OpenUrl(AppUpdates.ReleasesPage);
        var docs = palette.Action("Python documentation", "\uE8A7", compact: true); docs.HorizontalAlignment = HorizontalAlignment.Left; docs.Click += (_, _) => OpenUrl("https://docs.python.org/3/using/windows.html");
        about.Children.Add(Toolbar(update, releases));
        about.Children.Add(docs); body.Children.Add(about);
        settingsScroll = PageScroll(body);
        settingsScroll.Loaded += (sender, _) => ((ScrollViewer)sender).ChangeView(null, settingsOffset, null, true);
        At(layout, settingsScroll, 1);
        return layout;
    }

    private Grid SettingRow(string title, string? description, FrameworkElement control)
        => presentation.SettingRow(palette, title, description, control);
    private ComboBox Choice((string Id, string Label)[] options, string selected, Action<string> changed, string name)
    {
        var box = new ComboBox { MinWidth = 168, MinHeight = palette.Tokens.ControlHeight, FontSize = palette.Tokens.ControlFontSize,
            Padding = new(12, 4, 32, 4), IsEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        palette.ApplySurfaceResources(box);
        foreach (var option in options) box.Items.Add(new ComboBoxItem { Content = T(option.Label), Tag = option.Id });
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, T(name));
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is ComboBoxItem { Tag: string value }) changed(value); };
        return box;
    }
    private ToggleSwitch Toggle(bool value, Action<bool> changed, string name)
    {
        var toggle = new ToggleSwitch { IsOn = value, OnContent = T("On"), OffContent = T("Off"), IsEnabled = true, MinWidth = 112 };
        palette.ApplyAccentResources(toggle);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, T(name));
        toggle.Toggled += (_, _) => changed(toggle.IsOn);
        return toggle;
    }
    private void SavePreferences(AppSettings changed)
    {
        try
        {
            changed = changed.Normalize();
            var previous = preferences;
            store.Save(changed); preferences = changed;
            if (page == "catalog")
            {
                architecture = changed.DefaultArchitecture;
                distributionFilter = changed.CatalogPackageType!;
            }
            if (previous.Language != changed.Language) ApplyLanguage();
            var materialColorsChanged = previous.MaterialColorSource != changed.MaterialColorSource ||
                previous.MaterialSeed != changed.MaterialSeed || previous.MaterialSecondSeed != changed.MaterialSecondSeed || previous.MaterialColorStyle != changed.MaterialColorStyle;
            if (materialColorsChanged) RefreshMaterialColors();
            if (materialColorsChanged || previous.Theme != changed.Theme ||
                previous.Transparency != changed.Transparency || previous.Backdrop != changed.Backdrop) ApplyAppearance();
            else if ((previous with { Design = changed.Design }) == changed) UpdateDesignSelection();
            else RenderPage();
        }
        catch (Exception ex) { UpdateDesignSelection(); ShowError(ex); }
    }
    private void SaveAppearance(string design, string theme) => SavePreferences(preferences with { Design = design, Theme = theme });

    private void UpdateDesignSelection()
    {
        foreach (var (id, button) in designChoices)
        {
            palette.ConfigureDesignChoice(button, preferences.Design == id);
            if (button.Content is StackPanel preview && preview.Children.LastOrDefault() is TextBlock label)
                label.Text = T(id == "Fluent" ? "Windows Fluent" : "Material 3 Expressive") + (preferences.Design == id ? "  ✓" : "");
        }
        if (designRestartNotice is not null) designRestartNotice.IsOpen = preferences.Design != ActiveDesign;
    }

    private async Task ChangeManagerAsync(string path)
    {
        using var work = StartWork(WorkKind.Connection);
        if (work is null) return;

        StatusText.Text = T("Connecting to Python Install Manager…");
        try
        {
            // Validate before committing the preference or discarding the old connection.
            var candidate = CreateClient();
            if (!await candidate.DiscoverAsync(path)) throw new InvalidOperationException(candidate.LastDiscoveryError);
            var versions = await candidate.ListAsync();
            var changed = preferences with { ManagerPath = path }; store.Save(changed);
            preferences = changed; client = candidate; installed = MergeLocal(versions); catalog = null; connected = true;
            MessageBar.IsOpen = false; StatusText.Text = T("Connected on this computer");
            Log("Connected to " + candidate.Executable);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { UpdateConnection(); RefreshWorkPage("runtimes", "catalog"); }
    }

    private async Task SetDefaultAsync(PythonRuntime runtime)
    {
        if (confirmationOpen) return;
        using var work = StartWork(WorkKind.Configuration);
        if (work is null) return;
        if (runtime.IsLocalBuild || !connected) return;
        StatusText.Text = T("Setting {0} as default…", runtime.DisplayName);
        try
        {
            using var lease = client.AcquireConfigurationLock();
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PYTHON_MANAGER_CONFIG")) ||
                !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PYTHON_MANAGER_DEFAULT")))
                throw new InvalidOperationException("An environment variable overrides your PIM configuration. Update PYTHON_MANAGER_CONFIG / PYTHON_MANAGER_DEFAULT before changing the default here.");
            using var policy = Registry.LocalMachine.OpenSubKey(@"Software\Policies\Python\PyManager");
            if (policy?.GetValueNames().Any(name => new[] { "default_tag", "base_config", "user_config", "additional_config" }.Contains(name, StringComparer.OrdinalIgnoreCase)) == true)
                throw new InvalidOperationException("Your PIM configuration is controlled by an administrator. Change the default through your administrator's configuration.");
            var current = (await client.ListAsync()).SingleOrDefault(r => r.Id == runtime.Id && r.Selector == runtime.Selector);
            if (current is null) throw new InvalidOperationException("This Python entry has changed. Refresh the list before trying again.");
            var config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Python", "pymanager.json");
            var backup = PimConfiguration.Save(PimConfiguration.Read(config), new System.Text.Json.Nodes.JsonObject { ["default_tag"] = runtime.Selector });
            Log($"Saved default_tag = {runtime.Selector}." + (backup.Length > 0 ? $" Backup: {backup}" : ""));
            installed = await ListInstalledAsync();
            var active = installed.FirstOrDefault(r => r.IsDefault);
            if (active?.Id == runtime.Id)
            {
                var health = await RuntimeHealth.CheckAsync(active);
                Notify(health.Healthy ? T("{0} is now your default.", runtime.DisplayName) : T(health.Message), health.Healthy ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
                StatusText.Text = T(health.Healthy ? "Default interpreter updated" : "Result not confirmed");
            }
            else Notify("Your preference was saved, but PIM reports a different effective default. A custom configuration or policy may override it.", InfoBarSeverity.Warning);
        }
        catch (Exception ex) { try { installed = await ListInstalledAsync(); } catch { installed = localRuntimes; } ShowError(ex); }
        finally { UpdateConnection(); RefreshWorkPage("runtimes", "catalog"); }
    }

}
