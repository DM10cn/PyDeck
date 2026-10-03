using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;

namespace PimGui.App;

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class ActivityRow
{
    private static readonly FontFamily AppFont = new("Segoe UI Variable, Segoe UI");
    private static readonly FontFamily OutputFont = new("Cascadia Mono, Consolas");
    public ActivityEntry Entry { get; set; } = new(DateTime.MinValue, ActivityLevel.Information, "");
    public string Time => Entry.Timestamp.ToString("HH:mm:ss");
    public string Level => Entry.Level switch { ActivityLevel.Warning => "WARN", ActivityLevel.Error => "ERROR", _ => "INFO" };
    public string Message => Entry.Message;
    public string Summary => Entry.Message.Split(['\r', '\n'], 2)[0];
    public bool HasDetails => Entry.Level == ActivityLevel.Error && Entry.Message.IndexOfAny(['\r', '\n']) >= 0;
    public Visibility TextVisibility => HasDetails ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DetailVisibility => HasDetails ? Visibility.Visible : Visibility.Collapsed;
    public FontFamily MessageFont => Entry.Origin == ActivityOrigin.Application ? AppFont : OutputFont;
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
    private IReadOnlyCollection<ActivityEntry>? renderedActivity;
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
        var clear = palette.Action("Clear", "\uE74D", compact: true, role: ActionRole.Quiet);
        clear.Click += (_, _) => { activityLog.Clear(); RefreshActivityOutput(reset: true); };
        var copy = palette.Action("Copy log", "\uE8C8", compact: true);
        copy.Click += (_, _) => { if (Copy(activityLog.Text(activityFilter))) StatusText.Text = T("Activity copied to clipboard"); };
        var session = palette.Label("Session log", 12, muted: true); session.VerticalAlignment = VerticalAlignment.Center;
        var filters = Toolbar(session, level);
        var actions = Toolbar(clear, copy);
        var toolbar = new Grid { ColumnSpacing = palette.Tokens.ToolbarSpacing, RowSpacing = 8, Tag = "PageToolbar" };
        toolbar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.Children.Add(filters); Grid.SetColumn(actions, 1); toolbar.Children.Add(actions);
        var measuredTextScale = 0d;
        void AdaptToolbar()
        {
            if (toolbar.ActualWidth <= 0) return;
            measuredTextScale = systemUi.TextScaleFactor;
            // Measure the two related groups without the current Grid constraints.
            // Localized text and live Windows text scaling both affect the breakpoint.
            filters.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            actions.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
            var required = Math.Max(560, filters.DesiredSize.Width + actions.DesiredSize.Width + toolbar.ColumnSpacing);
            var narrow = toolbar.ActualWidth < required;
            Grid.SetRow(actions, narrow ? 1 : 0); Grid.SetColumn(actions, narrow ? 0 : 1);
            Grid.SetColumnSpan(filters, narrow ? 2 : 1); Grid.SetColumnSpan(actions, narrow ? 2 : 1);
            actions.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        }
        toolbar.SizeChanged += (_, _) => AdaptToolbar();
        toolbar.Loaded += (_, _) => AdaptToolbar();
        level.SizeChanged += (_, _) => AdaptToolbar();
        filters.SizeChanged += (_, _) => AdaptToolbar();
        actions.SizeChanged += (_, _) => AdaptToolbar();
        toolbar.LayoutUpdated += (_, _) =>
        {
            if (Math.Abs(measuredTextScale - systemUi.TextScaleFactor) > .001) AdaptToolbar();
        };
        At(layout, toolbar, 1);
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
                new Setter(Control.PaddingProperty, new Thickness(0, 0, 28, 0)), new Setter(Control.MinHeightProperty, 0d) }
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(activityList, ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollMode(activityList, ScrollMode.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(activityList, ScrollBarVisibility.Auto);
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
        var snapshot = activityLog.Entries;
        if (!reset && ReferenceEquals(snapshot, renderedActivity)) return;
        // Keep the reader's visible rows anchored during incremental updates. Only
        // a viewport already at the end follows newly appended output.
        if (activityList.ItemsPanelRoot is ItemsStackPanel panel)
        {
            var viewport = Descendants(activityList).OfType<ScrollViewer>().FirstOrDefault();
            panel.ItemsUpdatingScrollMode = !reset && viewport is not null && viewport.ScrollableHeight - viewport.VerticalOffset <= 2
                ? ItemsUpdatingScrollMode.KeepLastItemInView : ItemsUpdatingScrollMode.KeepItemsInView;
        }
        renderedActivity = snapshot;
        var visible = snapshot.Where(entry => activityFilter is null || entry.Level == activityFilter).ToArray();
        if (reset) activityRows.Clear();
        var retained = visible.ToHashSet(ReferenceEqualityComparer.Instance);
        while (activityRows.Count > 0 && !retained.Contains(activityRows[0].Entry)) activityRows.RemoveAt(0);
        var last = activityRows.Count == 0 ? -1 : Array.FindIndex(visible, item => ReferenceEquals(item, activityRows[^1].Entry));
        var textBrush = Palette.Brush(palette.Text);
        var mutedBrush = Palette.Brush(palette.Muted);
        foreach (var entry in visible.Skip(last + 1))
        {
            var severityBrush = entry.Level switch { ActivityLevel.Error => "SystemFillColorCriticalBrush", ActivityLevel.Warning => "SystemFillColorCautionBrush", _ => null };
            activityRows.Add(new ActivityRow { Entry = entry, TextBrush = textBrush, MutedBrush = mutedBrush,
                LevelBrush = severityBrush is not null && Application.Current.Resources.TryGetValue(severityBrush, out var resource) && resource is Brush brush ? brush : mutedBrush });
        }
        if (activityEmpty is not null)
        {
            activityEmpty.Text = T(snapshot.Count == 0 ? "No activity yet" : "No activity at this level");
            activityEmpty.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
