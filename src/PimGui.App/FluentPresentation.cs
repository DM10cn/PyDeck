using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace PimGui.App;

internal sealed class FluentPresentation : DesktopPresentation
{
    public override string Design => "Fluent";
    private NavigationView navigation = null!;
    private Border connection = null!;
    private Button refresh = null!;
    private readonly Dictionary<string, NavigationViewItem> items = [];

    public override FrameworkElement CreateShell(Border surface, StackPanel brand, Border connection, Button refresh, Action<string> navigate)
    {
        Surface = surface; this.connection = connection; this.refresh = refresh;
        brand.Margin = new(20, 12, 12, 20);
        connection.Margin = new(8, 8, 8, 12);
        connection.Padding = new(12, 6, 12, 6); connection.MinWidth = 0; connection.MinHeight = 0;
        connection.HorizontalAlignment = HorizontalAlignment.Stretch;
        navigation = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left, IsPaneOpen = true,
            OpenPaneLength = 224, IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsBackEnabled = false, IsSettingsVisible = false, IsPaneToggleButtonVisible = false,
            AlwaysShowHeader = false, IsTitleBarAutoPaddingEnabled = false,
            PaneHeader = brand, PaneFooter = connection, Content = surface, Tag = "FluentShell"
        };
        foreach (var destination in Destinations)
        {
            var item = new NavigationViewItem
            {
                Content = ShellLabel(destination.Label), Tag = destination.Id,
                Icon = new FontIcon { Glyph = destination.Icon, FontSize = 16, IsTextScaleFactorEnabled = false }
            };
            items.Add(destination.Id, item);
            if (destination.Id == "settings") navigation.FooterMenuItems.Add(item);
            else navigation.MenuItems.Add(item);
        }
        navigation.ItemInvoked += (_, args) =>
        {
            if (args.InvokedItemContainer?.Tag is string destination) navigate(destination);
        };
        return navigation;
    }

    public override void Detach()
    {
        navigation.Content = null; navigation.PaneHeader = null; navigation.PaneFooter = null;
    }

    public override void ApplyShell(Palette palette)
    {
        connection.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        connection.BorderThickness = new(0); connection.CornerRadius = new(palette.Tokens.IconRadius);
        if (connection.Child is StackPanel status && status.Children.OfType<StackPanel>().FirstOrDefault() is { } line)
        {
            line.Spacing = 8;
            foreach (var label in line.Children.OfType<TextBlock>())
            { label.Visibility = Visibility.Visible; label.FontSize = 12; label.Foreground = Palette.Brush(palette.Text); label.TextWrapping = TextWrapping.NoWrap; label.MaxWidth = double.PositiveInfinity; }
            foreach (var dot in line.Children.OfType<Microsoft.UI.Xaml.Shapes.Ellipse>()) dot.Width = dot.Height = 6;
        }
        refresh.HorizontalAlignment = HorizontalAlignment.Left;
        refresh.HorizontalContentAlignment = HorizontalAlignment.Center;
        refresh.MinWidth = 0;
        if (refresh.Content is StackPanel content)
        {
            content.Spacing = 5;
            foreach (var label in content.Children.OfType<TextBlock>()) { label.Visibility = Visibility.Visible; label.FontSize = 11; }
            foreach (var icon in content.Children.OfType<FontIcon>()) icon.FontSize = 12;
        }
        Surface.Background = Palette.Brush(palette.Surface);
        Surface.BorderBrush = Palette.Brush(palette.Line); Surface.BorderThickness = new(1, 1, 0, 0);
        Surface.CornerRadius = new(8, 0, 0, 0); Surface.Padding = new(24, 20, 24, 20);
        Surface.Margin = new(0);
        navigation.Foreground = Palette.Brush(palette.Text);
        navigation.Resources["NavigationViewDefaultPaneBackground"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        navigation.Resources["NavigationViewContentBackground"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public override void SelectPage(string page, Palette palette)
    {
        navigation.IsPaneVisible = page is not ("settings" or "components");
        foreach (var destination in Destinations)
        {
            var entry = items[destination.Id];
            if (entry.Content is TextBlock label) label.Text = T(destination.Label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(entry, T(destination.Label));
        }
        if (items.TryGetValue(page == "components" ? "settings" : page, out var item)) navigation.SelectedItem = item;
    }

    public override double ContentWidth(string page) => page switch
    {
        "build" or "components" => 1160, _ => double.PositiveInfinity
    };

    public override SettingsNavigation CreateSettingsNavigation(Palette palette, IReadOnlyList<SettingsDestination> destinations,
        string selected, Action<string> navigate)
    {
        var categories = new Dictionary<string, (ListView List, ListViewItem Item)>();
        var lists = new List<ListView>();
        var panel = new StackPanel { Spacing = 8, Padding = new(0, 0, 12, 12) };
        var scrolling = new ScrollViewer
        {
            Content = panel, Tag = "SettingsCategoryNavigation", HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var synchronizing = false;
        foreach (var group in destinations.GroupBy(destination => destination.Group))
        {
            var heading = palette.Label(group.Key, 14, true, muted: true);
            heading.Margin = new(12, panel.Children.Count == 0 ? 4 : 16, 8, 4);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHeadingLevel(heading,
                Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
            panel.Children.Add(heading);
            var list = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new(0), IsTabStop = true
            };
            ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Disabled);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, T(group.Key));
            lists.Add(list);
            foreach (var destination in group)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                content.Children.Add(new FontIcon { Glyph = destination.Icon, FontSize = 16,
                    IsTextScaleFactorEnabled = false, VerticalAlignment = VerticalAlignment.Center });
                var label = ShellLabel(T(destination.Label)); content.Children.Add(label);
                var item = new ListViewItem
                {
                    Content = content, Tag = "SettingsCategory:" + destination.Id, Padding = new(12, 10, 12, 10),
                    MinWidth = 0, MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center,
                    UseSystemFocusVisuals = true
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, T(destination.Label));
                item.SizeChanged += (_, args) => label.MaxWidth = Math.Max(1,
                    args.NewSize.Width - item.Padding.Left - item.Padding.Right - 36);
                categories.Add(destination.Id, (list, item)); list.Items.Add(item);
            }
            string? selectedByThisInput = null;
            list.SelectionChanged += (_, args) =>
            {
                if (synchronizing || args.AddedItems.FirstOrDefault() is not ListViewItem { Tag: string tag }) return;
                var id = tag["SettingsCategory:".Length..];
                selectedByThisInput = id;
                list.DispatcherQueue.TryEnqueue(() => selectedByThisInput = null);
                navigate(id);
            };
            list.ItemClick += (_, args) =>
            {
                if (synchronizing || args.ClickedItem is not ListViewItem { Tag: string tag }) return;
                var id = tag["SettingsCategory:".Length..];
                if (selectedByThisInput == id) { selectedByThisInput = null; return; }
                navigate(id);
            };
            panel.Children.Add(list);
        }
        void Select(string id)
        {
            synchronizing = true;
            try
            {
                categories.TryGetValue(id, out var selectedEntry);
                foreach (var list in lists) list.SelectedItem = ReferenceEquals(list, selectedEntry.List) ? selectedEntry.Item : null;
            }
            finally { synchronizing = false; }
        }
        Select(selected);
        return new(scrolling, Select);
    }

    public override Grid Header(Palette palette, string eyebrow, string title, string description, FrameworkElement? action)
    {
        var grid = HeaderGrid();
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = palette.Label(title, 28, true); heading.LineHeight = 36; heading.Tag = "PageTitle";
        heading.VerticalAlignment = VerticalAlignment.Center; Put(grid, heading, 0);
        var subtitle = palette.Label(description, 14, muted: true); subtitle.LineHeight = 20; Put(grid, subtitle, 1, span: 2);
        if (action is not null)
        {
            var actions = HeaderAction(action); Put(grid, actions, 0, 1);
            grid.SizeChanged += (_, args) =>
            {
                var narrow = args.NewSize.Width < 560;
                Grid.SetRow(actions, narrow ? 2 : 0); Grid.SetColumn(actions, narrow ? 0 : 1);
                Grid.SetColumnSpan(actions, narrow ? 2 : 1); Grid.SetColumnSpan(heading, narrow ? 2 : 1);
                actions.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            };
        }
        return grid;
    }

    public override Grid SettingRow(Palette palette, string title, string? description, FrameworkElement control)
    {
        var row = new Grid
        {
            ColumnSpacing = 20, RowSpacing = 8, Padding = new(0, 10, 0, 10), Tag = "SettingsRow", MinHeight = 52,
            BorderBrush = Palette.Brush(palette.Line), BorderThickness = new(0, 0, 0, 1)
        };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var label = SettingLabel(palette, title, description); row.Children.Add(label);
        control.VerticalAlignment = VerticalAlignment.Center; Put(row, control, 0, 1);
        AdaptSettingRow(row, label, control);
        return row;
    }
}
