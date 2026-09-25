using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;

namespace PimGui.App;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class ActivityRow
{
    public ActivityEntry Entry { get; set; } = new(DateTime.MinValue, ActivityLevel.Information, "");
    public string Time => Entry.Timestamp.ToString("HH:mm:ss");
    public string Level => Entry.Level switch { ActivityLevel.Warning => "WARN", ActivityLevel.Error => "ERROR", _ => "INFO" };
    public string Message => Entry.Message;
    public string Summary => Entry.Message.Split(['\r', '\n'], 2)[0];
    public bool HasDetails => Entry.Level == ActivityLevel.Error && Entry.Message.IndexOfAny(['\r', '\n']) >= 0;
    public Visibility TextVisibility => HasDetails ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DetailVisibility => HasDetails ? Visibility.Visible : Visibility.Collapsed;
    public FontFamily MessageFont => new(Entry.Origin == ActivityOrigin.Application ? "Segoe UI Variable, Segoe UI" : "Cascadia Mono, Consolas");
    public double MessageSize => Entry.Origin == ActivityOrigin.Application ? 14 : 13;
    public Brush TextBrush { get; set; } = new SolidColorBrush();
    public Brush MutedBrush { get; set; } = new SolidColorBrush();
    public Brush LevelBrush { get; set; } = new SolidColorBrush();
}

public sealed partial class MainWindow
{
    private ListView? activityList;
    private TextBlock? activityEmpty;
    private readonly ObservableCollection<ActivityRow> activityRows = [];
    private string ActivityVisibleText => string.Join(Environment.NewLine, activityRows.Select(row => row.Entry.Format()));

    private UIElement BuildActivityPage()
    {
        var layout = PageGrid(GridLength.Auto, GridLength.Auto, new(1, GridUnitType.Star), GridLength.Auto);
        At(layout, Header("BEHIND THE SCENES", "Activity", "Python Install Manager operations"), 0);
        var level = Choice([("All", "All levels"), ("Information", "Information"), ("Warning", "Warnings"), ("Error", "Errors")],
            activityFilter?.ToString() ?? "All", value =>
            {
                activityFilter = Enum.TryParse<ActivityLevel>(value, out var selected) ? selected : null;
                RefreshActivityOutput(reset: true);
            }, "Activity level");
        level.IsEnabled = true;
        var clear = palette.Action("Clear", "\uE74D", compact: true);
        clear.Click += (_, _) => { activityLog.Clear(); RefreshActivityOutput(reset: true); };
        var copy = palette.Action("Copy log", "\uE8C8", compact: true);
        copy.Click += (_, _) => { if (Copy(activityLog.Text(activityFilter))) StatusText.Text = T("Activity copied to clipboard"); };
        var session = palette.Label("Session log", 12, muted: true); session.VerticalAlignment = VerticalAlignment.Center;
        At(layout, Toolbar(session, level, clear, copy), 1);
        activityRows.Clear();
        activityList = new ListView
        {
            ItemsSource = activityRows, ItemTemplate = (DataTemplate)Root.Resources["ActivityRowTemplate"],
            SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new(0),
            Padding = new(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = "ActivityViewer"
        };
        activityList.ItemContainerStyle = new Style(typeof(ListViewItem))
        {
            Setters = { new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
                new Setter(Control.PaddingProperty, new Thickness(0)), new Setter(Control.MinHeightProperty, 0d) }
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(activityList, T("Activity output"));
        activityEmpty = palette.Label("No activity at this level", 14, muted: true);
        activityEmpty.Margin = new(0, 12, 0, 0);
        var content = new Grid(); content.Children.Add(activityList); content.Children.Add(activityEmpty);
        At(layout, content, 2);
        At(layout, palette.Label("Session only · up to 2,000 lines / 1 MB. Python Install Manager output is shown in its original language.", 12, muted: true), 3);
        RefreshActivityOutput(reset: true);
        return layout;
    }

    private void RefreshActivityOutput(bool reset = false)
    {
        if (activityList is null) return;
        var visible = activityLog.Entries.Where(entry => activityFilter is null || entry.Level == activityFilter).ToArray();
        if (reset) activityRows.Clear();
        var retained = visible.ToHashSet(ReferenceEqualityComparer.Instance);
        while (activityRows.Count > 0 && !retained.Contains(activityRows[0].Entry)) activityRows.RemoveAt(0);
        var last = activityRows.Count == 0 ? -1 : Array.FindIndex(visible, item => ReferenceEquals(item, activityRows[^1].Entry));
        foreach (var entry in visible.Skip(last + 1))
        {
            var severityBrush = entry.Level switch { ActivityLevel.Error => "SystemFillColorCriticalBrush", ActivityLevel.Warning => "SystemFillColorCautionBrush", _ => null };
            activityRows.Add(new ActivityRow { Entry = entry, TextBrush = Palette.Brush(palette.Text), MutedBrush = Palette.Brush(palette.Muted),
                LevelBrush = severityBrush is not null && Application.Current.Resources.TryGetValue(severityBrush, out var resource) && resource is Brush brush ? brush : Palette.Brush(palette.Muted) });
        }
        if (activityEmpty is not null) activityEmpty.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
