using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;
using System.Diagnostics;
using System.Text.Json;
using Windows.Foundation;
using Windows.Graphics;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private sealed record ExpansionSample(int Iteration, bool Warmup, string Action, double UiaSynchronousMilliseconds,
        double VisibleReadyMilliseconds, int RealizedContainers, int RealizedRuntimeRows, int VisibleTargetRows,
        int ModelEntries, bool HeaderIdentityPreserved, bool FocusOnHeader);

    private async Task CheckExpansionPerformanceAsync(string directory, List<string> checks)
    {
        if (smokeDirectory is null) throw new IOException("Expansion benchmark requires an isolated smoke profile");
        const int warmups = 2, repetitions = 6;
        var originalPreferences = preferences; var originalCatalog = catalog; var originalInstalled = installed;
        var originalSize = AppWindow.Size; var originalPage = page; var originalExpanded = expandedSeries.ToArray();
        var animationsEnabled = systemUi.AnimationsEnabled;
        var samples = new List<object>(); var scenarios = new List<object>();
        var completed = false;
        try
        {
            // The probe starts from Root.Loaded; let the native startup tree finish
            // attaching before replacing its presentation, as the recording probe does.
            await Task.Delay(250); Root.UpdateLayout();
            ApplySmokePreferences(preferences with { Design = "Material", Theme = "Dark", Language = "zh-CN", Transparency = "Off",
                MaterialColorSource = "Custom", MaterialColorStyle = "TonalSpot", MaterialSeed = 0xFF6750A4u,
                CatalogSource = "Online", DefaultArchitecture = "x64", CatalogPackageType = "Standard", ShowPreviewReleases = false });
            var scale = Root.XamlRoot.RasterizationScale;
            AppWindow.Resize(new SizeInt32((int)(1180 * scale), (int)(850 * scale)));
            foreach (var count in new[] { 25, 250, 1000 })
            foreach (var laterOpen in new[] { false, true })
            {
                var scenario = laterOpen ? "expand-front-with-later-group-open" : "ordinary-expand-collapse";
                catalog = new[] { "3.14", "3.13" }.SelectMany(series => Enumerable.Range(0, count).Select(micro =>
                    new PythonRuntime("expansion-" + series + "-64", "PythonCore", series + "-64", series + "." + micro,
                        "Python " + series + "." + micro, "", "", IsDefault: false, IsManaged: true))).ToArray();
                installed = [catalog[count - 1] with { IsDefault = true }];
                expandedSeries.Clear(); if (laterOpen) expandedSeries.Add("3.13");
                Navigate("catalog"); Root.UpdateLayout();
                await WaitForSmokeConditionAsync(() => runtimeRows?.IsLoaded == true && Descendants(runtimeRows).OfType<Expander>()
                    .Any(header => header.Tag as string == "CatalogSeries:3.14" && header.IsLoaded), "Expansion benchmark header did not load");
                palette.UpdateMotion(Root, animationsEnabled);
                await Task.Delay(600); Root.UpdateLayout();
                var list = runtimeRows!; var pageVisual = PageHost.Children.Single();
                var scroll = GetRuntimeScroll() ?? throw new IOException("Expansion benchmark has no native viewport");
                var header = Descendants(list).OfType<Expander>().Single(header => header.Tag as string == "CatalogSeries:3.14");
                var headerEntry = runtimeEntries.Single(entry => entry.Series == "3.14" && entry.Runtime is null);
                var nativeHeader = Descendants(header).OfType<ToggleButton>().Single(part => part.Name == "ExpanderHeader");
                var peer = FrameworkElementAutomationPeer.FromElement(header) ?? FrameworkElementAutomationPeer.CreatePeerForElement(header);
                var provider = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);
                var raw = new List<ExpansionSample>();

                int TargetEntries() => runtimeEntries.Count(entry => entry.Runtime is { } runtime && entry.InGroup && RuntimeCatalog.MinorSeries(runtime) == "3.14");
                // ContainerContentChanging writes the production entry into this template
                // host's Tag. ListViewItem.Content is not the entry contract for this template.
                RuntimeListEntry? EntryOf(ListViewItem container) =>
                    (container.ContentTemplateRoot as ContentControl)?.Tag as RuntimeListEntry;
                // Inspect only the actual realized InGroup hosts: the recommendation can
                // carry the same runtime identity and must not satisfy group readiness.
                Border[] VisibleTargetRows() => Descendants(list).OfType<ListViewItem>().Where(container =>
                    container.IsLoaded && EntryOf(container) is { InGroup: true, Runtime: { } runtime } && RuntimeCatalog.MinorSeries(runtime) == "3.14")
                    .SelectMany(container => Descendants(container).OfType<Border>())
                    .Where(row => row.Tag is PythonRuntime && row.IsLoaded && row.ActualHeight > 0 && IntersectsExpansionViewport(row, scroll)).ToArray();
                async Task DumpFailure(string reason, int iteration, bool expand)
                {
                    object BoundsOf(FrameworkElement element)
                    {
                        try
                        {
                            var bounds = element.TransformToVisual(scroll).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                            return new { left = bounds.Left, top = bounds.Top, width = bounds.Width, height = bounds.Height,
                                element.IsLoaded, element.Visibility, element.Opacity };
                        }
                        catch (Exception error) { return new { boundsError = error.Message }; }
                    }
                    object RowDescription(Border row) => new
                    {
                        tagType = row.Tag?.GetType().FullName,
                        tag = row.Tag is PythonRuntime runtime ? runtime.Id + "@" + runtime.Version : row.Tag?.ToString(),
                        bounds = BoundsOf(row)
                    };
                    var containers = Descendants(list).OfType<ListViewItem>().Select(container =>
                    {
                        var host = container.ContentTemplateRoot as ContentControl;
                        var entry = EntryOf(container);
                        return new
                        {
                            index = list.IndexFromContainer(container), containerType = container.GetType().FullName,
                            contentType = container.Content?.GetType().FullName, tagType = container.Tag?.GetType().FullName,
                            dataContextType = container.DataContext?.GetType().FullName,
                            templateRootType = container.ContentTemplateRoot?.GetType().FullName,
                            hostContentType = host?.Content?.GetType().FullName, hostTagType = host?.Tag?.GetType().FullName,
                            entryKey = entry?.Key, entryInGroup = entry?.InGroup, entryRecommended = entry?.Recommended,
                            entryVersion = entry?.Runtime?.Version, bounds = BoundsOf(container),
                            rows = Descendants(container).OfType<Border>().Where(row => row.Tag is PythonRuntime || row.Tag as string == "RecommendedRuntime")
                                .Select(RowDescription).ToArray()
                        };
                    }).ToArray();
                    await File.WriteAllTextAsync(Path.Combine(directory, "expansion-failure.json"), JsonSerializer.Serialize(new
                    {
                        reason, releasesPerGroup = count, scenario, iteration, action = expand ? "expand" : "collapse",
                        state = provider.ExpandCollapseState.ToString(), modelCount = runtimeEntries.Count,
                        targetEntryCount = TargetEntries(), visibleTargetCount = VisibleTargetRows().Length,
                        scroll.VerticalOffset, scroll.ViewportHeight, scroll.ScrollableHeight,
                        headerBounds = BoundsOf(header),
                        firstEntries = runtimeEntries.Take(20).Select(entry => new { entry.Key, entry.InGroup, entry.Recommended, entry.Series, version = entry.Runtime?.Version }).ToArray(),
                        containers,
                        allRuntimeRows = Descendants(list).OfType<Border>().Where(row => row.Tag is PythonRuntime || row.Tag as string == "RecommendedRuntime")
                            .Select(RowDescription).ToArray()
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                bool IdentityPreserved() => ReferenceEquals(list, runtimeRows) && ReferenceEquals(pageVisual, PageHost.Children.Single()) && header.IsLoaded &&
                    ReferenceEquals(header, Descendants(list).OfType<Expander>().SingleOrDefault(candidate => candidate.Tag as string == "CatalogSeries:3.14"));
                bool HeaderFocused() => ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), nativeHeader) ||
                    Descendants(nativeHeader).Any(element => ReferenceEquals(element, FocusManager.GetFocusedElement(Root.XamlRoot)));
                bool Ready(bool expanded)
                {
                    if (!IdentityPreserved() || header.IsExpanded != expanded || provider.ExpandCollapseState !=
                        (expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed)) return false;
                    if (TargetEntries() != (expanded ? count : 0)) return false;
                    var visible = VisibleTargetRows();
                    return expanded
                        ? visible.Any(row => ((PythonRuntime)row.Tag).Version == $"3.14.{count - 1}" && FullyWithin(row, scroll))
                        : visible.Length == 0;
                }
                void RequireInvariant()
                {
                    if (!IdentityPreserved() || VisibleRuntimeCount != count * 2 || list.ItemsPanelRoot is not ItemsStackPanel)
                        throw new IOException($"Expansion changed header/list/page identity, result count or virtualization: {count}/{scenario}");
                    if (systemUi.AnimationsEnabled != animationsEnabled)
                        throw new IOException("System animation preference changed during the expansion benchmark");
                    var laterCount = runtimeEntries.Count(entry => entry.InGroup && entry.Runtime is { } runtime && RuntimeCatalog.MinorSeries(runtime) == "3.13");
                    if (laterCount != (laterOpen ? count : 0)) throw new IOException("Expanding the front group disturbed the later group");
                    var realized = Descendants(list).OfType<ListViewItem>().Count(item => item.IsLoaded);
                    if (count >= 250 && realized >= count) throw new IOException("Expansion instantiated an unbounded number of row containers");
                }

                async Task<ExpansionSample> Measure(bool expand, int iteration, bool warmup)
                {
                    // Real layout/render notifications avoid the 50 ms quantization of the
                    // general smoke waiter. Subscribe before UIA so synchronous work is included.
                    var timer = new Stopwatch();
                    var ready = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var returned = false;
                    void CheckReady()
                    {
                        if (!returned || ready.Task.IsCompleted) return;
                        try { if (Ready(expand)) ready.TrySetResult(timer.Elapsed.TotalMilliseconds); }
                        catch (Exception error) { ready.TrySetException(error); }
                    }
                    EventHandler<object> onLayout = (_, _) => CheckReady();
                    EventHandler<object> onRender = (_, _) => CheckReady();
                    Root.LayoutUpdated += onLayout; CompositionTarget.Rendering += onRender;
                    double synchronous;
                    double visibleReady;
                    try
                    {
                        timer.Start();
                        if (expand) provider.Expand(); else provider.Collapse();
                        synchronous = timer.Elapsed.TotalMilliseconds;
                        returned = true;
                        Root.UpdateLayout(); CheckReady();
                        using var timeoutCancellation = new CancellationTokenSource();
                        var timeout = Task.Delay(TimeSpan.FromSeconds(10), timeoutCancellation.Token);
                        if (await Task.WhenAny(ready.Task, timeout) != ready.Task)
                            throw new IOException($"Expansion readiness timed out: {count}/{scenario}/{(expand ? "expand" : "collapse")}; UIA={provider.ExpandCollapseState}, targetEntries={TargetEntries()}, visibleRows={VisibleTargetRows().Length}, identity={IdentityPreserved()}");
                        visibleReady = await ready.Task;
                        timeoutCancellation.Cancel();
                    }
                    catch (Exception error)
                    {
                        await DumpFailure(error.Message, iteration, expand);
                        throw;
                    }
                    finally { Root.LayoutUpdated -= onLayout; CompositionTarget.Rendering -= onRender; }
                    // Focus recovery is correctness validation outside the visibility timer.
                    if (!expand) await WaitForSmokeConditionAsync(HeaderFocused, "Collapse did not restore focus to its retained native header");
                    RequireInvariant();
                    return new(iteration, warmup, expand ? "expand" : "collapse", synchronous, visibleReady,
                        Descendants(list).OfType<ListViewItem>().Count(item => item.IsLoaded),
                        Descendants(list).OfType<Border>().Count(row => row.Tag is PythonRuntime && row.IsLoaded),
                        VisibleTargetRows().Length, runtimeEntries.Count, IdentityPreserved(), HeaderFocused());
                }

                for (var iteration = 0; iteration < warmups + repetitions; iteration++)
                {
                    list.ScrollIntoView(headerEntry); Root.UpdateLayout();
                    await WaitForSmokeConditionAsync(() => FullyWithin(header, scroll), "Benchmark group header is outside the viewport");
                    if (!nativeHeader.Focus(FocusState.Keyboard) || !HeaderFocused()) throw new IOException("Expansion benchmark header rejected keyboard focus");
                    var warmup = iteration < warmups;
                    var expanded = await Measure(true, iteration, warmup);
                    raw.Add(expanded); samples.Add(new { releasesPerGroup = count, catalogCount = count * 2, scenario, sample = expanded });
                    // Keep repeated samples independent of an interrupted native chevron/state
                    // transition. This settling delay is deliberately outside both timing clocks.
                    await Task.Delay(600); Root.UpdateLayout();
                    var action = VisibleTargetRows().Where(row => FullyWithin(row, scroll)).SelectMany(row => Descendants(row).OfType<Button>())
                        .FirstOrDefault(button => button.IsLoaded && button.IsEnabled && button.Flyout is not null);
                    if (action is null || !action.Focus(FocusState.Keyboard) || !ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), action))
                        throw new IOException("Collapse benchmark could not focus a realized release action");
                    var collapsed = await Measure(false, iteration, warmup);
                    raw.Add(collapsed); samples.Add(new { releasesPerGroup = count, catalogCount = count * 2, scenario, sample = collapsed });
                    await Task.Delay(600); Root.UpdateLayout();
                    if (laterOpen)
                    {
                        var laterHeader = Descendants(list).OfType<Expander>().SingleOrDefault(candidate => candidate.Tag as string == "CatalogSeries:3.13");
                        if (laterHeader is null || !laterHeader.IsExpanded) throw new IOException("Later group's native expanded header was not retained");
                        var laterPeer = FrameworkElementAutomationPeer.FromElement(laterHeader) ?? FrameworkElementAutomationPeer.CreatePeerForElement(laterHeader);
                        if (((IExpandCollapseProvider)laterPeer.GetPattern(PatternInterface.ExpandCollapse)).ExpandCollapseState != ExpandCollapseState.Expanded)
                            throw new IOException("Later group's UIA expansion state changed");
                    }
                }
                object Summary(string action)
                {
                    var values = raw.Where(sample => !sample.Warmup && sample.Action == action).ToArray();
                    var synchronous = values.Select(sample => sample.UiaSynchronousMilliseconds).Order().ToArray();
                    var visible = values.Select(sample => sample.VisibleReadyMilliseconds).Order().ToArray();
                    return new { action, sampleCount = values.Length, synchronousMedianMilliseconds = Median(synchronous), synchronousP95Milliseconds = P95(synchronous),
                        visibleReadyMedianMilliseconds = Median(visible), visibleReadyP95Milliseconds = P95(visible), maxRealizedContainers = values.Max(sample => sample.RealizedContainers) };
                }
                scenarios.Add(new { releasesPerGroup = count, catalogCount = count * 2, scenario,
                    actualRootWidthDip = Root.ActualWidth, actualRootHeightDip = Root.ActualHeight, viewportHeightDip = scroll.ViewportHeight,
                    summaries = new[] { Summary("expand"), Summary("collapse") } });
                checks.Add($"Expansion benchmark: Material/{count} releases per group/{scenario}, 2 warmup + 6 measured native UIA expand/collapse cycles; viewport, header/page identity, later-group state and child-focus recovery passed. Raw synchronous/visible-ready timings are in expansion-performance.json.");
            }
            completed = true;
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "expansion-performance.json"), JsonSerializer.Serialize(new
            {
                completed, design = "Material", theme = "Dark", language = "zh-CN", requestedWindowWidthDip = 1180, requestedWindowHeightDip = 850,
                seed = "FF6750A4", colorStyle = "TonalSpot", transparency = "Off", systemAnimationsEnabled = animationsEnabled,
                rasterizationScale = Root.XamlRoot.RasterizationScale, textScaleFactor = systemUi.TextScaleFactor,
                warmupCycles = warmups, measuredCycles = repetitions,
                settleBetweenActionsMilliseconds = 600,
                timingMethod = "Stopwatch around native UIA call; visibility-ready observed through real layout/render notifications, including one explicit UpdateLayout. No fixed polling in timed region. Focus validation is outside visibility timing.",
                visibilityCriterion = "Native UIA state and model entry count match the target; expansion has the newest target release fully inside the real viewport, collapse has no target release intersecting it. This measures usable first-row feedback, not a compositor frame-time trace.",
                percentileMethod = "Nearest-rank P95; with six measured samples P95 equals the maximum. Median averages the central two values.",
                baselineComparison = "No automatic performance verdict; compare the same harness and settings against an independently built baseline on the same machine.",
                scenarios, samples
            }, new JsonSerializerOptions { WriteIndented = true }));
            CancelSearchRefresh(); installed = originalInstalled; catalog = originalCatalog;
            expandedSeries.Clear(); foreach (var series in originalExpanded) expandedSeries.Add(series);
            AppWindow.Resize(originalSize); ApplySmokePreferences(originalPreferences); Navigate(originalPage);
        }
        static double Median(double[] values) => (values[(values.Length - 1) / 2] + values[values.Length / 2]) / 2;
        static double P95(double[] values) => values[Math.Clamp((int)Math.Ceiling(values.Length * .95) - 1, 0, values.Length - 1)];
    }

    private static bool IntersectsExpansionViewport(FrameworkElement element, FrameworkElement viewport)
    {
        var bounds = element.TransformToVisual(viewport).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return bounds.Height > 0 && bounds.Bottom > 0 && bounds.Top < viewport.ActualHeight;
    }
}
