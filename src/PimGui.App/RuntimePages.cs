using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private string distributionFilter = "All";
    private readonly HashSet<string> expandedSeries = [];
    private Grid PageGrid(params GridLength[] rows)
    {
        var grid = new Grid { RowSpacing = palette.Tokens.SectionSpacing };
        foreach (var height in rows) grid.RowDefinitions.Add(new() { Height = height });
        return grid;
    }
    private static void At(Grid grid, UIElement element, int row) { Grid.SetRow((FrameworkElement)element, row); grid.Children.Add(element); }
    private Grid Header(string eyebrow, string title, string description, FrameworkElement? action = null)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 5 };
        var kicker = palette.Label(eyebrow.ToUpperInvariant(), 10, true, true); kicker.CharacterSpacing = 160;
        text.Children.Add(kicker);
        text.Children.Add(palette.Label(title, palette.Tokens.PageTitleSize, true));
        text.Children.Add(palette.Label(description, 13, muted: true));
        grid.Children.Add(text);
        if (action is not null) { Grid.SetColumn(action, 1); grid.Children.Add(action); if (action is FrameworkElement f) f.VerticalAlignment = VerticalAlignment.Center; }
        return grid;
    }
    private UIElement Empty(string icon, string title, string description, string? action = null, Action? clicked = null)
    {
        var panel = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 430, Margin = new(30) };
        panel.Children.Add(new FontIcon { Glyph = icon, FontSize = 38, Foreground = Palette.Brush(palette.Accent), Margin = new(0, 16, 0, 8) });
        var heading = palette.Label(title, 22, true); heading.TextAlignment = TextAlignment.Center; panel.Children.Add(heading);
        var body = palette.Label(description, 14, muted: true); body.TextAlignment = TextAlignment.Center; panel.Children.Add(body);
        if (action is not null) { var button = palette.Action(action, primary: true); button.HorizontalAlignment = HorizontalAlignment.Center; button.Click += (_, _) => clicked?.Invoke(); panel.Children.Add(button); }
        return panel;
    }
    private UIElement BuildRuntimesPage()
    {
        var layout = PageGrid(GridLength.Auto, GridLength.Auto, GridLength.Auto, new(1, GridUnitType.Star));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var refresh = palette.Action("Refresh", "\uE72C", compact: true); refresh.IsEnabled = !busy; refresh.Click += async (_, _) => await RefreshInstalledAsync();
        var install = palette.Action("Install Python", "\uE710", true); install.Click += (_, _) => Navigate("catalog");
        actions.Children.Add(refresh); actions.Children.Add(install);
        At(layout, Header("YOUR WORKSPACE", "My Python", "A little less setup. A lot more building.", actions), 0);
        if (!connected)
        {
            At(layout, ManagerSetup(), 3);
            return layout;
        }
        var defaultRuntime = installed.FirstOrDefault(r => r.IsDefault);
        var hero = new Grid { ColumnSpacing = 24 };
        hero.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); hero.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var heroText = new StackPanel { Spacing = 8 };
        heroText.Children.Add(palette.Label("YOUR DEFAULT INTERPRETER", 10, true, true));
        heroText.Children.Add(palette.Label(defaultRuntime is null ? "Choose your starting point" : "Python " + defaultRuntime.Version, palette.Tokens.HeroTitleSize, true));
        heroText.Children.Add(palette.Label(defaultRuntime is null ? "Install a version to get started." : $"{defaultRuntime.Company}  ·  {defaultRuntime.Architecture}", 13, muted: true));
        hero.Children.Add(heroText);
        var heroSide = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        if (defaultRuntime is not null)
        {
            var terminal = palette.Action("Open terminal", "\uE756"); terminal.IsEnabled = !busy; terminal.Click += (_, _) => OpenTerminal(defaultRuntime); heroSide.Children.Add(terminal);
        }
        Grid.SetColumn(heroSide, 1); hero.Children.Add(heroSide);
        var heroCard = palette.CardBox(hero, 24); heroCard.Background = Palette.Brush(palette.Tokens.Hero);
        At(layout, heroCard, 1);
        At(layout, FilterBar(false), 2);
        runtimeRows = new StackPanel { Spacing = 10 };
        At(layout, new ScrollViewer { Content = runtimeRows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 3);
        PopulateRuntimes();
        return layout;
    }

    private UIElement BuildCatalogPage()
    {
        var layout = PageGrid(GridLength.Auto, GridLength.Auto, GridLength.Auto, new(1, GridUnitType.Star), GridLength.Auto);
        var refresh = palette.Action("Refresh catalog", "\uE72C", compact: true); refresh.IsEnabled = !busy && (OfflineSource ? offlineBundle is not null : connected);
        refresh.Click += async (_, _) => { if (OfflineSource && offlineBundle is not null) await LoadOfflineFolderAsync(offlineBundle.DirectoryPath); else await LoadCatalogAsync(); };
        At(layout, Header("FIND YOUR NEXT VERSION", "Install Python", "Python releases, installed by Python Install Manager", refresh), 0);
        At(layout, CatalogSourceBar(), 1);
        At(layout, FilterBar(true), 2);
        runtimeRows = new StackPanel { Spacing = 10 };
        At(layout, new ScrollViewer { Content = runtimeRows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 3);
        At(layout, palette.Label(OfflineSource ? "Use bundles from a source you trust. A checksum verifies the files, not the publisher." : "Choose a release and select Install. Your other Python versions stay available.", 11, muted: true), 4);
        PopulateRuntimes();
        return layout;
    }

    private UIElement FilterBar(bool online)
    {
        var outer = new StackPanel { Spacing = 12 };
        var filters = new Grid { ColumnSpacing = 12 };
        filters.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var find = new AutoSuggestBox { PlaceholderText = T(online ? "Search versions or distributions" : "Search your Python versions"), Text = search, QueryIcon = new SymbolIcon(Symbol.Find), CornerRadius = new(palette.Tokens.InputRadius), MinWidth = 160 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(find, T("Search Python versions"));
        find.TextChanged += (sender, _) => { search = sender.Text; PopulateRuntimes(); };
        filters.Children.Add(find);
        var architectureFilter = Choice([("All architectures", "All architectures"), ("x64", "x64"), ("ARM64", "ARM64"), ("x86", "x86")], architecture,
            value => { architecture = value; PopulateRuntimes(); }, "Architecture");
        architectureFilter.IsEnabled = true;
        Grid.SetColumn(architectureFilter, 1); filters.Children.Add(architectureFilter);
        outer.Children.Add(filters);
        resultLabel = palette.Label("", 10, true, true); resultLabel.CharacterSpacing = 100; outer.Children.Add(resultLabel);
        return outer;
    }

    private void PopulateRuntimes()
    {
        if (runtimeRows is null) return;
        runtimeRows.Children.Clear();
        var online = page == "catalog";
        IEnumerable<PythonRuntime> source = online ? (OfflineSource ? offlineBundle?.Runtimes : catalog) ?? [] : installed;
        var filtered = RuntimeCatalog.Filter(source, architecture, !online || preferences.ShowPreviewReleases, search).ToArray();
        VisibleRuntimeCount = filtered.Length;
        if (resultLabel is not null) resultLabel.Text = online ? T(OfflineSource ? "Offline packages" : "RELEASE CATALOG") : T("INSTALLED VERSIONS  /  {0}", filtered.Length);
        if (online && OfflineSource && offlineBundle is null)
            runtimeRows.Children.Add(Empty("\uE8B7", "Install from an offline bundle", "Choose a folder containing index.json and the Python packages.", "Choose folder…", () => _ = PickOfflineFolderAsync()));
        else if (!connected)
            runtimeRows.Children.Add(ManagerSetup());
        else if (online && !OfflineSource && catalog is null)
            runtimeRows.Children.Add(Empty("\uE896", busy ? "Checking the release catalog…" : "Your next Python starts here", "Available versions are loaded directly from Python Install Manager.", busy ? null : "Load releases", () => _ = LoadCatalogAsync()));
        else if (filtered.Length == 0)
            runtimeRows.Children.Add(Empty("\uE721", source.Any() ? "No matching versions" : "No Python versions yet", source.Any() ? "Try a different search or architecture filter." : "Install your first Python version to get started.", source.Any() || online ? null : "Install Python", () => Navigate("catalog")));
        else if (online) PopulateCatalog(filtered);
        else foreach (var runtime in filtered) runtimeRows.Children.Add(RuntimeCard(runtime, false));
    }

    private UIElement ManagerSetup()
    {
        if (busy) return Empty("\uE8CE", "Finding your Python setup", "Checking Python Install Manager on this computer…");
        var panel = (StackPanel)Empty("\uE8CE", "Let's connect your manager",
            "Download Python Install Manager, then return here and check again.", "Download Python Install Manager", OpenManagerDownload);
        panel.Tag = "ManagerSetup";
        var retry = palette.Action("Check again", "\uE72C", compact: true);
        retry.Tag = "ReconnectManager";
        retry.HorizontalAlignment = HorizontalAlignment.Center;
        retry.Click += async (_, _) => await ChangeManagerAsync("");
        panel.Children.Add(retry);
        var settings = palette.Action("Open settings", compact: true);
        settings.HorizontalAlignment = HorizontalAlignment.Center;
        settings.Click += (_, _) => Navigate("settings");
        panel.Children.Add(settings);
        return panel;
    }

    private void OpenManagerDownload() => OpenUrl("https://www.python.org/downloads/windows/");

    private void PopulateCatalog(IReadOnlyList<PythonRuntime> filtered)
    {
        if (runtimeRows is null) return;
        var recommended = RuntimeCatalog.Recommended(filtered, preferences.DefaultArchitecture);
        if (recommended is not null)
        {
            runtimeRows.Children.Add(palette.Label("Recommended", 18, true));
            runtimeRows.Children.Add(RuntimeCard(recommended, true));
        }
        var standard = filtered.Where(r => !r.IsSpecialized && r.Id != recommended?.Id).ToArray();
        if (standard.Length > 0) runtimeRows.Children.Add(ReleaseGroup("More versions", standard, search.Length > 0));
        var specialized = filtered.Where(r => r.IsSpecialized).ToArray();
        if (specialized.Length > 0)
        {
            var group = ReleaseGroup("Other distributions", specialized, specializedExpanded || search.Length > 0);
            group.Expanding += (_, _) => specializedExpanded = true;
            group.Collapsed += (_, _) => specializedExpanded = false;
            runtimeRows.Children.Add(group);
        }
    }

    private Expander ReleaseGroup(string title, IReadOnlyList<PythonRuntime> releases, bool expanded)
    {
        var rows = new StackPanel { Spacing = 8 };
        var group = new Expander { Header = T(title), HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = rows, IsExpanded = expanded,
            CornerRadius = new(palette.Radius), Margin = new(0, 6, 0, 0) };
        palette.ApplySurfaceResources(group);
        void Populate()
        {
            if (rows.Children.Count != 0) return;
            var seriesRows = new StackPanel { Spacing = 8 };
            void PopulateSeries()
            {
                seriesRows.Children.Clear();
                var selected = releases.Where(r => title != "Other distributions" || RuntimeCatalog.MatchesDistribution(r, distributionFilter));
                foreach (var series in selected.GroupBy(RuntimeCatalog.MinorSeries).OrderByDescending(g => g.Key, Comparer<string>.Create(RuntimeCatalog.CompareVersions)))
                {
                    var key = title + "/" + series.Key;
                    var items = new StackPanel { Spacing = 8 };
                    var section = new Expander { Header = "Python " + series.Key, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        Content = items, IsExpanded = expandedSeries.Contains(key) || search.Length > 0, CornerRadius = new(palette.Radius) };
                    palette.ApplySurfaceResources(section);
                    void PopulateItems() { if (items.Children.Count == 0) foreach (var release in series) items.Children.Add(RuntimeCard(release, true)); }
                    if (section.IsExpanded) PopulateItems();
                    section.Expanding += (_, _) => { expandedSeries.Add(key); PopulateItems(); };
                    section.Collapsed += (_, _) => expandedSeries.Remove(key);
                    seriesRows.Children.Add(section);
                }
                if (seriesRows.Children.Count == 0) seriesRows.Children.Add(palette.Label("No matching versions", 13, muted: true));
            }
            if (title == "Other distributions")
            {
                var filter = Choice([("All", "All package types"), ("FreeThreaded", "Free-threaded"), ("Embedded", "Embeddable"), ("Tests", "With tests"), ("Other", "Other types")],
                    distributionFilter, value => { distributionFilter = value; PopulateSeries(); }, "Package type");
                filter.HorizontalAlignment = HorizontalAlignment.Left; rows.Children.Add(filter);
            }
            rows.Children.Add(seriesRows); PopulateSeries();
        }
        if (expanded) Populate();
        group.Expanding += (_, _) => Populate();
        return group;
    }

    private Border RuntimeCard(PythonRuntime runtime, bool online)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new(52) }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var monogram = RuntimeIcon(runtime);
        grid.Children.Add(monogram);
        var details = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        var title = palette.Label(RuntimeTitle(runtime), 16, true); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
        var titleLine = new Grid { ColumnSpacing = 8 };
        titleLine.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); titleLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        titleLine.Children.Add(title);
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (runtime.IsDefault && !online) badges.Children.Add(palette.Chip("Default", true));
        if (runtime.IsPrerelease) badges.Children.Add(palette.Chip("Preview"));
        if (!online && !runtime.IsManaged) badges.Children.Add(palette.Chip("External"));
        Grid.SetColumn(badges, 1); titleLine.Children.Add(badges); details.Children.Add(titleLine);
        details.Children.Add(palette.Label($"{runtime.Company}  ·  {runtime.Architecture}", 12, muted: true));
        if (!online)
        {
            var path = palette.Label(string.IsNullOrWhiteSpace(runtime.Executable) ? "Executable path unavailable" : runtime.Executable, 12, muted: true);
            path.FontFamily = new("Cascadia Mono, Consolas"); path.TextWrapping = TextWrapping.NoWrap; path.TextTrimming = TextTrimming.CharacterEllipsis;
            path.IsTextSelectionEnabled = true; ToolTipService.SetToolTip(path, runtime.Executable);
            details.Children.Add(path);
        }
        ToolTipService.SetToolTip(title, runtime.DisplayName);
        Grid.SetColumn(details, 1); grid.Children.Add(details);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (online)
        {
            var existing = installed.FirstOrDefault(r => r.Id.Equals(runtime.Id, StringComparison.OrdinalIgnoreCase));
            var install = palette.Action(existing is null ? "Install" : "Installed", existing is null ? "\uE896" : "\uE73E", primary: existing is null, compact: true);
            install.IsEnabled = existing is null && !busy && connected && client.SupportsMutations;
            install.Click += async (_, _) => { if (OfflineSource) await InstallOfflineRuntimeAsync(runtime); else await ChangeRuntimeAsync(RuntimeAction.Install, runtime); };
            actions.Children.Add(install);
            if (!OfflineSource)
            {
                var more = new Button { Content = new FontIcon { Glyph = "\uE712", FontSize = 16 }, Width = 38, Height = 38,
                    CornerRadius = new(palette.Tokens.ActionRadius), IsEnabled = connected && !busy };
                palette.ApplySurfaceResources(more);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(more, T("More options for {0}", RuntimeTitle(runtime)));
                ToolTipService.SetToolTip(more, T("Download offline package"));
                var menu = RuntimeMenu();
                var download = new MenuFlyoutItem { Text = T("Download offline package"), Icon = new FontIcon { Glyph = "\uE896" } };
                download.Click += async (_, _) => await DownloadOfflineRuntimeAsync(runtime);
                menu.Items.Add(download); more.Flyout = menu; actions.Children.Add(more);
            }
        }
        else
        {
            var terminal = palette.Action("Terminal", "\uE756", compact: true); terminal.IsEnabled = !busy; terminal.Click += (_, _) => OpenTerminal(runtime); actions.Children.Add(terminal);
            var more = new Button { Content = new FontIcon { Glyph = "\uE712", FontSize = 16 }, Width = 38, Height = 38, CornerRadius = new(palette.Tokens.ActionRadius), IsEnabled = !busy };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(more, T("More options for {0}", RuntimeTitle(runtime)));
            var menu = RuntimeMenu();
            MenuFlyoutItem Item(string text, string glyph, Action action, bool enabled = true)
            { var item = new MenuFlyoutItem { Text = T(text), Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); return item; }
            Item("Set as default", "\uE735", () => _ = SetDefaultAsync(runtime), !runtime.IsDefault);
            Item("Check for updates", "\uE895", () => _ = ChangeRuntimeAsync(RuntimeAction.Update, runtime), runtime.IsManaged && client.SupportsMutations);
            Item("Open installation folder", "\uE8B7", () => OpenFolder(runtime));
            Item("Copy executable path", "\uE8C8", () => Copy(runtime.Executable));
            Item("Check installation", "\uE73E", () => _ = CheckRuntimeAsync(runtime), runtime.IsManaged);
            Item("Reinstall to repair", "\uE90F", () => _ = ChangeRuntimeAsync(RuntimeAction.Repair, runtime), runtime.IsManaged && client.SupportsRepair && client.SupportsMutations);
            menu.Items.Add(new MenuFlyoutSeparator());
            Item("Uninstall…", "\uE74D", () => _ = ChangeRuntimeAsync(RuntimeAction.Uninstall, runtime), runtime.IsManaged && client.SupportsMutations);
            more.Flyout = menu; actions.Children.Add(more);
        }
        Grid.SetColumn(actions, 2); grid.Children.Add(actions);
        return palette.CardBox(grid, 17);
    }
}
