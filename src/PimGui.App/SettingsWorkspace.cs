using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private string settingsCategory = "appearance";
    private bool settingsDetailSelected;
    private string settingsReturnPage = "runtimes";
    private Grid? settingsWorkspace;
    private Border? settingsDetailHost;
    private FrameworkElement? settingsCategoryNavigation;
    private SettingsNavigation? settingsNavigation;
    private Button? settingsBack;
    private readonly Dictionary<string, (Grid View, ScrollViewer Scroll)> settingsCategoryViews = [];
    private readonly Dictionary<string, double> settingsCategoryOffsets = [];
    private static readonly SettingsDestination[] SettingsDestinations =
    [
        new("appearance", "Appearance settings", "\uE790", "Application"),
        new("interface", "Interface settings", "\uE8A9", "Application"),
        new("python", "Python management", "\uE8F1", "Python"),
        new("network", "Network and sources", "\uE774", "Python"),
        new("storage", "Storage and logs", "\uE7F1", "Other"),
        new("about", "About", "\uE946", "Other")
    ];

    private UIElement BuildSettingsPage()
    {
        settingsCategoryViews.Clear();
        designChoices.Clear();
        designRestartNotice = null;
        var root = PageGrid(GridLength.Auto, new(1, GridUnitType.Star));
        root.Tag = "SettingsWorkspace";
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new(0, 0, 0, 12), Tag = "SettingsWorkspaceHeader" };
        settingsBack = palette.Action("", "\uE72B", compact: true, role: ActionRole.Quiet);
        settingsBack.Tag = "SettingsBack";
        AutomationProperties.SetName(settingsBack, T("Back"));
        ToolTipService.SetToolTip(settingsBack, T("Back"));
        settingsBack.Click += (_, _) => BackFromSettings();
        heading.Children.Add(settingsBack);
        heading.Children.Add(palette.Label("Settings", 26));
        At(root, heading, 0);

        settingsWorkspace = new Grid { ColumnSpacing = 28 };
        settingsWorkspace.ColumnDefinitions.Add(new() { Width = new(224) });
        settingsWorkspace.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        settingsNavigation = presentation.CreateSettingsNavigation(palette, SettingsDestinations, settingsCategory, SelectSettingsCategory);
        settingsCategoryNavigation = settingsNavigation.View;
        settingsWorkspace.Children.Add(settingsCategoryNavigation);
        settingsDetailHost = new Border { Tag = "SettingsDetail" };
        Grid.SetColumn(settingsDetailHost, 1);
        settingsWorkspace.Children.Add(settingsDetailHost);
        settingsWorkspace.SizeChanged += (_, _) => AdaptSettingsWorkspace();
        At(root, settingsWorkspace, 1);
        ShowSettingsCategory();
        return root;
    }

    private void SelectSettingsCategory(string id)
    {
        if (!SettingsDestinations.Any(item => item.Id == id) || settingsDetailHost is null) return;
        RememberSettingsScroll();
        settingsCategory = id;
        settingsDetailSelected = true;
        ShowSettingsCategory();
        if (settingsWorkspace is { ActualWidth: > 0 and < 900 })
            DispatcherQueue.TryEnqueue(() =>
            {
                if (page == "settings" && settingsCategory == id && settingsDetailSelected) settingsBack?.Focus(FocusState.Keyboard);
            });
    }

    private void RememberSettingsScroll()
    {
        if (settingsScroll is { IsLoaded: true }) settingsCategoryOffsets[settingsCategory] = settingsScroll.VerticalOffset;
    }

    private void ShowSettingsCategory()
    {
        if (settingsDetailHost is null) return;
        settingsDetailHost.Child = null;
        if (!settingsCategoryViews.TryGetValue(settingsCategory, out var view))
        {
            var content = settingsCategory switch
            {
                "appearance" => BuildAppearanceSettings(),
                "interface" => BuildInterfaceSettings(),
                "python" => BuildPythonSettings(),
                "network" => ManagementSettings(true),
                "storage" => BuildStorageSettingsPage(),
                "about" => BuildAboutSettings(),
                _ => throw new InvalidOperationException("Unknown settings category.")
            };
            FinishSettingsGroups(content);
            var grid = PageGrid(GridLength.Auto, new(1, GridUnitType.Star));
            grid.Tag = "SettingsCategoryContent:" + settingsCategory;
            var title = palette.Label(SettingsDestinations.First(item => item.Id == settingsCategory).Label, 26);
            title.Tag = "PageTitle"; title.Margin = new(8, 6, 0, 20);
            At(grid, title, 0);
            var body = new Border { Child = content, Padding = new(0, 0, 24, 32) };
            var scroll = new ScrollViewer { Content = body, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollMode = ScrollMode.Enabled };
            var id = settingsCategory;
            scroll.Loaded += (_, _) => scroll.ChangeView(null, settingsCategoryOffsets.GetValueOrDefault(id), null, true);
            At(grid, scroll, 1);
            view = (grid, scroll);
            settingsCategoryViews[id] = view;
        }
        settingsDetailHost.Child = view.View;
        settingsScroll = view.Scroll;
        settingsOffset = settingsCategoryOffsets.GetValueOrDefault(settingsCategory);
        settingsNavigation?.Select(settingsCategory);
        AdaptSettingsWorkspace();
    }

    private void AdaptSettingsWorkspace()
    {
        if (settingsWorkspace is null || settingsCategoryNavigation is null || settingsDetailHost is null) return;
        var narrow = settingsWorkspace.ActualWidth > 0 && settingsWorkspace.ActualWidth < 900;
        settingsWorkspace.ColumnDefinitions[0].Width = narrow ? new(1, GridUnitType.Star) : new(224);
        settingsWorkspace.ColumnDefinitions[1].Width = narrow ? new(0) : new(1, GridUnitType.Star);
        settingsWorkspace.ColumnSpacing = narrow ? 0 : 28;
        settingsCategoryNavigation.Visibility = narrow && settingsDetailSelected ? Visibility.Collapsed : Visibility.Visible;
        settingsDetailHost.Visibility = narrow && !settingsDetailSelected ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(settingsDetailHost, narrow ? 0 : 1);
    }

    private void BackFromSettings()
    {
        if (settingsWorkspace is { ActualWidth: > 0 and < 900 } && settingsDetailSelected)
        {
            RememberSettingsScroll();
            settingsDetailSelected = false;
            AdaptSettingsWorkspace();
            DispatcherQueue.TryEnqueue(() =>
            {
                if (page != "settings" || settingsDetailSelected || settingsCategoryNavigation is null) return;
                var selected = Descendants(settingsCategoryNavigation).OfType<Control>()
                    .FirstOrDefault(item => Equals(item.Tag, "SettingsCategory:" + settingsCategory));
                selected?.Focus(FocusState.Keyboard);
            });
            return;
        }
        Navigate(settingsReturnPage);
    }

    private StackPanel BuildStorageSettingsPage()
    {
        var body = new StackPanel { Spacing = 24 };
        body.Children.Add(StorageSettings());
        var activity = palette.Action("Activity", "\uE9D9", role: ActionRole.Quiet);
        activity.Click += (_, _) => Navigate("activity");
        body.Children.Add(palette.Section("Activity", palette.Label("No analytics. Preferences stay on this computer; activity logs stay in this session.", 13, muted: true), activity));
        return body;
    }

    private void FinishSettingsGroups(UIElement element)
    {
        if (ActiveDesign != "Material") return;
        if (element is Grid grid)
            foreach (var child in grid.Children) FinishSettingsGroups(child);
        if (element is StackPanel stack)
        {
            if (stack.Tag is "SettingsSection" or "MaterialColorSettings")
            {
                stack.Spacing = 4;
                if (stack.Children.FirstOrDefault() is TextBlock heading)
                {
                    heading.FontSize = 14; heading.LineHeight = 20;
                    heading.Foreground = Palette.Brush(palette.Accent);
                    heading.Margin = new(16, 0, 0, 8);
                }
                var rows = new List<Grid>();
                void FinishRun()
                {
                    for (var index = 0; index < rows.Count; index++)
                    {
                        var first = index == 0 ? 24 : 4;
                        var last = index == rows.Count - 1 ? 24 : 4;
                        rows[index].CornerRadius = new(first, first, last, last);
                    }
                    rows.Clear();
                }
                foreach (var child in stack.Children)
                {
                    if (child is FrameworkElement { Visibility: Visibility.Collapsed } || child is InfoBar { IsOpen: false }) continue;
                    if (child is Grid { Tag: "SettingsRow" } row) rows.Add(row);
                    else FinishRun();
                }
                FinishRun();
            }
            foreach (var child in stack.Children) FinishSettingsGroups(child);
        }
    }
}
