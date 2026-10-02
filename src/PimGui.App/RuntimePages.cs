using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private string distributionFilter = "Standard";
    private readonly HashSet<string> expandedSeries = [];
    private IReadOnlyList<PythonRuntime>? runtimeSnapshotSource;
    private RuntimeCatalogSnapshot? runtimeSnapshot;
    private Grid PageGrid(params GridLength[] rows)
    {
        var grid = new Grid { RowSpacing = palette.Tokens.SectionSpacing, Tag = "PageShell" };
        foreach (var height in rows) grid.RowDefinitions.Add(new() { Height = height });
        return grid;
    }
    private static void At(Grid grid, UIElement element, int row) { Grid.SetRow((FrameworkElement)element, row); grid.Children.Add(element); }
    private static ScrollViewer PageScroll(UIElement content) => new()
    {
        // WinUI scrollbars overlay their viewport. Reserve a full gutter even while the thumb is hidden.
        Content = new Border { Child = content, Padding = new(0, 8, 28, 20), Tag = "ScrollContentGutter" },
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalScrollMode = ScrollMode.Disabled, IsHorizontalRailEnabled = false, IsVerticalRailEnabled = true,
        VerticalContentAlignment = VerticalAlignment.Top, Tag = "PageScroll"
    };
    private Grid Header(string eyebrow, string title, string description, FrameworkElement? action = null)
        => presentation.Header(palette, eyebrow, title, description, action);
    private StackPanel Toolbar(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = palette.Tokens.ToolbarSpacing, Tag = "PageToolbar" };
        foreach (var child in children) row.Children.Add(child);
        return row;
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
        var layout = PageGrid(GridLength.Auto, GridLength.Auto, new(1, GridUnitType.Star));
        var install = palette.Action("Install Python", "\uE710", true); install.Click += (_, _) => Navigate("catalog");
        At(layout, Header("YOUR WORKSPACE", "My Python", "Manage your installed Python versions", install), 0);
        if (!connected && !installed.Any(r => r.IsLocalBuild))
        {
            At(layout, ManagerSetup(), 2);
            return layout;
        }
        At(layout, FilterBar(false), 1);
        runtimeRows = new StackPanel { Spacing = 6 };
        At(layout, PageScroll(runtimeRows), 2);
        PopulateRuntimes();
        return layout;
    }

    private UIElement BuildCatalogPage()
    {
        var layout = PageGrid(GridLength.Auto, GridLength.Auto, GridLength.Auto, new(1, GridUnitType.Star), GridLength.Auto);
        At(layout, Header("FIND YOUR NEXT VERSION", "Install Python", "Python releases, installed by Python Install Manager"), 0);
        At(layout, CatalogSourceBar(), 1);
        At(layout, FilterBar(true), 2);
        runtimeRows = new StackPanel { Spacing = 10 };
        At(layout, PageScroll(runtimeRows), 3);
        At(layout, palette.Label(OfflineSource ? "Use bundles from a source you trust. A checksum verifies the files, not the publisher." : "Replacing an installed micro version requires confirmation", 11, muted: true), 4);
        PopulateRuntimes();
        return layout;
    }

    private UIElement FilterBar(bool online)
    {
        var outer = new StackPanel { Spacing = 12 };
        var filters = new Grid { ColumnSpacing = palette.Tokens.ToolbarSpacing, Tag = "PageToolbar" };
        filters.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var find = palette.Search(online ? "Search versions or distributions" : "Search your Python versions", search);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(find, T("Search Python versions"));
        find.TextChanged += (sender, _) => { search = sender.Text; ScheduleSearchRefresh(PopulateRuntimes); };
        find.QuerySubmitted += (_, _) => PopulateRuntimes();
        filters.Children.Add(find);
        var architectureFilter = Choice([("All architectures", "All architectures"), ("x64", "x64"), ("ARM64", "ARM64"), ("x86", "x86")], architecture,
            value =>
            {
                if (online) SaveCatalogPreferences(preferences with { DefaultArchitecture = value });
                else { architecture = value; PopulateRuntimes(); }
            }, "Architecture");
        architectureFilter.IsEnabled = true;
        architectureFilter.MinWidth = 120;
        Grid.SetColumn(architectureFilter, 1); filters.Children.Add(architectureFilter);
        if (online)
        {
            var type = Choice([("Standard", "Standard Python"), ("All", "All package types"), ("FreeThreaded", "Free-threaded"), ("Embedded", "Embeddable"), ("Tests", "With tests"), ("Other", "Other types")],
                distributionFilter, value => SaveCatalogPreferences(preferences with { CatalogPackageType = value }), "Package type");
            type.MinWidth = 140; type.IsEnabled = true; Grid.SetColumn(type, 2); filters.Children.Add(type);
        }
        else
        {
            var refresh = palette.IconAction("Refresh", "\uE72C"); BindAvailability(refresh, () => CanWork(WorkKind.Runtimes));
            refresh.Click += async (_, _) => await RefreshInstalledAsync(); Grid.SetColumn(refresh, 2); filters.Children.Add(refresh);
        }
        outer.Children.Add(filters);
        if (online)
        {
            var label = palette.Label("Show preview releases", palette.Tokens.ControlFontSize);
            label.VerticalAlignment = VerticalAlignment.Center;
            var previews = Toggle(preferences.ShowPreviewReleases,
                value => SaveCatalogPreferences(preferences with { ShowPreviewReleases = value }), "Show preview releases");
            previews.FontSize = palette.Tokens.ControlFontSize;
            previews.MinHeight = palette.Tokens.ControlHeight;
            previews.IsEnabled = true;
            outer.Children.Add(Toolbar(label, previews));
        }
        resultLabel = online ? null : palette.Label("", palette.Tokens.CaptionFontSize, muted: true);
        if (resultLabel is not null) outer.Children.Add(resultLabel);
        return outer;
    }

    private void SaveCatalogPreferences(AppSettings changed)
    {
        try
        {
            changed = changed.Normalize();
            store.Save(changed);
            preferences = changed;
            architecture = changed.DefaultArchitecture;
            distributionFilter = changed.CatalogPackageType!;
            // Refresh the results in place so keyboard focus and the search text survive filtering.
            PopulateRuntimes();
        }
        catch (Exception ex)
        {
            ShowError(ex);
            RenderPage();
        }
    }

    private void PopulateRuntimes()
    {
        CancelSearchRefresh();
        if (runtimeRows is null) return;
        runtimeRows.Children.Clear();
        var online = page == "catalog";
        IReadOnlyList<PythonRuntime> source = online ? (OfflineSource ? offlineBundle?.Runtimes : catalog) ?? [] : installed;
        if (!ReferenceEquals(source, runtimeSnapshotSource))
        {
            runtimeSnapshot = new(source);
            runtimeSnapshotSource = source;
        }
        var filtered = runtimeSnapshot!.Filter(architecture, !online || preferences.ShowPreviewReleases, search, online ? distributionFilter : "All")
            .Where(r => online || connected || r.IsLocalBuild).ToArray();
        VisibleRuntimeCount = filtered.Length;
        if (resultLabel is not null) resultLabel.Text = online ? T(OfflineSource ? "Offline packages" : "RELEASE CATALOG") : T("INSTALLED VERSIONS  /  {0}", filtered.Length);
        if (online && OfflineSource && offlineBundle is null)
            runtimeRows.Children.Add(Empty("\uE8B7", "Install from an offline bundle", "Choose a folder containing index.json and the Python packages.", "Choose folder…", () => _ = PickOfflineFolderAsync()));
        else if (online && !connected)
            runtimeRows.Children.Add(ManagerSetup());
        else if (online && !OfflineSource && catalog is null)
            runtimeRows.Children.Add(Empty("\uE896", workCoordinator.Contains(WorkKind.Catalog) ? "Checking the release catalog…" : "Your next Python starts here", "Available versions are loaded from the selected catalog", workCoordinator.Contains(WorkKind.Catalog) ? null : "Load releases", () => _ = LoadCatalogAsync()));
        else if (filtered.Length == 0)
            runtimeRows.Children.Add(Empty("\uE721", source.Any() ? "No matching versions" : "No Python versions yet", source.Any() ? "Try another search or filter" : "Install your first Python version to get started.", source.Any() || online ? null : "Install Python", () => Navigate("catalog")));
        else if (online) PopulateCatalog(filtered);
        else foreach (var runtime in filtered) runtimeRows.Children.Add(RuntimeCard(runtime, false));
    }

    private UIElement ManagerSetup()
    {
        if (workCoordinator.Contains(WorkKind.Connection)) return Empty("\uE8CE", "Finding your Python setup", "Checking Python Install Manager on this computer…");
        var panel = (StackPanel)Empty("\uE8CE", "Let's connect your manager",
            "Download Python Install Manager, then return here and check again.", "Download Python Install Manager", OpenManagerDownload);
        panel.Tag = "ManagerSetup";
        var retry = palette.Action("Check again", "\uE72C", compact: true);
        BindAvailability(retry, () => CanWork(WorkKind.Connection));
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
        var recommendedArchitecture = architecture == "All architectures"
            ? System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
                System.Runtime.InteropServices.Architecture.X86 => "x86",
                _ => "x64"
            } : architecture;
        var recommended = RuntimeCatalog.Recommended(filtered, recommendedArchitecture);
        if (recommended is not null)
        {
            runtimeRows.Children.Add(palette.Label("Recommended", palette.Tokens.SectionTitleSize, true));
            var card = RuntimeCard(recommended, true, recommended: true); card.Tag = "RecommendedRuntime";
            runtimeRows.Children.Add(card);
        }
        runtimeRows.Children.Add(palette.Label("All versions", palette.Tokens.SectionTitleSize, true));
        foreach (var series in filtered.GroupBy(RuntimeCatalog.MinorSeries).OrderByDescending(g => RuntimeCatalog.VersionKey(g.Key)))
        {
            var items = new StackPanel { Spacing = 0 };
            var section = palette.Expander("Python " + series.Key, items, expandedSeries.Contains(series.Key) || search.Length > 0);
            section.Tag = "CatalogSeries:" + series.Key;
            section.Padding = new(0);
            // One expandable level per minor. Release rows use dividers, not nested cards.
            section.Resources["ExpanderContentBackground"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            void PopulateItems()
            {
                if (items.Children.Count != 0) return;
                foreach (var release in series) items.Children.Add(RuntimeCard(release, true, inGroup: true));
                // Material separates adjacent releases without outlining the group's foot.
                palette.FinishReleaseGroup(section, items);
            }
            if (section.IsExpanded) PopulateItems();
            section.Expanding += (_, _) => { expandedSeries.Add(series.Key); PopulateItems(); };
            section.Collapsed += (_, _) => expandedSeries.Remove(series.Key);
            runtimeRows.Children.Add(section);
        }
    }
    private Border RuntimeCard(PythonRuntime runtime, bool online, bool inGroup = false, bool recommended = false)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new(inGroup ? 44 : 52) }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var monogram = RuntimeIcon(runtime, compact: inGroup);
        grid.Children.Add(monogram);
        var details = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        var title = palette.Label(RuntimeTitle(runtime), inGroup ? 14 : 16, true); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
        var titleLine = new Grid { ColumnSpacing = 8 };
        titleLine.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); titleLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        titleLine.Children.Add(title);
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (runtime.IsDefault && !online) badges.Children.Add(palette.Badge("Default", true));
        if (runtime.IsPrerelease) badges.Children.Add(palette.Badge("Preview"));
        if (!online && !runtime.IsManaged) badges.Children.Add(palette.Badge(runtime.IsLocalBuild ? "Local build" : "External"));
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
            var existing = installed.FirstOrDefault(r => r.Id.Equals(runtime.Id, StringComparison.OrdinalIgnoreCase) && r.Version == runtime.Version);
            var install = palette.Action(existing is null ? "Install" : "Installed", existing is null ? "\uE896" : "\uE73E", compact: !recommended, role: existing is null ? (recommended ? ActionRole.Primary : ActionRole.Secondary) : ActionRole.Quiet);
            BindAvailability(install, () => existing is null && CanWork(WorkKind.RuntimeMutation) && connected && client.SupportsMutations);
            install.Click += async (_, _) => { if (OfflineSource) await InstallOfflineRuntimeAsync(runtime); else await ChangeRuntimeAsync(RuntimeAction.Install, runtime); };
            actions.Children.Add(install);
            if (!OfflineSource)
            {
                var more = palette.IconAction(T("More options for {0}", RuntimeTitle(runtime)), "\uE712");
                BindAvailability(more, () => connected && CanWork(WorkKind.OfflineDownload));
                ToolTipService.SetToolTip(more, T("Download offline package"));
                var menu = RuntimeMenu();
                var download = new MenuFlyoutItem { Text = T("Download offline package"), Icon = new FontIcon { Glyph = "\uE896" } };
                download.Click += async (_, _) => await DownloadOfflineRuntimeAsync(runtime);
                menu.Items.Add(download); more.Flyout = menu; actions.Children.Add(more);
            }
        }
        else
        {
            var terminal = palette.Action("Terminal", "\uE756", compact: true, role: ActionRole.Quiet); BindAvailability(terminal, () => !workCoordinator.Contains(WorkKind.RuntimeMutation) && !workCoordinator.Contains(WorkKind.Storage)); terminal.Click += (_, _) => OpenTerminal(runtime); actions.Children.Add(terminal);
            var more = palette.IconAction(T("More options for {0}", RuntimeTitle(runtime)), "\uE712");
            var menu = RuntimeMenu();
            MenuFlyoutItem Item(string text, string glyph, Action action, bool enabled = true)
            { var item = new MenuFlyoutItem { Text = T(text), Icon = new FontIcon { Glyph = glyph }, IsEnabled = enabled };
                if (text is "Set as default" or "Check for updates" or "Reinstall to repair" or "Uninstall…") BindAvailability(item, () => enabled && CanWork(WorkKind.RuntimeMutation));
                else if (text == "Check installation") BindAvailability(item, () => enabled && CanWork(WorkKind.Runtimes));
                else if (text == "Remove from list") BindAvailability(item, () => enabled && CanWork(WorkKind.Storage));
                item.Click += (_, _) => action(); menu.Items.Add(item); return item; }
            Item("Set as default", "\uE735", () => _ = SetDefaultAsync(runtime), !runtime.IsDefault && !runtime.IsLocalBuild && connected);
            Item("Check for updates", "\uE895", () => _ = ChangeRuntimeAsync(RuntimeAction.Update, runtime), runtime.IsManaged && client.SupportsMutations);
            Item("Open installation folder", "\uE8B7", () => OpenFolder(runtime));
            Item("Runtime usage", "\uE8B7", () => _ = ShowRuntimeUsageAsync(runtime));
            Item("Copy executable path", "\uE8C8", () => Copy(runtime.Executable));
            Item("Check installation", "\uE73E", () => _ = CheckRuntimeAsync(runtime), runtime.IsManaged || runtime.IsLocalBuild);
            Item("Reinstall to repair", "\uE90F", () => _ = ChangeRuntimeAsync(RuntimeAction.Repair, runtime), runtime.IsManaged && client.SupportsRepair && client.SupportsMutations);
            menu.Items.Add(new MenuFlyoutSeparator());
            if (runtime.IsLocalBuild) Item("Remove from list", "\uE74D", () => _ = RemoveLocalRuntimeAsync(runtime));
            else Item("Uninstall…", "\uE74D", () => _ = ChangeRuntimeAsync(RuntimeAction.Uninstall, runtime), runtime.IsManaged && client.SupportsMutations);
            more.Flyout = menu; actions.Children.Add(more);
        }
        Grid.SetColumn(actions, 2); grid.Children.Add(actions);
        var row = palette.CardBox(grid, inGroup ? 8 : palette.Tokens.RowPadding); row.Tag = runtime;
        if (inGroup)
        {
            row.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            row.CornerRadius = new(0); row.BorderThickness = new(0, 0, 0, 1);
        }
        return row;
    }
}
