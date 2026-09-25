using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private StackPanel BuildField(string title, FrameworkElement control)
    {
        var field = new StackPanel { Spacing = 6 };
        field.Children.Add(palette.Label(title, palette.Tokens.ControlFontSize, true));
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        field.Children.Add(control);
        return field;
    }

    private static Grid BuildCompactGrid(IReadOnlyList<FrameworkElement> items, double minimumColumnWidth)
    {
        var grid = new Grid { ColumnSpacing = 20, RowSpacing = 8, Tag = "BuildCompactGroup" };
        foreach (var item in items) grid.Children.Add(item);
        void Arrange(double width)
        {
            var columns = Math.Clamp((int)((width + 20) / (minimumColumnWidth + 20)), 1, 3);
            if (grid.ColumnDefinitions.Count == columns) return;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            for (var i = 0; i < (items.Count + columns - 1) / columns; i++) grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var i = 0; i < items.Count; i++) { Grid.SetRow(items[i], i / columns); Grid.SetColumn(items[i], i % columns); }
        }
        Arrange(0); grid.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        return grid;
    }
}
