using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;
using Windows.Foundation;
using Windows.Graphics;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckPageLayoutsAsync(string directory)
    {
        if (smokeDirectory is null || !Path.GetFullPath(store.DirectoryPath).StartsWith(Path.GetFullPath(smokeDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("UI fixtures require the isolated smoke preferences directory");
        var originalPreferences = preferences;
        var originalInstalled = installed;
        var originalCatalog = catalog;
        var originalPage = page;
        var originalExpanded = expandedSeries.ToArray();
        var originalWindowSize = AppWindow.Size;
        var originalActivityFilter = activityFilter;
        var phase = "historical-releases";
        var fixtureEnvironment = new VirtualEnvironment(Path.Combine(store.DirectoryPath, "layout-project", ".venv"), "Not checked", "3.14.7", store.DirectoryPath);
        var latest = new PythonRuntime("pythoncore-3.14-64", "PythonCore", "3.14-64", "3.14.7", "Python 3.14.7",
            Path.Combine(store.DirectoryPath, "python.exe"), store.DirectoryPath, true, true);
        var previous = latest with { Version = "3.14.6", DisplayName = "Python 3.14.6", IsDefault = false };
        var older = latest with { Version = "3.14.5", DisplayName = "Python 3.14.5", IsDefault = false };
        var anotherSeries = latest with { Id = "pythoncore-3.13-64", Tag = "3.13-64", Version = "3.13.15", DisplayName = "Python 3.13.15", IsDefault = false };
        var embedded = latest with { Id = "pythonembed-3.14-64", Company = "PythonEmbed", DisplayName = "Python 3.14.7 (embeddable)", IsDefault = false };
        try
        {
            installed = [latest, anotherSeries];
            catalog = [latest, previous, older, anotherSeries, embedded];
            expandedSeries.Clear();
            ApplySmokePreferences(preferences with { Language = "en-US", CatalogSource = "Online", DefaultArchitecture = "x64", CatalogPackageType = "Standard", ShowPreviewReleases = false });
            var fixtureScale = Root.XamlRoot.RasterizationScale;
            AppWindow.Resize(new SizeInt32((int)(1180 * fixtureScale), (int)(850 * fixtureScale)));
            Navigate("catalog"); Root.UpdateLayout();
            await WaitForSmokeConditionAsync(() => Descendants(PageHost).OfType<Expander>().Any(e => e.Tag as string == "CatalogSeries:3.14" && e.IsLoaded),
                "Historical catalog header was not realized");
            var series = Descendants(PageHost).OfType<Expander>().Single(e => e.Tag as string == "CatalogSeries:3.14");
            series.IsExpanded = true; Root.UpdateLayout();
            RuntimeListEntry[] HistoryEntries() => runtimeEntries.Where(entry => entry.InGroup && entry.Series == "3.14" && entry.Runtime is not null).ToArray();
            var expectedVersions = new[] { "3.14.7", "3.14.6", "3.14.5" };
            await WaitForSmokeConditionAsync(() => HistoryEntries().Length == expectedVersions.Length,
                "Expanded historical series did not expose exactly three group entries");
            var history = HistoryEntries();
            if (!history.Select(entry => entry.Runtime!.Version).SequenceEqual(expectedVersions) || history.Select(entry => entry.Key).Distinct().Count() != 3)
                throw new IOException("Historical micro releases sharing a PIM ID were hidden or ordered incorrectly");
            var list = runtimeRows!;
            Border? HistoryRow(RuntimeListEntry entry)
            {
                // Container.Content is not the item contract: ContainerContentChanging
                // places the entry on the ContentTemplateRoot host and the card below it.
                var container = list.ContainerFromItem(entry) as ListViewItem;
                if (container?.ContentTemplateRoot is not ContentControl { Tag: RuntimeListEntry mapped } host ||
                    !container.IsLoaded || !mapped.InGroup || mapped.Key != entry.Key) return null;
                return Descendants(host).OfType<Border>().SingleOrDefault(row => row.Tag is PythonRuntime runtime &&
                    runtime.Id == entry.Runtime!.Id && runtime.Version == entry.Runtime.Version && row.IsLoaded && row.ActualHeight > 0);
            }
            var viewport = GetRuntimeScroll() ?? throw new IOException("Historical catalog scroll viewport is missing");
            // The initial window is specified in physical pixels. Normalize this fixture
            // to DIP and bring its group into view instead of relying on cache length or
            // counting every Border across the page (including recycled containers).
            var renderedPositions = new List<(string Version, double ContentTop)>();
            for (var index = 0; index < history.Length; index++)
            {
                var entry = history[index];
                list.ScrollIntoView(entry, ScrollIntoViewAlignment.Leading); Root.UpdateLayout();
                await WaitForSmokeConditionAsync(() => HistoryRow(entry) is { } row && FullyWithin(row, viewport),
                    "Historical group row did not become fully visible: " + expectedVersions[index]);
                var row = HistoryRow(entry)!;
                renderedPositions.Add((((PythonRuntime)row.Tag).Version,
                    row.TransformToVisual(viewport).TransformPoint(new Point()).Y + viewport.VerticalOffset));
                var isInstalled = index == 0;
                var actions = Descendants(row).OfType<Button>().Where(button =>
                    AutomationProperties.GetName(button) == T("Installed") || AutomationProperties.GetName(button) == T("Install")).ToArray();
                if (history[index].Installed != isInstalled || actions.Length != 1 ||
                    AutomationProperties.GetName(actions[0]) != T(isInstalled ? "Installed" : "Install") ||
                    actions[0].IsEnabled != (!isInstalled && connected && client.SupportsMutations && CanWork(WorkKind.RuntimeMutation)))
                    throw new IOException("Installed status was not matched to exact historical micro version: " + expectedVersions[index]);
            }
            if (!renderedPositions.OrderBy(row => row.ContentTop).Select(row => row.Version).SequenceEqual(expectedVersions) ||
                renderedPositions.Select(row => row.ContentTop).Distinct().Count() != 3)
                throw new IOException("Realized historical rows do not follow descending micro-version order");
            if (Descendants(series).OfType<Expander>().Any() || Descendants(series).OfType<Border>().Any(row => row.Tag is PythonRuntime))
                throw new IOException("Catalog expander contains nested headers or nonvirtualized release rows");
            var type = Descendants(PageHost).OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == T("Package type"));
            type.SelectedItem = type.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == "Embedded");
            await WaitForSmokeConditionAsync(() => VisibleRuntimeCount == 1 && runtimeEntries.Any(entry => entry.Runtime is not null) &&
                runtimeEntries.Where(entry => entry.Runtime is not null).All(entry => entry.Runtime!.IsEmbeddable),
                "Package-type selection did not complete its queued catalog refresh");
            Root.UpdateLayout();
            if (VisibleRuntimeCount != 1 || Descendants(PageHost).OfType<Border>().Where(row => row.Tag is PythonRuntime).Any(row => !((PythonRuntime)row.Tag).IsEmbeddable))
                throw new IOException("Package-type filtering did not apply to the entire catalog");
            if (Environments.Read().Count != 0) throw new IOException("Environment layout fixture requires a fresh smoke profile");

            foreach (var design in new[] { "Fluent", "Material" })
            foreach (var theme in new[] { "Light", "Dark" })
            foreach (var language in Strings.Languages)
            foreach (var compact in new[] { false, true })
            {
                ApplySmokePreferences(preferences with { Design = design, Theme = theme, Language = language, Transparency = "Off" });
                var scale = Root.XamlRoot.RasterizationScale;
                AppWindow.Resize(new SizeInt32((int)((compact ? 930 : 1180) * scale), (int)((compact ? 620 : 850) * scale)));
                foreach (var destination in new[] { "runtimes", "catalog", "environments", "build", "activity", "settings" })
                {
                    Navigate(destination);
                    if (destination == "settings") SelectSettingsCategory("appearance");
                    Root.UpdateLayout(); await Task.Delay(80); Root.UpdateLayout();
                    var context = $"{design}/{theme}/{language}/{(compact ? "compact" : "normal")}/{destination}";
                    phase = context;
                    CheckPageGeometry(context);
                    if (!compact && language == "zh-CN" && destination is "runtimes" or "catalog" or "settings" or "build")
                        await CaptureAsync(Path.Combine(directory, $"page-{design}-{theme}-{destination}-zh-CN.png"));
                    if (destination == "catalog")
                    {
                        var preview = Descendants(PageHost).OfType<ToggleSwitch>().Single(toggle => AutomationProperties.GetName(toggle) == T("Show preview releases"));
                        RequireHorizontalBounds(preview, PageHost, context);
                        var bounds = preview.TransformToVisual(PageHost).TransformBounds(new Rect(0, 0, preview.ActualWidth, preview.ActualHeight));
                        if (bounds.Top < -1 || bounds.Bottom > PageHost.ActualHeight + 1) throw new IOException("Catalog preview switch exceeds the available height: " + context);
                        foreach (var label in Descendants(PageHost).OfType<TextBlock>().Where(label => label.Text == T("Show preview releases")))
                            RequireHorizontalBounds(label, PageHost, context);
                        foreach (var label in Descendants(preview).OfType<TextBlock>().Where(label => label.ActualWidth > 0 && label.ActualHeight > 0 && label.Visibility == Visibility.Visible))
                            if (label.IsTextTrimmed) throw new IOException("Catalog preview label is clipped: " + context);
                    }
                    if (destination == "runtimes")
                    {
                        var rows = Descendants(PageHost).OfType<Border>().Where(row => row.Tag is PythonRuntime).ToArray();
                        if (rows.Length != installed.Count || Descendants(PageHost).OfType<TextBlock>().Any(label => label.Text == T("YOUR DEFAULT INTERPRETER")))
                            throw new IOException("Installed runtime layout duplicates the default runtime: " + context);
                        var defaultRow = rows.Single(row => ((PythonRuntime)row.Tag).IsDefault);
                        if (!Descendants(defaultRow).OfType<TextBlock>().Any(label => label.Text == T("Default"))) throw new IOException("Default badge missing: " + context);
                    }
                    if (destination == "environments")
                    {
                        var empty = Descendants(PageHost).OfType<StackPanel>().Single(panel => panel.Tag as string == "EnvironmentsEmpty");
                        foreach (var name in new[] { "Create environment", "Import environment" })
                            if (Descendants(PageHost).OfType<Button>().Count(button => AutomationProperties.GetName(button) == T(name)) != 1 ||
                                !Descendants(empty).OfType<Button>().Any(button => AutomationProperties.GetName(button) == T(name)))
                                throw new IOException("Environment empty-state action duplicated or missing: " + context);
                    }
                    if (destination == "settings")
                    {
                        var rows = Descendants(PageHost).OfType<Grid>().Where(row => row.Tag as string == "SettingsRow").ToArray();
                        if (rows.Length == 0) throw new IOException("Appearance settings rows missing: " + context);
                        RequireCatalogFiltersAbsentFromSettings();
                        foreach (var row in rows)
                        {
                            var control = (FrameworkElement)row.Children[1];
                            if (Grid.GetRow(control) != (row.ActualWidth < 620 ? 1 : 0)) throw new IOException("Settings controls did not adapt to the available width: " + context);
                            RequireHorizontalBounds(control, row, context);
                        }
                        if (compact && language == "ja-JP") await CaptureAsync(Path.Combine(directory, "23-layout-" + design + "-" + theme + "-ja-JP-compact.png"));
                    }
                }
            }

            Environments.Remember(fixtureEnvironment);
            Navigate("environments"); Root.UpdateLayout(); await Task.Delay(80); Root.UpdateLayout();
            var header = Descendants(PageHost).OfType<Grid>().Single(grid => grid.Tag as string == "PageHeader");
            if (!Descendants(header).OfType<Button>().Any(button => AutomationProperties.GetName(button) == T("Create environment")) ||
                Descendants(PageHost).OfType<FrameworkElement>().Any(element => element.Tag as string == "EnvironmentsEmpty") ||
                !Descendants(PageHost).OfType<Grid>().Any(grid => grid.Tag as string == "EnvironmentRow"))
                throw new IOException("Populated environments did not switch to header creation and environment rows");
            CheckPageGeometry("populated-environments");
            activityFilter = null; Navigate("activity");
            activityLog.Add("> pymanager list --format=json", ActivityLevel.Information, ActivityOrigin.Command);
            activityLog.Add("layout-error\nDetailed diagnostic output", ActivityLevel.Error, ActivityOrigin.Application);
            RefreshActivityOutput();
            Root.UpdateLayout();
            var command = activityRows.Last(row => row.Entry.Origin == ActivityOrigin.Command);
            var diagnostic = activityRows.Last();
            if (command.MessageSize != 13 || !command.MessageFont.Source.Contains("Mono") || diagnostic.MessageSize != 14 ||
                !diagnostic.HasDetails || diagnostic.Summary != "layout-error")
                throw new IOException("Activity does not distinguish commands, application messages and expandable error details");
            activityList!.ScrollIntoView(diagnostic);
            await WaitForSmokeConditionAsync(() => Descendants(activityList).OfType<Expander>().Any(expander => expander.Header as string == diagnostic.Summary),
                "Multiline error header did not bind");
            var details = Descendants(activityList).OfType<Expander>().Single(expander => expander.Header as string == diagnostic.Summary);
            details.IsExpanded = true;
            await WaitForSmokeConditionAsync(() => Descendants(details).OfType<TextBlock>().Any(label => label.Text == diagnostic.Message && label.ActualHeight > 0),
                "Expanded error details did not render");
            var clear = Descendants(PageHost).OfType<Button>().Single(button => AutomationProperties.GetName(button) == T("Clear"));
            await WaitForSmokeConditionAsync(() => clear.IsLoaded, "Activity clear action did not load");
            InvokeButton(clear);
            await WaitForSmokeConditionAsync(() => activityRows.Count == 0 && activityLog.Entries.Count == 0 && activityEmpty?.Visibility == Visibility.Visible,
                "Clearing activity did not show its empty state");
        }
        catch (Exception error)
        {
            string Bounds(FrameworkElement element)
            {
                try { return element.TransformToVisual(PageHost).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)).ToString(); }
                catch (Exception) { return "unavailable"; }
            }
            var diagnostics = new
            {
                phase, error = error.ToString(), activeDesign = ActiveDesign, page,
                physicalWindowWidth = AppWindow.Size.Width, physicalWindowHeight = AppWindow.Size.Height,
                rootWidthDip = Root.ActualWidth, rootHeightDip = Root.ActualHeight, scale = Root.XamlRoot.RasterizationScale,
                viewportHeight = GetRuntimeScroll()?.ViewportHeight, scrollOffset = GetRuntimeScroll()?.VerticalOffset,
                entries = runtimeEntries.Select(entry => new { entry.Key, entry.Series, entry.InGroup, entry.Recommended, entry.Installed, version = entry.Runtime?.Version }).ToArray(),
                containers = runtimeRows is null ? [] : Descendants(runtimeRows).OfType<ListViewItem>().Select(container =>
                {
                    var host = container.ContentTemplateRoot as ContentControl;
                    var entry = host?.Tag as RuntimeListEntry;
                    return new { container.IsLoaded, container.ActualHeight, bounds = Bounds(container), contentType = container.Content?.GetType().Name,
                        templateRootType = container.ContentTemplateRoot?.GetType().Name, hostTagType = host?.Tag?.GetType().Name,
                        entryKey = entry?.Key, entryInGroup = entry?.InGroup, entryRecommended = entry?.Recommended,
                        rows = Descendants(container).OfType<Border>().Where(row => row.Tag is PythonRuntime || row.Tag as string == "RecommendedRuntime")
                            .Select(row => new { tag = row.Tag is PythonRuntime runtime ? runtime.Version : row.Tag, row.IsLoaded, row.ActualHeight, bounds = Bounds(row) }).ToArray() };
                }).ToArray()
            };
            await File.WriteAllTextAsync(Path.Combine(directory, "ui-layout-failure.json"), System.Text.Json.JsonSerializer.Serialize(diagnostics,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
        finally
        {
            if (Environments.Read().Any(environment => environment.Path == fixtureEnvironment.Path)) Environments.Remember(fixtureEnvironment, remove: true);
            installed = originalInstalled; catalog = originalCatalog;
            expandedSeries.Clear(); foreach (var key in originalExpanded) expandedSeries.Add(key);
            activityFilter = originalActivityFilter;
            AppWindow.Resize(originalWindowSize);
            ApplySmokePreferences(originalPreferences); Navigate(originalPage); Root.UpdateLayout();
        }
    }

    private void CheckPageGeometry(string context)
    {
        if (page == "settings") { RequireSettingsWorkspaceGeometry(context); return; }
        if (palette.Tokens.ControlHeight < 32 || palette.Tokens.CompactControlHeight >= palette.Tokens.ControlHeight)
            throw new IOException("Desktop actions must have distinct regular and compact metrics: " + context);
        var header = Descendants(PageHost).OfType<Grid>().Single(grid => grid.Tag as string == "PageHeader");
        var title = Descendants(header).OfType<TextBlock>().Single(label => label.Tag as string == "PageTitle");
        if (Math.Abs(header.TransformToVisual(PageHost).TransformPoint(new Point()).X) > 1)
            throw new IOException("Page header does not align with content: " + context);
        var actions = Descendants(header).OfType<FrameworkElement>().SingleOrDefault(element => element.Tag as string == "HeaderActions");
        if (actions is not null)
        {
            var titleCenter = title.TransformToVisual(header).TransformPoint(new Point(0, title.ActualHeight / 2)).Y;
            var actionCenter = actions.TransformToVisual(header).TransformPoint(new Point(0, actions.ActualHeight / 2)).Y;
            if (Grid.GetRow(actions) == Grid.GetRow(title) && Math.Abs(titleCenter - actionCenter) > 1)
                throw new IOException($"Header actions do not align with the title ({titleCenter:F2} vs {actionCenter:F2}, header {header.ActualWidth:F2}x{header.ActualHeight:F2}, margin {actions.Margin}): {context}");
            if (Grid.GetRow(actions) != Grid.GetRow(title) && actions.TransformToVisual(header).TransformPoint(new Point()).Y < titleCenter)
                throw new IOException("Stacked header actions overlap the title: " + context);
        }
        foreach (var button in Descendants(PageHost).OfType<Button>().Where(button => (button.Tag as string is "ActionButton" or "IconButton") && button.ActualWidth > 0))
        {
            RequireHorizontalBounds(button, PageHost, context);
            var compact = button.Resources.ContainsKey("PyDeckActionCompact") && button.Resources["PyDeckActionCompact"] is true;
            var minimumHeight = compact ? palette.Tokens.CompactControlHeight : palette.Tokens.ControlHeight;
            var iconSize = compact ? palette.Tokens.CompactIconSize : palette.Tokens.ControlIconSize;
            if (Math.Abs(button.MinHeight - minimumHeight) > 0.1) throw new IOException("Action minimum height diverged from its density role: " + context);
            var content = (FrameworkElement)button.Content;
            // Fluent's native presenter measures its border. Material draws the outline
            // over the container, keeping outlined and filled actions the same height.
            var measuredBorder = ActiveDesign == "Fluent" ? button.BorderThickness.Top + button.BorderThickness.Bottom : 0;
            var expectedHeight = Math.Max(minimumHeight, content.ActualHeight + button.Padding.Top + button.Padding.Bottom + measuredBorder);
            if (Math.Abs(button.ActualHeight - expectedHeight) > 1) throw new IOException($"Action height is {button.ActualHeight}, expected {expectedHeight}: {context}");
            if (button.Tag as string == "IconButton" && Math.Abs(button.ActualWidth - button.ActualHeight) > 1) throw new IOException("Icon button is not square: " + context);
            foreach (var label in (content as StackPanel)?.Children.OfType<TextBlock>() ?? [])
                if (Math.Abs(label.FontSize - palette.Tokens.ControlFontSize) > 0.1 || label.IsTextTrimmed)
                    throw new IOException("Action label is clipped or has inconsistent typography: " + context);
            foreach (var icon in new[] { content }.Concat(Descendants(content).OfType<FrameworkElement>()).OfType<FontIcon>())
                if (Math.Abs(icon.FontSize - iconSize) > 0.1) throw new IOException("Action icon size is inconsistent: " + context);
            var horizontalBorder = ActiveDesign == "Fluent" ? button.BorderThickness.Left + button.BorderThickness.Right : 0;
            if (content.ActualWidth + button.Padding.Left + button.Padding.Right + horizontalBorder > button.ActualWidth + 1)
                throw new IOException("Action content exceeds its button bounds: " + context);
        }
    }

    private static void RequireHorizontalBounds(FrameworkElement element, FrameworkElement container, string context)
    {
        var bounds = element.TransformToVisual(container).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        if (bounds.Left < -1 || bounds.Right > container.ActualWidth + 1)
            throw new IOException($"Horizontal overflow ({bounds.Left:F1}..{bounds.Right:F1}, available {container.ActualWidth:F1}): {context}");
    }
}
