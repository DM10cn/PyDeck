using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using PimGui.Core;
using System.Diagnostics;
using Windows.Foundation;
using Windows.Graphics;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckVirtualizedRuntimeListsAsync(List<string> checks)
    {
        var originalPreferences = preferences; var originalCatalog = catalog; var originalInstalled = installed;
        var originalExpanded = expandedSeries.ToArray(); var originalPage = page; var originalSize = AppWindow.Size;
        var originalActivityFilter = activityFilter;
        try
        {
            var candidates = Enumerable.Range(0, 1000).Select(index => new PythonRuntime("virtualization-" + index,
                "PythonCore", "3.14-64", "3.14." + index, "Python 3.14." + index, "", "", IsDefault: false, IsManaged: true)).ToArray();
            var scale = Root.XamlRoot.RasterizationScale;
            AppWindow.Resize(new SizeInt32((int)(1180 * scale), (int)(850 * scale)));
            foreach (var design in new[] { "Fluent", "Material" })
            {
                catalog = candidates; installed = [candidates[999]]; expandedSeries.Clear(); expandedSeries.Add("3.14");
                ApplySmokePreferences(preferences with { Design = design, Theme = "Dark", Language = "en-US", Transparency = "Off",
                    CatalogSource = "Online", DefaultArchitecture = "x64", CatalogPackageType = "Standard", ShowPreviewReleases = false });
                var retainedBefore = GC.GetTotalMemory(false);
                var initial = Stopwatch.StartNew(); Navigate("catalog"); Root.UpdateLayout();
                await WaitForSmokeConditionAsync(() => runtimeRows?.IsLoaded == true && RealizedRuntimeRows().Any(), "Virtualized catalog did not realize its first page");
                initial.Stop();
                var list = runtimeRows!; var pageVisual = PageHost.Children.Single();
                var editor = Descendants(PageHost).OfType<AutoSuggestBox>().Single();
                var scroll = GetRuntimeScroll() ?? throw new IOException("Virtualized catalog has no native scroll viewer");
                RequireRecycling(list, 1000, design + "/catalog");
                if (VisibleRuntimeCount != 1000 || runtimeEntries.Count(entry => entry.InGroup && entry.Runtime is not null) != 1000)
                    throw new IOException("Virtualization changed the catalog result count");
                var maximumRealized = RealizedRuntimeRows().Length;
                foreach (var index in new[] { 500, 3, 750, 400, 999 })
                {
                    var entry = runtimeEntries.Single(entry => entry.InGroup && entry.Runtime?.Id == candidates[index].Id);
                    list.ScrollIntoView(entry); Root.UpdateLayout();
                    await WaitForSmokeConditionAsync(() => RealizedRuntimeRows().Any(row => row.Tag is PythonRuntime runtime && runtime.Id == candidates[index].Id && FullyWithin(row, scroll)),
                        "ScrollIntoView did not realize the requested release " + index);
                    RequireRecycling(list, 1000, design + "/catalog/" + index);
                    maximumRealized = Math.Max(maximumRealized, RealizedRuntimeRows().Length);
                    foreach (var row in RealizedRuntimeRows())
                    {
                        var runtime = (PythonRuntime)row.Tag;
                        var expectedInstalled = runtime.Id == candidates[999].Id;
                        var action = Descendants(row).OfType<Button>().Single(button =>
                            AutomationProperties.GetName(button) == T("Install") || AutomationProperties.GetName(button) == T("Installed"));
                        if ((AutomationProperties.GetName(action) == T("Installed")) != expectedInstalled)
                            throw new IOException("Recycled release retained another runtime's installed status");
                    }
                }

                // A data refresh at a scrolled position must preserve the actual visible row and offset.
                var anchorEntry = runtimeEntries.Single(entry => entry.InGroup && entry.Runtime?.Id == candidates[500].Id);
                list.ScrollIntoView(anchorEntry); Root.UpdateLayout();
                await WaitForSmokeConditionAsync(() => RealizedRuntimeRows().Any(row => row.Tag is PythonRuntime runtime && runtime.Id == candidates[500].Id && FullyWithin(row, scroll)),
                    "Anchor fixture did not reach its middle release");
                var anchor = RealizedRuntimeRows().First(row => FullyWithin(row, scroll));
                var anchorId = ((PythonRuntime)anchor.Tag).Id;
                var anchorY = anchor.TransformToVisual(scroll).TransformPoint(new Point()).Y;
                installed = [candidates[999], candidates[250]]; PopulateRuntimes(); Root.UpdateLayout();
                await Task.Delay(80); Root.UpdateLayout();
                var retained = RealizedRuntimeRows().SingleOrDefault(row => ((PythonRuntime)row.Tag).Id == anchorId);
                if (retained is null || Math.Abs(retained.TransformToVisual(scroll).TransformPoint(new Point()).Y - anchorY) > 2 ||
                    !ReferenceEquals(list, runtimeRows) || !ReferenceEquals(pageVisual, PageHost.Children.Single()))
                    throw new IOException("Installed-state refresh displaced the catalog anchor or replaced the viewport");

                var headerEntry = runtimeEntries.Single(entry => entry.Series == "3.14" && entry.Runtime is null);
                list.ScrollIntoView(headerEntry); Root.UpdateLayout();
                await WaitForSmokeConditionAsync(() => Descendants(list).OfType<Expander>().Any(expander => expander.Tag as string == "CatalogSeries:3.14" && expander.IsLoaded),
                    "Series header was not realized");
                var header = Descendants(list).OfType<Expander>().Single(expander => expander.Tag as string == "CatalogSeries:3.14");
                var headerPeer = FrameworkElementAutomationPeer.FromElement(header) ?? FrameworkElementAutomationPeer.CreatePeerForElement(header);
                var provider = (IExpandCollapseProvider)headerPeer.GetPattern(PatternInterface.ExpandCollapse);
                var expandedHeaderHeight = header.ActualHeight;
                Button? childAction = null;
                await WaitForSmokeConditionAsync(() =>
                {
                    childAction = RealizedRuntimeRows().Where(row => FullyWithin(row, scroll))
                        .SelectMany(row => Descendants(row).OfType<Button>())
                        .FirstOrDefault(button => button.IsLoaded && button.IsEnabled && button.Flyout is not null);
                    return childAction is not null;
                }, "Collapse focus fixture has no realized, enabled release action");
                if (!childAction!.Focus(FocusState.Keyboard) || !ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), childAction))
                    throw new IOException("Collapse focus fixture did not focus the release's native action");
                provider.Collapse(); Root.UpdateLayout();
                await WaitForSmokeConditionAsync(() => !runtimeEntries.Any(entry => entry.InGroup), "Collapsing did not remove flattened release entries");
                if (!header.IsLoaded || !ReferenceEquals(header, Descendants(list).OfType<Expander>().SingleOrDefault(expander => expander.Tag as string == "CatalogSeries:3.14")))
                    throw new IOException("Collapsing a focused release replaced its series header");
                var nativeHeader = Descendants(header).OfType<ToggleButton>().Single(part => part.Name == "ExpanderHeader");
                await WaitForSmokeConditionAsync(() => ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), nativeHeader) ||
                    Descendants(nativeHeader).Any(element => ReferenceEquals(element, FocusManager.GetFocusedElement(Root.XamlRoot))),
                    "Collapsed series did not receive its deferred native header focus: " + design);
                var focusAfterCollapse = FocusManager.GetFocusedElement(Root.XamlRoot);
                if (!ReferenceEquals(focusAfterCollapse, nativeHeader) && !Descendants(nativeHeader).Any(element => ReferenceEquals(element, focusAfterCollapse)))
                    throw new IOException($"Collapsing a focused release did not return keyboard focus to its native series header: {design}; focused={focusAfterCollapse?.GetType().Name}/{(focusAfterCollapse as FrameworkElement)?.Name}; headerLoaded={header.IsLoaded}");
                if (VisibleRuntimeCount != 1000 || Descendants(header).OfType<Border>().Any(row => row.Tag is PythonRuntime))
                    throw new IOException("Series header changed result semantics or owns eager release visuals");
                if (provider.ExpandCollapseState != ExpandCollapseState.Collapsed || header.Content is not null || Math.Abs(header.ActualHeight - expandedHeaderHeight) > 1)
                    throw new IOException("Native catalog header reserves empty expansion space or reports an incorrect collapsed state");
                provider.Expand(); Root.UpdateLayout();
                await WaitForSmokeConditionAsync(() => runtimeEntries.Count(entry => entry.InGroup) == 1000, "Native expansion did not restore release entries");
                if (provider.ExpandCollapseState != ExpandCollapseState.Expanded)
                    throw new IOException("Native catalog header did not report its expanded state to UI Automation");

                provider.Collapse(); Root.UpdateLayout();
                await WaitForSmokeConditionAsync(() => !runtimeEntries.Any(entry => entry.InGroup), "Search expansion fixture could not collapse its group");
                editor.Text = "3.14.999";
                await WaitForSmokeConditionAsync(() => VisibleRuntimeCount == 1 && header.IsExpanded && runtimeEntries.Count(entry => entry.InGroup) == 1,
                    "Search results did not synchronize a reused header's native expanded state");
                if (provider.ExpandCollapseState != ExpandCollapseState.Expanded)
                    throw new IOException("Search auto-expansion left stale UI Automation collapse state");
                editor.Text = "";
                await WaitForSmokeConditionAsync(() => VisibleRuntimeCount == 1000 && !header.IsExpanded && !runtimeEntries.Any(entry => entry.InGroup),
                    "Clearing search did not restore the user's collapsed series state");
                provider.Expand(); Root.UpdateLayout();
                await WaitForSmokeConditionAsync(() => runtimeEntries.Count(entry => entry.InGroup) == 1000, "Search fixture did not restore explicit expansion");

                var filterTimes = new List<double>();
                for (var sample = 0; sample < 5; sample++)
                {
                    var clock = Stopwatch.StartNew(); editor.Text = "3.14.999";
                    await WaitForSmokeConditionAsync(() => VisibleRuntimeCount == 1 && RealizedRuntimeRows().Any(row => ((PythonRuntime)row.Tag).Version == "3.14.999"),
                        "Long-list search did not realize the final query");
                    clock.Stop(); filterTimes.Add(clock.Elapsed.TotalMilliseconds);
                    if (!ReferenceEquals(pageVisual, PageHost.Children.Single()) || !ReferenceEquals(editor, Descendants(PageHost).OfType<AutoSuggestBox>().Single()))
                        throw new IOException("Long-list filtering rebuilt its controls");
                    editor.Text = "";
                    await WaitForSmokeConditionAsync(() => VisibleRuntimeCount == 1000 && runtimeEntries.Count(entry => entry.InGroup) == 1000,
                        "Clearing search lost the previously expanded group");
                    Root.UpdateLayout(); RequireRecycling(list, 1000, design + "/filter-clear");
                }
                var retainedDelta = GC.GetTotalMemory(false) - retainedBefore;
                checks.Add($"{design}: synthetic 1,000-candidate list keeps native recycling ({maximumRealized} maximum realized runtime rows in sampled scroll positions), preserves refresh anchor and expansion, returns child keyboard focus to its retained native header on collapse, clears filters; initial view {initial.Elapsed.TotalMilliseconds:F1} ms, median final-query response including debounce {filterTimes.Order().ElementAt(2):F1} ms, managed-memory snapshot delta {retainedDelta} bytes (no forced GC; not a baseline comparison).");

                activityFilter = null; activityLog.Clear();
                for (var index = 0; index < 1000; index++) activityLog.Add($"Virtual activity fixture {index:0000}", ActivityLevel.Information, ActivityOrigin.Application);
                Navigate("activity"); Root.UpdateLayout();
                await WaitForSmokeConditionAsync(() => activityList?.IsLoaded == true && activityRows.Count == 1000, "Activity fixture did not load");
                var activityViewport = Descendants(activityList!).OfType<ScrollViewer>().First();
                RequireRecycling(activityList!, 1000, design + "/activity");
                activityList!.ScrollIntoView(activityRows[500]); Root.UpdateLayout();
                await WaitForSmokeConditionAsync(() => activityList.ContainerFromItem(activityRows[500]) is FrameworkElement row && FullyWithin(row, activityViewport),
                    "Activity fixture did not reach its reading position");
                var activityAnchor = activityRows[500];
                var activityY = ((FrameworkElement)activityList.ContainerFromItem(activityAnchor)).TransformToVisual(activityViewport).TransformPoint(new Point()).Y;
                activityLog.Add("Appended while reading older output", ActivityLevel.Information, ActivityOrigin.Application); RefreshActivityOutput();
                await Task.Delay(80); Root.UpdateLayout();
                if (activityList.ContainerFromItem(activityAnchor) is not FrameworkElement retainedActivity ||
                    Math.Abs(retainedActivity.TransformToVisual(activityViewport).TransformPoint(new Point()).Y - activityY) > 2)
                    throw new IOException("New activity stole the reader's scroll position");
                activityList.ScrollIntoView(activityRows[^1]); Root.UpdateLayout();
                await WaitForSmokeConditionAsync(() => activityViewport.ScrollableHeight - activityViewport.VerticalOffset <= 2, "Activity could not reach its final row");
                activityLog.Add("Appended while following the latest output", ActivityLevel.Information, ActivityOrigin.Application); RefreshActivityOutput();
                await WaitForSmokeConditionAsync(() => activityList.ContainerFromItem(activityRows[^1]) is FrameworkElement last && FullyWithin(last, activityViewport),
                    "Activity at the bottom did not follow appended output");
                RequireRecycling(activityList, 1000, design + "/activity-appended");
                checks.Add($"{design}: synthetic 1,000-entry activity list recycles; appended output preserves an older reading position and follows the last row only when already at the end.");
            }
        }
        finally
        {
            CancelSearchRefresh(); catalog = originalCatalog; installed = originalInstalled;
            expandedSeries.Clear(); foreach (var series in originalExpanded) expandedSeries.Add(series);
            activityFilter = originalActivityFilter; activityLog.Clear(); AppWindow.Resize(originalSize);
            ApplySmokePreferences(originalPreferences); Navigate(originalPage);
        }
    }

    private Border[] RealizedRuntimeRows() => runtimeRows is null ? [] :
        Descendants(runtimeRows).OfType<Border>().Where(row => row.Tag is PythonRuntime && row.IsLoaded && row.ActualHeight > 0).ToArray();

    private static void RequireRecycling(ListView list, int sourceCount, string context)
    {
        var realized = Descendants(list).OfType<ListViewItem>().Count(item => item.IsLoaded);
        if (list.ItemsPanelRoot is not ItemsStackPanel || realized == 0 || realized >= sourceCount / 2)
            throw new IOException($"List is not recycling a bounded viewport: {realized}/{sourceCount}, panel {list.ItemsPanelRoot?.GetType().Name}: {context}");
    }
}
