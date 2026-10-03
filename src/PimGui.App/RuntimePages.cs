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
    private sealed record RuntimeFilterKey(RuntimeCatalogSnapshot Snapshot, string Architecture, bool Previews,
        string Search, string Distribution, bool Online, bool Connected);
    private RuntimeFilterKey? runtimeFilterKey;
    private PythonRuntime[] runtimeFilterResults = [];
    private IReadOnlyList<PythonRuntime>? catalogProjectionSource;
    private string catalogProjectionArchitecture = "";
    private PythonRuntime? catalogRecommended;
    private (string Key, PythonRuntime[] Releases)[] catalogSeries = [];
    private Grid PageGrid(params GridLength[] rows)
    {
        var grid = new Grid { RowSpacing = palette.Tokens.SectionSpacing, Tag = "PageShell" };
        foreach (var height in rows) grid.RowDefinitions.Add(new() { Height = height });
        return grid;
    }
    private static void At(Grid grid, UIElement element, int row) { Grid.SetRow((FrameworkElement)element, row); grid.Children.Add(element); }
    private ScrollViewer PageScroll(UIElement content)
    {
        var limit = page is "settings" or "build" or "components"
            ? Math.Min(palette.Tokens.ContentWidth, presentation.ContentWidth(page)) : double.PositiveInfinity;
        var body = new Border { Child = content, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = limit };
        var scroll = new ScrollViewer
        {
        // WinUI scrollbars overlay their viewport. Reserve a full gutter even while the thumb is hidden.
        Content = new Border { Child = body, Padding = new(0, 8, 28, 20), Tag = "ScrollContentGutter" },
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalScrollMode = ScrollMode.Disabled, IsHorizontalRailEnabled = false, IsVerticalRailEnabled = true,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Top, Tag = "PageScroll"
        };
        scroll.SizeChanged += (_, args) => body.Width = Math.Max(0, Math.Min(limit, args.NewSize.Width - 28));
        return scroll;
    }
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
        runtimeRows = CreateRuntimeList();
        At(layout, runtimeRows, 2);
        PopulateRuntimes();
        return layout;
    }

    private UIElement BuildCatalogPage()
    {
        var layout = PageGrid(GridLength.Auto, GridLength.Auto, GridLength.Auto, new(1, GridUnitType.Star), GridLength.Auto);
        At(layout, Header("FIND YOUR NEXT VERSION", "Install Python", "Python releases, installed by Python Install Manager"), 0);
        At(layout, CatalogSourceBar(), 1);
        At(layout, FilterBar(true), 2);
        runtimeRows = CreateRuntimeList();
        At(layout, runtimeRows, 3);
        At(layout, palette.Label(OfflineSource ? "Use bundles from a source you trust. A checksum verifies the files, not the publisher." : "Replacing an installed micro version requires confirmation", 11, muted: true), 4);
        PopulateRuntimes();
        return layout;
    }

    private UIElement FilterBar(bool online)
    {
        var outer = new StackPanel { Spacing = 8 };
        var filters = new Grid { ColumnSpacing = palette.Tokens.ToolbarSpacing, Tag = "PageToolbar" };
        filters.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); filters.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        filters.RowDefinitions.Add(new() { Height = GridLength.Auto });
        filters.RowDefinitions.Add(new() { Height = GridLength.Auto });
        filters.RowSpacing = 8;
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
        void AdaptFilters()
        {
            if (filters.ActualWidth <= 0) return;
            var last = (FrameworkElement)filters.Children[^1];
            var fixedWidth = Math.Max(architectureFilter.MinWidth, architectureFilter.DesiredSize.Width) +
                Math.Max(last.MinWidth, last.DesiredSize.Width) + filters.ColumnSpacing * 2;
            var narrow = filters.ActualWidth < Math.Max(620, fixedWidth + 180 * systemUi.TextScaleFactor);
            Grid.SetColumnSpan(find, narrow ? 3 : 1);
            foreach (var control in filters.Children.OfType<FrameworkElement>().Skip(1))
                Grid.SetRow(control, narrow ? 1 : 0);
            Grid.SetColumn(architectureFilter, narrow ? 0 : 1);
            architectureFilter.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            Grid.SetColumn(last, narrow ? 1 : 2);
            Grid.SetColumnSpan(last, narrow ? 2 : 1);
            last.HorizontalAlignment = online && narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        }
        filters.SizeChanged += (_, _) => AdaptFilters();
        filters.Loaded += (_, _) => AdaptFilters();
        architectureFilter.SizeChanged += (_, _) => AdaptFilters();
        ((FrameworkElement)filters.Children[^1]).SizeChanged += (_, _) => AdaptFilters();
        outer.Children.Add(filters);
        resultLabel = palette.Label("", palette.Tokens.CaptionFontSize, muted: true);
        if (online)
        {
            var label = palette.Label("Show preview releases", palette.Tokens.ControlFontSize);
            label.VerticalAlignment = VerticalAlignment.Center;
            var previews = Toggle(preferences.ShowPreviewReleases,
                value => SaveCatalogPreferences(preferences with { ShowPreviewReleases = value }), "Show preview releases");
            previews.FontSize = palette.Tokens.ControlFontSize;
            previews.MinHeight = palette.Tokens.ControlHeight;
            previews.IsEnabled = true;
            var previewControls = Toolbar(label, previews);
            var summary = resultLabel;
            summary.VerticalAlignment = VerticalAlignment.Center;
            var status = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
            status.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            status.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            status.RowDefinitions.Add(new() { Height = GridLength.Auto });
            status.RowDefinitions.Add(new() { Height = GridLength.Auto });
            status.Children.Add(previewControls); Grid.SetColumn(summary, 1); status.Children.Add(summary);
            void AdaptSummary()
            {
                if (status.ActualWidth <= 0) return;
                var narrow = status.ActualWidth < previewControls.DesiredSize.Width + summary.DesiredSize.Width + 12;
                Grid.SetRow(summary, narrow ? 1 : 0); Grid.SetColumn(summary, narrow ? 0 : 1);
                Grid.SetColumnSpan(summary, narrow ? 2 : 1); Grid.SetColumnSpan(previewControls, narrow ? 2 : 1);
                summary.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            }
            status.SizeChanged += (_, _) => AdaptSummary();
            summary.SizeChanged += (_, _) => AdaptSummary();
            previewControls.SizeChanged += (_, _) => AdaptSummary();
            outer.Children.Add(status);
        }
        else outer.Children.Add(resultLabel);
        return outer;
    }

    private void SaveCatalogPreferences(AppSettings changed)
    {
        if (savingFromToggle)
        {
            _ = SaveTogglePreferencesAsync(changed);
            architecture = preferences.DefaultArchitecture;
            distributionFilter = preferences.CatalogPackageType!;
            // Let the switch present its native on/off feedback before measuring new rows.
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (page == "catalog") PopulateRuntimes();
            });
            return;
        }
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
        if (runtimeListSearch != search) { searchCollapsedSeries.Clear(); runtimeListSearch = search; }
        var entries = new List<RuntimeListEntry>();
        var online = page == "catalog";
        IReadOnlyList<PythonRuntime> source = online ? (OfflineSource ? offlineBundle?.Runtimes : catalog) ?? [] : installed;
        if (!ReferenceEquals(source, runtimeSnapshotSource))
        {
            runtimeSnapshot = new(source);
            runtimeSnapshotSource = source;
        }
        var filterKey = new RuntimeFilterKey(runtimeSnapshot!, architecture, !online || preferences.ShowPreviewReleases,
            search, online ? distributionFilter : "All", online, connected);
        // Expansion alone does not change filtering. A replaced source creates a new
        // snapshot; every filter input is part of this key, including connection state.
        if (runtimeFilterKey != filterKey)
        {
            runtimeFilterResults = runtimeSnapshot!.Filter(filterKey.Architecture, filterKey.Previews, filterKey.Search, filterKey.Distribution)
                .Where(r => online || connected || r.IsLocalBuild).ToArray();
            runtimeFilterKey = filterKey;
        }
        var filtered = runtimeFilterResults;
        VisibleRuntimeCount = filtered.Length;
        if (resultLabel is not null) resultLabel.Text = filtered.Length == source.Count
            ? T(online ? "Available versions · {0}" : "Installed · {0}", filtered.Length)
            : T("Showing {0} of {1}", filtered.Length, source.Count);
        if (online && OfflineSource && offlineBundle is null)
            entries.Add(new("empty", Content: Empty("\uE8B7", "Install from an offline bundle", "Choose a folder containing index.json and the Python packages.", "Choose folder…", () => _ = PickOfflineFolderAsync())));
        else if (online && !connected)
            entries.Add(new("empty", Content: ManagerSetup()));
        else if (online && !OfflineSource && catalog is null)
            entries.Add(new("empty", Content: Empty("\uE896", workCoordinator.Contains(WorkKind.Catalog) ? "Checking the release catalog…" : "Your next Python starts here", "Available versions are loaded from the selected catalog", workCoordinator.Contains(WorkKind.Catalog) ? null : "Load releases", () => _ = LoadCatalogAsync())));
        else if (filtered.Length == 0)
            entries.Add(new("empty", Content: Empty("\uE721", source.Any() ? "No matching versions" : "No Python versions yet", source.Any() ? "Try another search or filter" : "Install your first Python version to get started.", source.Any() ? "Clear filters" : online ? null : "Install Python", source.Any() ? ClearRuntimeFilters : () => Navigate("catalog"))));
        else if (online) PopulateCatalog(filtered, entries);
        else foreach (var runtime in filtered) entries.Add(new(RuntimeEntryKey(runtime), Runtime: runtime));
        UpdateRuntimeEntries(entries);
    }

    private void ClearRuntimeFilters()
    {
        search = "";
        var find = Descendants(PageHost).OfType<AutoSuggestBox>().FirstOrDefault();
        if (find is not null) find.Text = "";
        foreach (var choice in Descendants(PageHost).OfType<ComboBox>().ToArray())
        {
            var name = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(choice);
            var value = name == T("Architecture") ? "All architectures" : name == T("Package type") ? "All" : null;
            if (value is not null) choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().First(item => item.Tag as string == value);
        }
        PopulateRuntimes();
        find?.Focus(FocusState.Keyboard);
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

    private void PopulateCatalog(IReadOnlyList<PythonRuntime> filtered, List<RuntimeListEntry> entries)
    {
        if (runtimeRows is null) return;
        var recommendedArchitecture = architecture == "All architectures"
            ? System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
            {
                System.Runtime.InteropServices.Architecture.Arm64 => "ARM64",
                System.Runtime.InteropServices.Architecture.X86 => "x86",
                _ => "x64"
            } : architecture;
        if (!ReferenceEquals(catalogProjectionSource, filtered) || catalogProjectionArchitecture != recommendedArchitecture)
        {
            catalogRecommended = RuntimeCatalog.Recommended(filtered, recommendedArchitecture);
            catalogSeries = filtered.GroupBy(RuntimeCatalog.MinorSeries).OrderByDescending(g => RuntimeCatalog.VersionKey(g.Key))
                .Select(group => (group.Key, group.ToArray())).ToArray();
            catalogProjectionSource = filtered;
            catalogProjectionArchitecture = recommendedArchitecture;
        }
        // Installation state can change while the catalog/filter projection is retained.
        // Rebuild this lookup once per populate, with the same ID/version comparison rules.
        var installedVersions = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var runtime in installed)
        {
            if (!installedVersions.TryGetValue(runtime.Id, out var versions))
                installedVersions.Add(runtime.Id, versions = new(StringComparer.Ordinal));
            versions.Add(runtime.Version);
        }
        bool IsInstalled(PythonRuntime runtime) => installedVersions.TryGetValue(runtime.Id, out var versions) && versions.Contains(runtime.Version);
        var recommended = catalogRecommended;
        if (recommended is not null)
        {
            entries.Add(new("recommended-heading", Heading: "Recommended"));
            entries.Add(new("recommended:" + RuntimeEntryKey(recommended), Runtime: recommended, Online: true, Recommended: true,
                Installed: IsInstalled(recommended)));
        }
        entries.Add(new("all-heading", Heading: "All versions"));
        var groups = catalogSeries;
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var series = groups[groupIndex];
            var open = expandedSeries.Contains(series.Key) || search.Length > 0 && !searchCollapsedSeries.Contains(series.Key);
            entries.Add(new("series:" + series.Key, Series: series.Key, First: groupIndex == 0,
                Last: groupIndex == groups.Length - 1 && !open, Expanded: open));
            if (!open) continue;
            var releases = series.Releases;
            for (var index = 0; index < releases.Length; index++)
                entries.Add(new(RuntimeEntryKey(releases[index]), Runtime: releases[index], Series: series.Key, Online: true, InGroup: true,
                    Last: groupIndex == groups.Length - 1 && index == releases.Length - 1, Installed: IsInstalled(releases[index])));
        }
    }

    private Border RuntimeCard(PythonRuntime runtime, bool online, bool inGroup = false, bool recommended = false, bool installedRuntime = false)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new() { Width = new(inGroup ? 44 : 52) }); grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var monogram = RuntimeIcon(runtime, compact: inGroup);
        grid.Children.Add(monogram);
        var details = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var title = palette.Label(RuntimeTitle(runtime), inGroup ? 14 : 16, true); title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis;
        var titleLine = new Grid { ColumnSpacing = 8 };
        titleLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); titleLine.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        titleLine.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        titleLine.Children.Add(title);
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (runtime.IsDefault && !online) badges.Children.Add(palette.Badge("Default", true));
        if (runtime.IsPrerelease) badges.Children.Add(palette.Badge("Preview"));
        if (!online && !runtime.IsManaged) badges.Children.Add(palette.Badge(runtime.IsLocalBuild ? "Local build" : "External"));
        Grid.SetColumn(badges, 1); titleLine.Children.Add(badges); details.Children.Add(titleLine);
        void FitTitle() => title.MaxWidth = Math.Max(0, titleLine.ActualWidth - badges.ActualWidth - (badges.Children.Count > 0 ? 8 : 0));
        titleLine.SizeChanged += (_, _) => FitTitle();
        badges.SizeChanged += (_, _) => FitTitle();
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
            var install = palette.Action(installedRuntime ? "Installed" : "Install", installedRuntime ? "\uE73E" : "\uE896", compact: !recommended, role: installedRuntime ? ActionRole.Quiet : ActionRole.Secondary);
            SizeInstallAction(install, recommended);
            BindAvailability(install, () => !installedRuntime && CanWork(WorkKind.RuntimeMutation) && connected && client.SupportsMutations);
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
            var terminal = palette.RuntimeTerminalAction(); BindAvailability(terminal, () => !workCoordinator.Contains(WorkKind.RuntimeMutation) && !workCoordinator.Contains(WorkKind.Storage)); terminal.Click += (_, _) => OpenTerminal(runtime); actions.Children.Add(terminal);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(terminal, T("Open terminal for {0}", RuntimeTitle(runtime)));
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
        var row = palette.RuntimeCardBox(grid, runtime.IsDefault && !online, inGroup ? 8 : palette.Tokens.RowPadding); row.Tag = runtime;
        if (inGroup)
        {
            row.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            row.CornerRadius = new(0); row.BorderThickness = new(0, 0, 0, 1);
        }
        return row;
    }
}
