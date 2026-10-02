using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace PimGui.App;

// The business pages supply content and commands. Each presentation owns the visual
// composition, navigation and layout; no second tree is kept alive in the window.
internal abstract class DesktopPresentation
{
    public abstract string Design { get; }
    protected Border Surface = null!;
    protected static readonly (string Id, string Label, string Icon)[] Destinations =
    [
        ("runtimes", "My Python", "\uE8F1"), ("catalog", "Install Python", "\uE896"),
        ("environments", "Virtual environments", "\uE8B7"), ("build", "Build Python", "\uE90F"),
        ("activity", "Activity", "\uE9D9"), ("settings", "Settings", "\uE713")
    ];

    public abstract FrameworkElement CreateShell(Border surface, StackPanel brand, Border connection, Action<string> navigate);
    public abstract void Detach();
    public abstract void ApplyShell(Palette palette);
    public abstract void SelectPage(string page, Palette palette);
    public abstract double ContentWidth(string page);
    public abstract Grid Header(Palette palette, string eyebrow, string title, string description, FrameworkElement? action);
    public abstract Grid SettingRow(Palette palette, string title, string? description, FrameworkElement control);

    protected static TextBlock ShellLabel(string text) => new()
    {
        Text = text, Tag = "NavigationLabel", VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 20
    };

    protected static Grid HeaderGrid()
    {
        var grid = new Grid { ColumnSpacing = 20, RowSpacing = 6, Tag = "PageHeader" };
        grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        return grid;
    }

    protected static void Put(Grid grid, FrameworkElement element, int row, int column = 0, int span = 1)
    {
        Grid.SetRow(element, row); Grid.SetColumn(element, column); Grid.SetColumnSpan(element, span);
        grid.Children.Add(element);
    }

    protected static Grid HeaderAction(FrameworkElement action) => new()
    {
        Tag = "HeaderActions", VerticalAlignment = VerticalAlignment.Center,
        Children = { action }
    };

    protected static void AdaptSettingRow(Grid row, StackPanel label, FrameworkElement control)
    {
        // Keep the native input's keyboard behavior. Only the surrounding layout changes.
        row.SizeChanged += (_, args) =>
        {
            var available = Math.Max(0, args.NewSize.Width - row.Padding.Left - row.Padding.Right);
            var narrow = args.NewSize.Width < 620;
            Grid.SetColumnSpan(label, narrow ? 2 : 1);
            Grid.SetColumn(control, narrow ? 0 : 1); Grid.SetRow(control, narrow ? 1 : 0); Grid.SetColumnSpan(control, narrow ? 2 : 1);
            control.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            control.MaxWidth = Math.Max(control.MinWidth, narrow ? available : Math.Min(380, available / 2));
        };
    }

    protected static StackPanel SettingLabel(Palette palette, string title, string? description)
    {
        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var heading = palette.Label(title, palette.Tokens.BodyFontSize, true); heading.LineHeight = 20;
        text.Children.Add(heading);
        if (description is not null)
        {
            var caption = palette.Label(description, palette.Tokens.CaptionFontSize, muted: true); caption.LineHeight = 16;
            text.Children.Add(caption);
        }
        return text;
    }
}
