using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;
using Microsoft.Win32;
using Windows.Storage.Pickers;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private UIElement BuildSettingsPage()
    {
        var layout = PageGrid(GridLength.Auto, new(1, GridUnitType.Star));
        At(layout, Header("MAKE IT YOURS", "Settings", "Appearance, language, and Python preferences."), 0);
        var body = new StackPanel { Spacing = 16 };
        var appearance = new StackPanel { Spacing = 18 };
        appearance.Children.Add(palette.Label("Appearance", 19, true));
        var choices = new Grid { ColumnSpacing = 12 };
        choices.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); choices.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        foreach (var (id, title, column) in new[] { ("Material", "Material 3 Expressive", 0), ("Fluent", "Windows Fluent", 1) })
        {
            var previewTokens = DesignTokens.For(id, Root.ActualTheme == ElementTheme.Light);
            var preview = new StackPanel { Spacing = 10 };
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var color in new[] { previewTokens.Accent, previewTokens.Hero, previewTokens.Card })
                swatches.Children.Add(new Border { Width = 38, Height = 18, CornerRadius = new(previewTokens.ChipRadius), Background = Palette.Brush(color) });
            preview.Children.Add(swatches);
            preview.Children.Add(palette.Label(T(title) + (preferences.Design == id ? "  ✓" : ""), 14, true));
            var button = new Button { Content = preview, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch,
                Padding = new(16), CornerRadius = new(palette.Radius), BorderThickness = new(preferences.Design == id ? 2 : 1),
                BorderBrush = Palette.Brush(preferences.Design == id ? palette.Accent : palette.Line), IsEnabled = !busy };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, T(title));
            palette.ApplySurfaceResources(button);
            button.Click += (_, _) => SaveAppearance(id, preferences.Theme);
            Grid.SetColumn(button, column); choices.Children.Add(button);
        }
        appearance.Children.Add(choices);
        appearance.Children.Add(SettingRow("App theme", null, Choice([("System", "System"), ("Light", "Light"), ("Dark", "Dark")], preferences.Theme,
            value => SaveAppearance(preferences.Design, value), "App theme")));
        appearance.Children.Add(SettingRow("Language", "Changes apply immediately.", Choice([("en-US", "English (US)"), ("zh-CN", "简体中文"), ("zh-TW", "繁體中文（台灣）"), ("ja-JP", "日本語")], preferences.Language,
            value => SavePreferences(preferences with { Language = value }), "Language")));

        var effects = new StackPanel { Spacing = 8 };
        effects.Children.Add(palette.Label("Transparency effects", 14, true));
        var radios = new StackPanel { Spacing = 2 };
        foreach (var (id, label) in new[] { ("System", "Use Windows setting"), ("On", "On"), ("Off", "Off") })
        {
            var radio = new RadioButton { Content = T(label), GroupName = "Transparency", IsChecked = preferences.Transparency == id, IsEnabled = palette.Tokens.SupportsEffects && !busy };
            palette.ApplyAccentResources(radio);
            radio.Checked += (_, _) => SavePreferences(preferences with { Transparency = id });
            radios.Children.Add(radio);
        }
        effects.Children.Add(radios);
        var material = Choice([("Mica", "Mica"), ("Acrylic", "Acrylic")], preferences.Backdrop,
            value => SavePreferences(preferences with { Backdrop = value }), "Window material");
        material.IsEnabled = palette.Tokens.SupportsEffects && preferences.Transparency != "Off" && !busy;
        effects.Children.Add(SettingRow("Window material", "Mica uses your wallpaper colors. Acrylic blurs what is behind the window.", material));
        effects.Children.Add(palette.Label(BackdropDescription(), 12, muted: true));
        appearance.Children.Add(effects);
        body.Children.Add(palette.CardBox(appearance));

        var python = new StackPanel { Spacing = 18 };
        python.Children.Add(palette.Label("Python", 19, true));
        python.Children.Add(SettingRow("Default architecture", "Used when opening the install catalog.", Choice([("x64", "x64"), ("ARM64", "ARM64"), ("x86", "x86")], preferences.DefaultArchitecture,
            value => SavePreferences(preferences with { DefaultArchitecture = value }), "Default architecture")));
        python.Children.Add(SettingRow("Show preview releases", "Include prerelease versions in the catalog.", Toggle(preferences.ShowPreviewReleases,
            value => SavePreferences(preferences with { ShowPreviewReleases = value }), "Show preview releases")));
        python.Children.Add(SettingRow("Show specialized packages", "Expand Other distributions by default.", Toggle(preferences.ShowSpecializedPackages,
            value => SavePreferences(preferences with { ShowSpecializedPackages = value }), "Show specialized packages")));
        python.Children.Add(SettingRow("Confirm before uninstall", null, Toggle(preferences.ConfirmBeforeUninstall,
            value => SavePreferences(preferences with { ConfirmBeforeUninstall = value }), "Confirm before uninstall")));
        body.Children.Add(palette.CardBox(python));

        var manager = new StackPanel { Spacing = 12, Tag = "ManagerLocation" };
        manager.Children.Add(palette.Label("Python Install Manager location", 19, true));
        manager.Children.Add(palette.Label(connected ? "Connected on this computer" : "Not connected", 12, muted: true));
        var path = palette.Label(client.Executable ?? T("Not found. Choose a location or install Python Install Manager."), 12, muted: true);
        path.IsTextSelectionEnabled = true; path.TextWrapping = TextWrapping.Wrap;
        manager.Children.Add(path);
        var managerActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var browse = palette.Action("Change…", "\uE8B7", compact: true); browse.IsEnabled = !busy;
        browse.Click += async (_, _) =>
        {
            if (busy || confirmationOpen) return;
            confirmationOpen = true;
            try
            {
                var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".exe");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var selected = await picker.PickSingleFileAsync();
                if (selected is not null) await ChangeManagerAsync(selected.Path);
            }
            catch (Exception ex) { ShowError(ex); }
            finally { confirmationOpen = false; }
        };
        var detect = palette.Action("Auto-detect", "\uE72C", compact: true); detect.IsEnabled = !busy;
        detect.Click += async (_, _) => await ChangeManagerAsync("");
        var download = palette.Action("Download Python Install Manager", "\uE8A7", compact: true); download.Click += (_, _) => OpenManagerDownload();
        managerActions.Children.Add(browse); managerActions.Children.Add(detect); managerActions.Children.Add(download);
        manager.Children.Add(managerActions); body.Children.Add(palette.CardBox(manager));
        manager.Children.Add(palette.Label("After installing the manager, choose Auto-detect to connect without restarting PyDeck.", 12, muted: true));

        var about = new StackPanel { Spacing = 10 };
        about.Children.Add(palette.Label("About", 19, true));
        about.Children.Add(palette.Label("PyDeck", 24, true));
        about.Children.Add(palette.Label(T("Version {0} · Development preview", "0.5.1"), 12, muted: true));
        about.Children.Add(palette.Label("A desktop companion for managing Python installations with Python Install Manager.", 13));
        about.Children.Add(palette.Label("Independent project. Not affiliated with the Python Software Foundation.", 12, muted: true));
        about.Children.Add(palette.Label("Built with WinUI 3. .NET and Windows App Runtime are installed separately.", 12, muted: true));
        about.Children.Add(palette.Label("No analytics. Preferences stay on this computer; activity logs stay in this session.", 12, muted: true));
        var docs = palette.Action("Python documentation", "\uE8A7", compact: true); docs.Click += (_, _) => OpenUrl("https://docs.python.org/3/using/windows.html");
        about.Children.Add(docs); body.Children.Add(palette.CardBox(about));
        settingsScroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        settingsScroll.Loaded += (sender, _) => ((ScrollViewer)sender).ChangeView(null, settingsOffset, null, true);
        At(layout, settingsScroll, 1);
        return layout;
    }

    private Grid SettingRow(string title, string? description, FrameworkElement control)
    {
        var row = new Grid { ColumnSpacing = 24 };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(palette.Label(title, 14, true));
        if (description is not null) text.Children.Add(palette.Label(description, 12, muted: true));
        row.Children.Add(text); Grid.SetColumn(control, 1); control.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(control);
        return row;
    }
    private ComboBox Choice((string Id, string Label)[] options, string selected, Action<string> changed, string name)
    {
        var box = new ComboBox { MinWidth = 168, IsEnabled = !busy };
        palette.ApplySurfaceResources(box);
        foreach (var option in options) box.Items.Add(new ComboBoxItem { Content = T(option.Label), Tag = option.Id });
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(item => (string)item.Tag == selected);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, T(name));
        box.SelectionChanged += (_, _) => { if (box.SelectedItem is ComboBoxItem { Tag: string value }) changed(value); };
        return box;
    }
    private ToggleSwitch Toggle(bool value, Action<bool> changed, string name)
    {
        var toggle = new ToggleSwitch { IsOn = value, OnContent = T("On"), OffContent = T("Off"), IsEnabled = !busy, MinWidth = 112 };
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
            specializedExpanded = changed.ShowSpecializedPackages;
            if (previous.Language != changed.Language) ApplyLanguage();
            if (previous.Design != changed.Design || previous.Theme != changed.Theme ||
                previous.Transparency != changed.Transparency || previous.Backdrop != changed.Backdrop) ApplyAppearance();
            else RenderPage();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void SaveAppearance(string design, string theme) => SavePreferences(preferences with { Design = design, Theme = theme });

    private async Task ChangeManagerAsync(string path)
    {
        if (busy) return;
        SetBusy(true, "Connecting to Python Install Manager…");
        try
        {
            // Validate before committing the preference or discarding the old connection.
            var candidate = new PimClient(new ProcessRunner());
            if (!await candidate.DiscoverAsync(path)) throw new InvalidOperationException(candidate.LastDiscoveryError);
            var versions = await candidate.ListAsync();
            var changed = preferences with { ManagerPath = path }; store.Save(changed);
            preferences = changed; client = candidate; installed = versions; catalog = null; connected = true;
            MessageBar.IsOpen = false; StatusText.Text = T("Connected on this computer");
            Log("Connected to " + candidate.Executable);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); UpdateConnection(); RenderPage(); }
    }

    private async Task SetDefaultAsync(PythonRuntime runtime)
    {
        if (busy) return;
        SetBusy(true, T("Setting {0} as default…", runtime.DisplayName));
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
            var backup = DefaultVersionConfig.SetDefault(config, runtime.Selector);
            Log($"Saved default_tag = {runtime.Selector}." + (backup.Length > 0 ? $" Backup: {backup}" : ""));
            installed = await client.ListAsync();
            var active = installed.FirstOrDefault(r => r.IsDefault);
            if (active?.Id == runtime.Id) { Notify(T("{0} is now your default.", runtime.DisplayName), InfoBarSeverity.Success); StatusText.Text = T("Default interpreter updated"); }
            else Notify("Your preference was saved, but PIM reports a different effective default. A custom configuration or policy may override it.", InfoBarSeverity.Warning);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); UpdateConnection(); RenderPage(); }
    }

    private UIElement BuildActivityPage()
    {
        var layout = PageGrid(GridLength.Auto, GridLength.Auto, new(1, GridUnitType.Star), GridLength.Auto);
        var copy = palette.Action("Copy log", "\uE8C8", compact: true); copy.Click += (_, _) => { if (Copy(string.Join(Environment.NewLine, logLines))) StatusText.Text = T("Activity copied to clipboard"); };
        At(layout, Header("BEHIND THE SCENES", "Activity", "Real output from your Python operations.", copy), 0);
        var state = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        state.Children.Add(palette.Chip(busy ? "In progress" : "Session log", true));
        state.Children.Add(palette.Label(busy ? "You can keep browsing while this finishes." : "Everything that happens in this session appears here.", 12, muted: true));
        At(layout, state, 1);
        logBox = new TextBox { Text = string.Join(Environment.NewLine, logLines), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontFamily = new("Cascadia Mono, Consolas"), FontSize = 12, Padding = new(18), CornerRadius = new(palette.Radius), Background = Palette.Brush(palette.Card) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(logBox, T("Activity output"));
        ScrollViewer.SetVerticalScrollBarVisibility(logBox, ScrollBarVisibility.Auto); At(layout, logBox, 2);
        At(layout, palette.Label("Session only · up to 2,000 lines / 1 MB. Python Install Manager output is shown in its original language.", 11, muted: true), 3);
        return layout;
    }
}
