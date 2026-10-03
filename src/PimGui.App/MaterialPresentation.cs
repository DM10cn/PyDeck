using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace PimGui.App;

internal sealed class MaterialPresentation : DesktopPresentation
{
    public override string Design => "Material";
    private Grid shell = null!;
    private Grid drawer = null!;
    private StackPanel brand = null!;
    private Border connection = null!;
    private Button refresh = null!;
    private StackPanel utilities = null!;
    private readonly Dictionary<string, RadioButton> items = [];
    private bool selecting;
    private string currentPage = "";
    private bool compact;
    private Palette? activePalette;

    public override FrameworkElement CreateShell(Border surface, StackPanel brand, Border connection, Button refresh, Action<string> navigate)
    {
        Surface = surface; this.brand = brand; this.connection = connection; this.refresh = refresh;
        shell = new Grid { Tag = "MaterialShell" };
        shell.ColumnDefinitions.Add(new() { Width = new(220) }); shell.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        drawer = new Grid { Padding = new(12, 14, 12, 12) };
        drawer.RowDefinitions.Add(new() { Height = GridLength.Auto }); drawer.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        drawer.RowDefinitions.Add(new() { Height = GridLength.Auto }); drawer.RowDefinitions.Add(new() { Height = GridLength.Auto });
        brand.Margin = new(14, 2, 0, 20); connection.Margin = new(0);
        drawer.Children.Add(brand);
        var destinations = new StackPanel { Spacing = 6 }; Put(drawer, destinations, 1);
        var template = NavigationTemplate();
        foreach (var destination in Destinations)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            content.Children.Add(new FontIcon { Glyph = destination.Icon, FontSize = 20, IsTextScaleFactorEnabled = false, VerticalAlignment = VerticalAlignment.Center });
            var label = ShellLabel(destination.Label); label.MaxWidth = 144; content.Children.Add(label);
            var item = new RadioButton
            {
                Content = content, Tag = destination.Id, GroupName = "MaterialNavigation", Template = template,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center, Padding = new(16, 10, 12, 10), MinWidth = 0, MinHeight = 44,
                CornerRadius = new(22), UseSystemFocusVisuals = true, IsTabStop = true
            };
            AutomationProperties.SetName(item, destination.Label);
            item.Loaded += (_, _) => activePalette?.ApplyMotion(item);
            item.Checked += (_, _) => { if (!selecting) navigate(destination.Id); };
            item.Click += (_, _) => { if (!selecting && currentPage != destination.Id) navigate(destination.Id); };
            items.Add(destination.Id, item);
            if (destination.Id == "settings") Put(drawer, item, 3);
            else destinations.Children.Add(item);
        }
        utilities = new StackPanel { Spacing = 8, Margin = new(0, 12, 0, 12), Tag = "MaterialSidebarStatus" };
        utilities.Children.Add(connection); utilities.Children.Add(refresh);
        Put(drawer, utilities, 2); shell.Children.Add(drawer);
        Grid.SetColumn(surface, 1); shell.Children.Add(surface);
        if (connection.Child is StackPanel status && status.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault() is { } statusLabel)
        {
            var tooltip = new ToolTip();
            tooltip.SetBinding(ContentControl.ContentProperty, new Binding { Source = statusLabel, Path = new PropertyPath("Text") });
            ToolTipService.SetToolTip(connection, tooltip);
            connection.SetBinding(AutomationProperties.NameProperty, new Binding { Source = statusLabel, Path = new PropertyPath("Text") });
            AutomationProperties.SetAccessibilityView(connection, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content);
        }
        shell.SizeChanged += (_, args) => AdaptDrawer(args.NewSize.Width);
        return shell;
    }

    public override void Detach()
    {
        AdaptDrawer(double.MaxValue);
        drawer.Children.Remove(brand); utilities.Children.Remove(connection); utilities.Children.Remove(refresh); shell.Children.Remove(Surface);
        Grid.SetColumn(Surface, 0); Grid.SetRow(connection, 0);
    }

    public override void ApplyShell(Palette palette)
    {
        activePalette = palette;
        Surface.Background = Palette.Brush(palette.Surface); Surface.BorderThickness = new(0);
        Surface.CornerRadius = new(palette.Tokens.SurfaceRadius); Surface.Padding = compact ? new(16) : new(24, 20, 24, 20); Surface.Margin = new(0, 6, 12, 4);
        drawer.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        connection.Background = Palette.Brush(palette.Tokens.SecondaryContainer);
        connection.BorderThickness = new(0); connection.CornerRadius = new(16);
        connection.MinHeight = 56; connection.MinWidth = 44;
        if (connection.Child is StackPanel status && status.Children.OfType<StackPanel>().FirstOrDefault() is { } line)
        {
            status.VerticalAlignment = VerticalAlignment.Center;
            foreach (var label in line.Children.OfType<TextBlock>())
            { label.FontSize = 14; label.Foreground = Palette.Brush(palette.Tokens.OnSecondaryContainer); label.TextWrapping = TextWrapping.Wrap; label.MaxWidth = 146; }
            foreach (var dot in line.Children.OfType<Microsoft.UI.Xaml.Shapes.Ellipse>()) dot.Width = dot.Height = 8;
        }
        refresh.MinHeight = 44; refresh.MinWidth = 44;
        if (refresh.Content is StackPanel content)
        {
            content.Spacing = 10;
            foreach (var label in content.Children.OfType<TextBlock>()) label.FontSize = 14;
            foreach (var icon in content.Children.OfType<FontIcon>()) icon.FontSize = 20;
        }
        AdaptUtilities();
        foreach (var item in items.Values)
        {
            item.FocusVisualPrimaryBrush = Palette.Brush(palette.Accent);
            item.FocusVisualSecondaryBrush = Palette.Brush(palette.Shell);
        }
        palette.ApplyMotion(shell);
    }

    public override void SelectPage(string page, Palette palette)
    {
        currentPage = page;
        UpdateDrawerVisibility();
        selecting = true;
        try
        {
            foreach (var (id, item) in items)
            {
                var selected = id == (page == "components" ? "settings" : page);
                item.IsChecked = selected;
                item.Background = selected ? Palette.Brush(palette.Tokens.NavigationSelected) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                item.Foreground = Palette.Brush(selected ? palette.Tokens.NavigationForeground : palette.Muted);
                if (item.Content is StackPanel content && content.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
                {
                    label.Text = T(Destinations.First(destination => destination.Id == id).Label);
                    label.FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
                    AutomationProperties.SetName(item, label.Text);
                    ToolTipService.SetToolTip(item, label.Text);
                }
            }
        }
        finally { selecting = false; }
    }

    public override double ContentWidth(string page) => page switch
    {
        "build" or "components" => 1160, _ => double.PositiveInfinity
    };

    private void UpdateDrawerVisibility()
    {
        var settings = currentPage is "settings" or "components";
        drawer.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        shell.ColumnDefinitions[0].Width = new(settings ? 0 : compact ? 72 : 220);
    }

    private void AdaptDrawer(double width)
    {
        if (width <= 0) return;
        var narrow = width < 1040;
        UpdateDrawerVisibility();
        if (compact == narrow) return;
        compact = narrow;
        UpdateDrawerVisibility();
        drawer.Padding = compact ? new(8, 14, 8, 12) : new(12, 14, 12, 12);
        brand.Margin = compact ? new(6, 2, 0, 20) : new(14, 2, 0, 20);
        foreach (var label in brand.Children.OfType<TextBlock>()) label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        foreach (var item in items.Values)
        {
            item.Padding = compact ? new(12) : new(16, 10, 12, 10);
            item.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            if (item.Content is StackPanel content)
                foreach (var label in content.Children.OfType<TextBlock>()) label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }
        AdaptUtilities();
        Surface.Padding = compact ? new(16) : new(24, 20, 24, 20);
    }

    private void AdaptUtilities()
    {
        connection.Padding = compact ? new(16, 12, 16, 12) : new(14, 14, 14, 14);
        connection.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        if (connection.Child is StackPanel status && status.Children.OfType<StackPanel>().FirstOrDefault() is { } statusLine)
        {
            statusLine.Spacing = compact ? 0 : 8;
            foreach (var label in statusLine.Children.OfType<TextBlock>()) label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }
        refresh.HorizontalAlignment = HorizontalAlignment.Stretch;
        refresh.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        refresh.Padding = compact ? new(12) : new(14, 10, 14, 10);
        if (refresh.Content is StackPanel content)
        {
            content.Spacing = compact ? 0 : 10;
            foreach (var label in content.Children.OfType<TextBlock>()) label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    public override Grid Header(Palette palette, string eyebrow, string title, string description, FrameworkElement? action)
    {
        var grid = HeaderGrid(); grid.RowSpacing = 4;
        for (var index = 0; index < 4; index++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var kicker = palette.Label(eyebrow, 11, muted: true);
        kicker.LineHeight = 16; Put(grid, kicker, 0, span: 2);
        var heading = palette.Label(title, TextRole.Title); heading.Tag = "PageTitle";
        heading.VerticalAlignment = VerticalAlignment.Center; Put(grid, heading, 1);
        var subtitle = palette.Label(description, 14, muted: true); subtitle.LineHeight = 20; Put(grid, subtitle, 2, span: 2);
        if (action is not null)
        {
            var actions = HeaderAction(action); Put(grid, actions, 1, 1);
            grid.SizeChanged += (_, args) =>
            {
                var narrow = args.NewSize.Width < 600;
                Grid.SetRow(actions, narrow ? 3 : 1); Grid.SetColumn(actions, narrow ? 0 : 1);
                Grid.SetColumnSpan(actions, narrow ? 2 : 1); Grid.SetColumnSpan(heading, narrow ? 2 : 1);
                actions.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
                actions.Margin = narrow ? new(0, 8, 0, 0) : new(0);
            };
        }
        return grid;
    }

    public override Grid SettingRow(Palette palette, string title, string? description, FrameworkElement control)
    {
        var row = new Grid
        {
            ColumnSpacing = 20, RowSpacing = 8, Padding = new(16, 12, 16, 12), Tag = "SettingsRow", MinHeight = 60,
            Background = Palette.Brush(palette.Tokens.HighContrast ? palette.Card : palette.Tokens.SurfaceContainerLow),
            CornerRadius = new(24), Margin = new(0),
            BorderThickness = new(palette.Tokens.HighContrast ? 1 : 0), BorderBrush = Palette.Brush(palette.Tokens.Outline)
        };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var label = SettingLabel(palette, title, description);
        if (label.Children.FirstOrDefault() is TextBlock heading) heading.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        row.Children.Add(label);
        control.VerticalAlignment = VerticalAlignment.Center; Put(row, control, 0, 1);
        AdaptSettingRow(row, label, control);
        return row;
    }

    public override SettingsNavigation CreateSettingsNavigation(Palette palette, IReadOnlyList<SettingsDestination> destinations,
        string selected, Action<string> navigate)
    {
        var categories = new Dictionary<string, RadioButton>();
        var panel = new StackPanel { Spacing = 4, Padding = new(0, 0, 8, 12) };
        var scrolling = new ScrollViewer
        {
            Content = panel, Tag = "SettingsCategoryNavigation", HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var template = NavigationTemplate();
        var synchronizing = false;
        foreach (var group in destinations.GroupBy(destination => destination.Group))
        {
            var heading = palette.Label(group.Key, 14, true);
            heading.Foreground = Palette.Brush(palette.Accent);
            heading.Margin = new(16, panel.Children.Count == 0 ? 4 : 18, 12, 8);
            AutomationProperties.SetHeadingLevel(heading, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
            panel.Children.Add(heading);
            foreach (var destination in group)
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
                content.Children.Add(new FontIcon { Glyph = destination.Icon, FontSize = 20,
                    IsTextScaleFactorEnabled = false, VerticalAlignment = VerticalAlignment.Center });
                var label = ShellLabel(T(destination.Label)); content.Children.Add(label);
                var item = new RadioButton
                {
                    Content = content, Tag = "SettingsCategory:" + destination.Id, GroupName = "SettingsCategories", Template = template,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center, Padding = new(16, 12, 12, 12), MinWidth = 0, MinHeight = 48,
                    CornerRadius = new(24), UseSystemFocusVisuals = true, IsTabStop = true,
                    FocusVisualPrimaryBrush = Palette.Brush(palette.Accent), FocusVisualSecondaryBrush = Palette.Brush(palette.Surface)
                };
                AutomationProperties.SetName(item, T(destination.Label));
                item.SizeChanged += (_, args) => label.MaxWidth = Math.Max(1,
                    args.NewSize.Width - item.Padding.Left - item.Padding.Right - 34);
                item.Loaded += (_, _) => palette.ApplyMotion(item);
                var selectedByThisInput = false;
                item.Checked += (_, _) =>
                {
                    if (synchronizing) return;
                    // Native RadioButton selects before raising Click. UIA/arrow-key
                    // selection has no Click, while an already-selected item has only
                    // Click; support both without opening a detail page twice.
                    selectedByThisInput = true;
                    item.DispatcherQueue.TryEnqueue(() => selectedByThisInput = false);
                    navigate(destination.Id);
                };
                item.Click += (_, _) =>
                {
                    if (synchronizing) return;
                    if (selectedByThisInput) { selectedByThisInput = false; return; }
                    navigate(destination.Id);
                };
                categories.Add(destination.Id, item); panel.Children.Add(item);
            }
        }
        void Select(string id)
        {
            synchronizing = true;
            try
            {
                foreach (var (key, item) in categories)
                {
                    var active = key == id;
                    item.IsChecked = active;
                    item.Background = Palette.Brush(active ? palette.Tokens.NavigationSelected : Microsoft.UI.Colors.Transparent);
                    item.Foreground = Palette.Brush(active ? palette.Tokens.NavigationForeground : palette.Muted);
                    if (item.Content is StackPanel content && content.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
                        label.FontWeight = active ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
                }
            }
            finally { synchronizing = false; }
        }
        Select(selected);
        return new(scrolling, Select);
    }

    private static ControlTemplate NavigationTemplate() => (ControlTemplate)XamlReader.Load("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="RadioButton">
          <Grid Background="Transparent" Tag="MaterialMotionRoot">
            <VisualStateManager.VisualStateGroups>
              <VisualStateGroup x:Name="CommonStates">
                <VisualStateGroup.Transitions><VisualTransition GeneratedDuration="0:0:0" /></VisualStateGroup.Transitions>
                <VisualState x:Name="Normal" />
                <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="StateLayer.Opacity" Value="0.08" /></VisualState.Setters></VisualState>
                <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="StateLayer.Opacity" Value="0.14" /></VisualState.Setters></VisualState>
                <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="Content.Opacity" Value="0.38" /></VisualState.Setters></VisualState>
              </VisualStateGroup>
            </VisualStateManager.VisualStateGroups>
            <Border x:Name="RippleSurface" Background="{TemplateBinding Background}" CornerRadius="{TemplateBinding CornerRadius}" />
            <Border x:Name="StateLayer" Background="{TemplateBinding Foreground}" CornerRadius="{TemplateBinding CornerRadius}" Opacity="0" IsHitTestVisible="False" />
            <Grid x:Name="RippleHost" IsHitTestVisible="False" AutomationProperties.AccessibilityView="Raw" />
            <ContentPresenter x:Name="Content" Content="{TemplateBinding Content}" Padding="{TemplateBinding Padding}" Foreground="{TemplateBinding Foreground}" HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}" />
          </Grid>
        </ControlTemplate>
        """);
}
