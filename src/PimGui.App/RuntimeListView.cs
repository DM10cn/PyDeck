using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PimGui.Core;
using Windows.Foundation;

namespace PimGui.App;

public sealed partial class MainWindow
{
    // Only lightweight data survives outside the realized viewport. Group releases live in
    // the same ItemsStackPanel as their header, so expanding a large series stays virtualized.
    private sealed record RuntimeListEntry(string Key, PythonRuntime? Runtime = null, string? Series = null,
        string? Heading = null, UIElement? Content = null, bool Online = false, bool InGroup = false,
        bool Recommended = false, bool First = false, bool Last = false, bool Installed = false, bool Expanded = false)
    {
        public bool First { get; set; } = First;
        public bool Last { get; set; } = Last;
        public bool Expanded { get; set; } = Expanded;
    }

    private ObservableCollection<RuntimeListEntry> runtimeEntries = [];
    private readonly HashSet<string> searchCollapsedSeries = [];
    private string runtimeListSearch = "";
    private bool reconcilingRuntimeEntries;

    private static string RuntimeEntryKey(PythonRuntime runtime) => $"runtime:{runtime.Id}:{runtime.Version}:{runtime.Architecture}";

    private ListView CreateRuntimeList()
    {
        runtimeEntries = [];
        searchCollapsedSeries.Clear();
        runtimeListSearch = search;
        var list = new ListView
        {
            ItemsSource = runtimeEntries, SelectionMode = ListViewSelectionMode.None, IsItemClickEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new(0), BorderThickness = new(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), Tag = "RuntimeList",
            ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <ContentControl HorizontalContentAlignment="Stretch" IsTabStop="False" Padding="0" />
                </DataTemplate>
                """),
            ItemContainerStyle = new Style(typeof(ListViewItem))
            {
                Setters =
                {
                    new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
                    new Setter(Control.PaddingProperty, new Thickness(0)),
                    new Setter(Control.MinHeightProperty, 0d),
                    new Setter(Control.IsTabStopProperty, false)
                }
            }
        };
        // A series changes many sibling rows at once. Keep the native header's own
        // interaction, but do not queue an entrance/add-delete transition per release.
        if (page == "catalog")
        {
            list.ItemContainerTransitions = new TransitionCollection();
            // The SDK's four-viewport cache eagerly rebuilds many offscreen action
            // cards when releases are inserted ahead of an already expanded series.
            // Retain native recycling and one viewport of scroll prefetch instead.
            list.ItemsPanel = (ItemsPanelTemplate)XamlReader.Load("""
                <ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                  <ItemsStackPanel Orientation="Vertical" CacheLength="1" />
                </ItemsPanelTemplate>
                """);
        }
        ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(list, T(page == "catalog" ? "Available Python versions" : "Installed Python versions"));
        list.ContainerContentChanging += (_, args) =>
        {
            if (args.ItemContainer.ContentTemplateRoot is not ContentControl host) return;
            if (args.InRecycleQueue) { host.Content = null; host.Tag = null; return; }
            if (args.Item is not RuntimeListEntry entry) return;
            if (!ReferenceEquals(host.Tag, entry))
            {
                host.Content = RenderRuntimeEntry(entry);
                host.Tag = entry;
            }
            args.Handled = true;
        };
        list.Loaded += (_, _) =>
        {
            if (GetRuntimeScroll() is { } scroll) scroll.Tag = "RuntimeScroll";
        };
        return list;
    }

    private ScrollViewer? GetRuntimeScroll() => runtimeRows is null ? null : Descendants(runtimeRows).OfType<ScrollViewer>().FirstOrDefault();

    private UIElement RenderRuntimeEntry(RuntimeListEntry entry)
    {
        FrameworkElement view;
        if (entry.Content is FrameworkElement content) view = content;
        else if (entry.Heading is { } heading)
        {
            view = palette.Label(heading, palette.Tokens.SectionTitleSize, true);
            view.Margin = new(0, heading == "All versions" ? 12 : 0, 0, 8);
        }
        else if (entry.Runtime is { } runtime)
        {
            var card = RuntimeCard(runtime, entry.Online, entry.InGroup, entry.Recommended, entry.Installed);
            if (entry.Recommended) card.Tag = "RecommendedRuntime";
            if (entry.InGroup)
            {
                card.CornerRadius = palette.CatalogSegmentCorner(false, entry.Last);
                if (palette.Tokens.Design == "Material")
                {
                    card.Background = Palette.Brush(palette.Tokens.SurfaceContainer);
                    card.BorderThickness = new(palette.Tokens.HighContrast ? 1 : 0);
                }
                card.Margin = new(0, 0, 0, 2);
            }
            else card.Margin = new(0, 0, 0, 6);
            view = card;
        }
        else if (entry.Series is { } series)
        {
            var expanded = expandedSeries.Contains(series) || search.Length > 0 && !searchCollapsedSeries.Contains(series);
            var header = palette.Expander("Python " + series, null, expanded);
            header.Tag = "CatalogSeries:" + series;
            header.Padding = new(0);
            palette.ApplyCatalogSegment(header, entry.First, entry.Last, expanded);
            header.Margin = new(0, 0, 0, palette.Tokens.Design == "Material" ? 2 : 8);
            void SetExpanded(bool open)
            {
                if (reconcilingRuntimeEntries || runtimeRows is null || !header.IsLoaded) return;
                if (open) { expandedSeries.Add(series); searchCollapsedSeries.Remove(series); }
                else
                {
                    expandedSeries.Remove(series); searchCollapsedSeries.Add(series);
                }
                // Keep the native header alive and focused while only its sibling rows change.
                PopulateRuntimes();
                // Explicit collapse returns focus to its surviving native header. Releases
                // are siblings, so the Expander cannot do this through its own Content.
                // Wait for both the native collapse and ListView focus relocation to finish.
                if (!open) header.DispatcherQueue.TryEnqueue(() =>
                {
                    if (header.IsLoaded && !header.IsExpanded)
                        (Descendants(header).OfType<ToggleButton>().FirstOrDefault() as Control ?? header).Focus(FocusState.Keyboard);
                });
            }
            header.Expanding += (_, _) => SetExpanded(true);
            header.Collapsed += (_, _) => SetExpanded(false);
            view = header;
        }
        else view = new Border();
        // The scrollbar belongs to the full workspace; padding belongs to each realized row.
        return new Border { Child = view, Padding = new(0, 0, 28, 0), Tag = "RuntimeRowGutter" };
    }

    private void UpdateRuntimeEntries(IReadOnlyList<RuntimeListEntry> entries)
    {
        if (runtimeRows is null) return;
        var list = runtimeRows;
        var scroll = GetRuntimeScroll();
        RuntimeListEntry? anchor = null;
        double anchorY = 0;
        if (scroll is not null && list.ItemsPanelRoot is ItemsStackPanel panel && panel.FirstVisibleIndex >= 0)
        {
            // Query only the visible range, not every offscreen item preceding it.
            var lastVisible = Math.Min(panel.LastVisibleIndex, runtimeEntries.Count - 1);
            for (var index = panel.FirstVisibleIndex; index <= lastVisible; index++)
            {
                if (list.ContainerFromIndex(index) is not FrameworkElement container) continue;
                var bounds = container.TransformToVisual(list).TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                if (bounds.Bottom <= 0) continue;
                anchor = runtimeEntries[index]; anchorY = bounds.Top; break;
            }
        }
        reconcilingRuntimeEntries = true;
        try
        {
            var desiredKeys = entries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
            for (var index = runtimeEntries.Count - 1; index >= 0; index--)
                if (!desiredKeys.Contains(runtimeEntries[index].Key)) runtimeEntries.RemoveAt(index);
            var retainedKeys = runtimeEntries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
            for (var index = 0; index < entries.Count; index++)
            {
                var next = entries[index];
                if (index < runtimeEntries.Count && runtimeEntries[index].Key == next.Key)
                {
                    UpdateRetainedRuntimeEntry(index, next);
                    continue;
                }
                var oldIndex = -1;
                // Newly expanded releases cannot be in the retained tail. Avoid a
                // full tail scan for each insertion while preserving native notifications.
                if (retainedKeys.Contains(next.Key))
                    for (var candidate = index + 1; candidate < runtimeEntries.Count; candidate++)
                        if (runtimeEntries[candidate].Key == next.Key) { oldIndex = candidate; break; }
                if (oldIndex >= 0) runtimeEntries.Move(oldIndex, index);
                else runtimeEntries.Insert(index, next);
                UpdateRetainedRuntimeEntry(index, next);
            }
        }
        finally { reconcilingRuntimeEntries = false; }
        if (anchor is null || scroll is null) return;
        var retained = runtimeEntries.FirstOrDefault(entry => entry.Key == anchor.Key);
        if (retained is null) { scroll.ChangeView(null, 0, null, true); return; }
        list.UpdateLayout();
        if (list.ContainerFromItem(retained) is FrameworkElement retainedContainer)
        {
            var y = retainedContainer.TransformToVisual(list).TransformPoint(new Point()).Y;
            if (Math.Abs(y - anchorY) > 1) scroll.ChangeView(null, Math.Max(0, scroll.VerticalOffset + y - anchorY), null, true);
        }
    }

    private void UpdateRetainedRuntimeEntry(int index, RuntimeListEntry next)
    {
        var retained = runtimeEntries[index];
        if (next.Runtime is null && next.Series is not null)
        {
            // Keep the same header and its keyboard focus even if its data index moved.
            if (retained.First == next.First && retained.Last == next.Last && retained.Expanded == next.Expanded) return;
            retained.First = next.First; retained.Last = next.Last; retained.Expanded = next.Expanded;
            if (runtimeRows?.ContainerFromIndex(index) is ListViewItem item)
                foreach (var header in Descendants(item).OfType<Expander>())
                {
                    if (header.IsExpanded != next.Expanded) header.IsExpanded = next.Expanded;
                    palette.ApplyCatalogSegment(header, next.First, next.Last, next.Expanded);
                }
        }
        else if (retained != next) runtimeEntries[index] = next;
    }

    private void SizeInstallAction(Button button, bool recommended)
    {
        // Both localized states participate in live XAML measurement, including system text
        // scaling. The invisible sizing peer is excluded from control/content automation views.
        if (button.Content is not UIElement current) return;
        var alternate = palette.Action(AutomationProperties.GetName(button) == T("Install") ? "Installed" : "Install",
            "\uE896", compact: !recommended, role: ActionRole.Secondary);
        var measuringContent = (FrameworkElement)alternate.Content;
        alternate.Content = null;
        measuringContent.Opacity = 0;
        measuringContent.IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(measuringContent, AccessibilityView.Raw);
        foreach (var element in Descendants(measuringContent)) AutomationProperties.SetAccessibilityView(element, AccessibilityView.Raw);
        var sizing = new Grid();
        sizing.Children.Add(measuringContent); sizing.Children.Add(current);
        button.Content = sizing;
        button.BorderThickness = new(palette.Tokens.Design == "Material" && !palette.Tokens.HighContrast ? 0 : 1);
        button.Resources["RuntimeInstallAction"] = true;
    }
}
