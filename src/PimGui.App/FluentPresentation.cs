using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace PimGui.App;

internal sealed class FluentPresentation : DesktopPresentation
{
    public override string Design => "Fluent";
    private NavigationView navigation = null!;
    private readonly Dictionary<string, NavigationViewItem> items = [];

    public override FrameworkElement CreateShell(Border surface, StackPanel brand, Border connection, Action<string> navigate)
    {
        Surface = surface;
        brand.Margin = new(20, 12, 12, 20);
        connection.Margin = new(8, 8, 8, 12);
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
        "settings" => 920, "build" => 1160, "activity" => 1120, _ => 1040
    };

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
