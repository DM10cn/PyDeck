using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
    private readonly Dictionary<string, RadioButton> items = [];
    private bool selecting;
    private string currentPage = "";

    public override FrameworkElement CreateShell(Border surface, StackPanel brand, Border connection, Action<string> navigate)
    {
        Surface = surface; this.brand = brand; this.connection = connection;
        shell = new Grid { Tag = "MaterialShell" };
        shell.ColumnDefinitions.Add(new() { Width = new(232) }); shell.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        drawer = new Grid { Padding = new(14, 14, 14, 12) };
        drawer.RowDefinitions.Add(new() { Height = GridLength.Auto }); drawer.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        drawer.RowDefinitions.Add(new() { Height = GridLength.Auto }); drawer.RowDefinitions.Add(new() { Height = GridLength.Auto });
        brand.Margin = new(14, 2, 0, 28); connection.Margin = new(8, 12, 8, 0);
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
                VerticalContentAlignment = VerticalAlignment.Center, Padding = new(16, 10, 12, 10), MinHeight = 48,
                CornerRadius = new(24), UseSystemFocusVisuals = true, IsTabStop = true
            };
            AutomationProperties.SetName(item, destination.Label);
            item.Checked += (_, _) => { if (!selecting) navigate(destination.Id); };
            item.Click += (_, _) => { if (!selecting && currentPage != destination.Id) navigate(destination.Id); };
            items.Add(destination.Id, item);
            if (destination.Id == "settings") Put(drawer, item, 2);
            else destinations.Children.Add(item);
        }
        Put(drawer, connection, 3); shell.Children.Add(drawer);
        Grid.SetColumn(surface, 1); shell.Children.Add(surface);
        return shell;
    }

    public override void Detach()
    {
        drawer.Children.Remove(brand); drawer.Children.Remove(connection); shell.Children.Remove(Surface);
        Grid.SetColumn(Surface, 0); Grid.SetRow(connection, 0);
    }

    public override void ApplyShell(Palette palette)
    {
        Surface.Background = Palette.Brush(palette.Surface); Surface.BorderThickness = new(0);
        Surface.CornerRadius = new(28); Surface.Padding = new(28, 26, 24, 22); Surface.Margin = new(0, 6, 14, 4);
        drawer.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    public override void SelectPage(string page, Palette palette)
    {
        currentPage = page;
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
                }
            }
        }
        finally { selecting = false; }
    }

    public override double ContentWidth(string page) => page switch
    {
        "settings" => 900, "build" => 1160, "activity" => 1080, "catalog" => 1100, _ => 1020
    };

    public override Grid Header(Palette palette, string eyebrow, string title, string description, FrameworkElement? action)
    {
        var grid = HeaderGrid(); grid.RowSpacing = 4;
        for (var index = 0; index < 4; index++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var kicker = palette.Label(eyebrow, 11, true); kicker.Foreground = Palette.Brush(palette.Accent);
        kicker.CharacterSpacing = 100; kicker.LineHeight = 16; Put(grid, kicker, 0, span: 2);
        var heading = palette.Label(title, 32, true); heading.LineHeight = 40; heading.Tag = "PageTitle";
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
            Background = Palette.Brush(palette.Tokens.ControlFill), CornerRadius = new(16), Margin = new(0, 3, 0, 3)
        };
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto }); row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var label = SettingLabel(palette, title, description); row.Children.Add(label);
        control.VerticalAlignment = VerticalAlignment.Center; Put(row, control, 0, 1);
        AdaptSettingRow(row, label, control);
        return row;
    }

    private static ControlTemplate NavigationTemplate() => (ControlTemplate)XamlReader.Load("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="RadioButton">
          <Grid Background="Transparent">
            <VisualStateManager.VisualStateGroups>
              <VisualStateGroup x:Name="CommonStates">
                <VisualState x:Name="Normal" />
                <VisualState x:Name="PointerOver"><VisualState.Setters><Setter Target="StateLayer.Opacity" Value="0.08" /></VisualState.Setters></VisualState>
                <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="StateLayer.Opacity" Value="0.14" /></VisualState.Setters></VisualState>
                <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="Content.Opacity" Value="0.38" /></VisualState.Setters></VisualState>
              </VisualStateGroup>
            </VisualStateManager.VisualStateGroups>
            <Border Background="{TemplateBinding Background}" CornerRadius="{TemplateBinding CornerRadius}" />
            <Border x:Name="StateLayer" Background="{TemplateBinding Foreground}" CornerRadius="{TemplateBinding CornerRadius}" Opacity="0" />
            <ContentPresenter x:Name="Content" Content="{TemplateBinding Content}" Padding="{TemplateBinding Padding}" Foreground="{TemplateBinding Foreground}" HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}" />
          </Grid>
        </ControlTemplate>
        """);
}
