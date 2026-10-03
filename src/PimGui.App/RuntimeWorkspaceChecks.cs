using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;
using System.Text.Json;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task RunWorkspaceProbeAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        try
        {
            connected = true;
            if (Environment.GetCommandLineArgs().Contains("--window-chrome-only"))
            {
                await CheckWindowChromeAsync(directory, checks);
                await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--settings-only"))
            {
                await CheckSettingsWorkspacesAsync(directory, checks);
                await CheckCatalogFilterPreferencesAsync(directory);
                checks.Add("Catalog preview toggle saves and deferred result refreshes settle in four languages; control identity, keyboard focus, exact filter results and persisted choices survive navigation.");
                await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--ripple-only"))
            {
                await CheckRippleInteractionAsync(directory, checks);
                await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--expansion-only"))
            {
                await CheckExpansionPerformanceAsync(directory, checks);
                await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }
            if (Environment.GetCommandLineArgs().Contains("--recording-only"))
            {
                await RunWorkspaceRecordingAsync(directory, checks);
                await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
                return;
            }
            await CheckRuntimeWorkspacesAsync(directory, checks);
            await CheckLiveMaterialInteractionsAsync(checks);
            await CheckCatalogFilterPreferencesAsync(directory);
            checks.Add("Catalog filters preserve controls, keyboard focus, and saved choices across navigation in all four languages.");
            await CheckSearchControlsAsync(directory);
            checks.Add("Native search editing, clearing, text propagation and rendered Material focus/text contrast passed in both themes and designs.");
            await CheckVirtualizedRuntimeListsAsync(checks);
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = false, checks, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { CancelSearchRefresh(); Close(); }
    }

    private async Task CheckRuntimeWorkspacesAsync(string directory, List<string> checks)
    {
        if (smokeDirectory is null) throw new IOException("Workspace fixtures require an isolated smoke profile");
        var originalPreferences = preferences; var originalCatalog = catalog; var originalInstalled = installed;
        var originalSize = AppWindow.Size; var originalPage = page; var originalExpanded = expandedSeries.ToArray();
        var originalActivityFilter = activityFilter;
        var measurements = new List<object>(); var contrast = new List<object>();
        var passed = false;
        try
        {
            installed = Enumerable.Range(0, 12).Select(index => new PythonRuntime("layout-installed-" + index,
                "PythonCore", $"3.{14 - index}-64", $"3.{14 - index}.7", $"Python 3.{14 - index}.7",
                Path.Combine(store.DirectoryPath, "workspace-fixture", $"python-3.{14 - index}", "python.exe"), store.DirectoryPath,
                IsDefault: index == 0, IsManaged: true)).ToArray();
            catalog = Enumerable.Range(0, 3).SelectMany(series => Enumerable.Range(0, 8).Select(micro =>
                new PythonRuntime("layout-installed-" + series, "PythonCore", $"3.{14 - series}-64", $"3.{14 - series}.{micro}",
                    $"Python 3.{14 - series}.{micro}", "", "", IsDefault: false, IsManaged: true))).ToArray();
            if (installed.Count(runtime => runtime.IsDefault) != 1 || installed.Any(runtime => !runtime.IsManaged) || catalog.Any(runtime => runtime.IsDefault))
                throw new IOException("Workspace fixture must contain exactly one default installed runtime and managed catalog candidates");
            activityLog.Clear(); activityFilter = null;
            for (var index = 0; index < 60; index++)
                activityLog.Add($"Workspace fixture {index:000}: Python 3.14.7 · x64 · operation completed", ActivityLevel.Information, ActivityOrigin.Application);
            expandedSeries.Clear(); foreach (var series in new[] { "3.14", "3.13", "3.12" }) expandedSeries.Add(series);

            foreach (var design in new[] { "Fluent", "Material" })
            foreach (var theme in new[] { "Light", "Dark" })
            foreach (var language in Strings.Languages)
            foreach (var width in new[] { 930, 1180, 1680, 1920 })
            {
                ApplySmokePreferences(preferences with { Design = design, Theme = theme, Language = language, Transparency = "Off",
                    MaterialColorSource = "Custom", MaterialColorStyle = "TonalSpot", MaterialSeed = 0xFF6750A4u,
                    CatalogSource = "Online", DefaultArchitecture = "x64", CatalogPackageType = "Standard", ShowPreviewReleases = false });
                var scale = Root.XamlRoot.RasterizationScale;
                AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(850 * scale)));
                foreach (var destination in new[] { "runtimes", "catalog", "activity" })
                {
                    Navigate(destination); Root.UpdateLayout();
                    // Native expanders/containers can still be transitioning after their
                    // first layout. Measure the same settled state that the screenshot shows.
                    await Task.Delay(600); Root.UpdateLayout();
                    var context = $"{design}/{theme}/{language}/{width}/{destination}";
                    RequireWorkspaceGeometry(context); CheckPageGeometry(context);
                    var list = destination == "activity" ? activityList! : runtimeRows!;
                    var scroll = Descendants(list).OfType<ScrollViewer>().First();
                    if (scroll.ViewportHeight <= 0 || scroll.ViewportHeight > PageHost.ActualHeight + 1)
                        throw new IOException("List does not have a finite visible viewport: " + context);
                    if (destination == "catalog") CheckInstalledActionColumns(context);
                    if (design == "Material" && language == "en-US" && width == 1180)
                        CheckRenderedWorkspaceContrast(context, contrast);
                    var screenshot = language is "en-US" or "zh-CN" ? $"workspace-{design}-{theme}-{language}-{width}-{destination}.png" : null;
                    if (screenshot is not null) await CaptureAsync(Path.Combine(directory, screenshot));
                    Root.UpdateLayout();
                    var rows = destination == "activity"
                        ? Descendants(list).OfType<ListViewItem>().Cast<FrameworkElement>().ToArray()
                        : Descendants(list).OfType<Border>().Where(row => row.Tag is PythonRuntime).Cast<FrameworkElement>().ToArray();
                    var complete = rows.Count(row => FullyWithin(row, scroll));
                    if (complete == 0) throw new IOException("Workspace has no complete visible record: " + context);
                    measurements.Add(new { context, requestedWindowWidthDip = width, requestedWindowHeightDip = 850,
                        actualRootWidthDip = Root.ActualWidth, actualRootHeightDip = Root.ActualHeight,
                        rasterizationScale = scale, textScaleFactor = systemUi.TextScaleFactor,
                        workspaceWidthDip = PageHost.ActualWidth, viewportHeightDip = scroll.ViewportHeight,
                        completeVisibleRecords = complete, realizedRecords = rows.Length, settleDelayMilliseconds = 600, screenshot });
                }
            }
            passed = true;
            checks.Add("Runtime/catalog/activity workspaces fill the content surface at 930/1180/1680/1920 DIP in both designs, light/dark and four languages; installed/install actions have equal measured bounds and aligned columns.");
            checks.Add("workspace-layout.json records actual viewport sizes, complete record counts, scale, screenshots and rendered Material text contrast. These are current measurements, not a before/after density claim.");
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "workspace-layout.json"), JsonSerializer.Serialize(new
            {
                passed, fixture = "Synthetic: 12 installed runtimes, 24 catalog candidates, 60 single-line activity records",
                baselineComparison = "Not performed by this check; compare a baseline captured with the same fixture, effective size, language and text scaling.",
                contrastScope = "Rendered Material text on solid surfaces, with transparency disabled; excludes disabled controls, system high contrast, gradients, and translucent compositor materials.",
                measurements, contrast
            }, new JsonSerializerOptions { WriteIndented = true }));
            CancelSearchRefresh(); installed = originalInstalled; catalog = originalCatalog;
            expandedSeries.Clear(); foreach (var series in originalExpanded) expandedSeries.Add(series);
            activityLog.Clear(); activityFilter = originalActivityFilter; AppWindow.Resize(originalSize);
            ApplySmokePreferences(originalPreferences); Navigate(originalPage); Root.UpdateLayout();
        }
    }

    private void RequireWorkspaceGeometry(string context)
    {
        var available = PageSurface.ActualWidth - PageSurface.Padding.Left - PageSurface.Padding.Right -
            PageSurface.BorderThickness.Left - PageSurface.BorderThickness.Right;
        if (Math.Abs(ContentColumn.ActualWidth - available) > 2 || Math.Abs(PageHost.ActualWidth - ContentColumn.ActualWidth) > 1)
            throw new IOException($"Workspace remains centered in a width cap ({ContentColumn.ActualWidth:F1}/{available:F1} DIP): {context}");
        var list = page == "activity" ? activityList : runtimeRows;
        if (list is null) return;
        var bounds = list.TransformToVisual(PageHost).TransformBounds(new Rect(0, 0, list.ActualWidth, list.ActualHeight));
        if (Math.Abs(bounds.Left) > 1 || Math.Abs(PageHost.ActualWidth - bounds.Right) > 2)
            throw new IOException("List viewport does not reach the workspace edges: " + context);
        var scroll = Descendants(list).OfType<ScrollViewer>().FirstOrDefault();
        if (scroll is null) throw new IOException("List scrollbar has not loaded: " + context);
        var vertical = Descendants(scroll).OfType<ScrollBar>().FirstOrDefault(bar => bar.Orientation == Orientation.Vertical && bar.ActualWidth > 0);
        if (vertical is not null)
        {
            var right = vertical.TransformToVisual(PageHost).TransformBounds(new Rect(0, 0, vertical.ActualWidth, vertical.ActualHeight)).Right;
            // A small native rail margin belongs to the scrollbar itself. It must
            // not be mistaken for the old centered, width-constrained viewport.
            var allowedInset = Math.Min(8, Math.Max(3, vertical.Margin.Right + 1));
            if (vertical.Margin.Right > 8 || PageHost.ActualWidth - right > allowedInset || right > PageHost.ActualWidth + 1)
                throw new IOException($"Scrollbar is inset from the workspace right edge by {PageHost.ActualWidth - right:F1} DIP: {context}");
        }
    }

    private void CheckInstalledActionColumns(string context)
    {
        var rows = Descendants(runtimeRows!).OfType<Border>().Where(row => row.Tag is PythonRuntime && row.ActualHeight > 0).ToArray();
        var candidates = rows.Select(row => (row, action: Descendants(row).OfType<Button>().FirstOrDefault(button =>
            AutomationProperties.GetName(button) == T("Install") || AutomationProperties.GetName(button) == T("Installed"))))
            .Where(pair => pair.action is not null).ToArray();
        var installedAction = candidates.FirstOrDefault(pair => AutomationProperties.GetName(pair.action!) == T("Installed")).action;
        var installAction = candidates.FirstOrDefault(pair => AutomationProperties.GetName(pair.action!) == T("Install")).action;
        if (installedAction is null || installAction is null) throw new IOException("Equal-width fixture did not realize both installation states: " + context);
        if (installedAction.IsEnabled || Math.Abs(installedAction.ActualWidth - installAction.ActualWidth) > .5 ||
            Math.Abs(installedAction.ActualHeight - installAction.ActualHeight) > .5)
            throw new IOException($"Installation states differ in measured size: {installedAction.ActualWidth:F1}x{installedAction.ActualHeight:F1} / {installAction.ActualWidth:F1}x{installAction.ActualHeight:F1}: {context}");
        foreach (var pair in candidates)
        {
            var action = pair.action!;
            var edge = action.TransformToVisual(PageHost).TransformPoint(new Point(action.ActualWidth, 0)).X;
            var expected = installAction.TransformToVisual(PageHost).TransformPoint(new Point(installAction.ActualWidth, 0)).X;
            if (Math.Abs(edge - expected) > 1) throw new IOException("Installation actions do not share a right-aligned column: " + context);
            if (Descendants(action).OfType<TextBlock>().Any(label => label.IsTextTrimmed))
                throw new IOException("Localized installation status is trimmed: " + context);
        }
    }

    private static bool FullyWithin(FrameworkElement element, FrameworkElement viewport)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0 || element.Visibility != Visibility.Visible) return false;
        var bounds = element.TransformToVisual(viewport).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        return bounds.Top >= -.5 && bounds.Bottom <= viewport.ActualHeight + .5 && bounds.Left >= -.5 && bounds.Right <= viewport.ActualWidth + .5;
    }

    private void CheckRenderedWorkspaceContrast(string context, List<object> samples)
    {
        var checkedCount = 0;
        foreach (var label in Descendants(PageHost).OfType<TextBlock>().Where(label => label.ActualWidth > 0 && label.ActualHeight > 0 &&
            !string.IsNullOrWhiteSpace(label.Text) && label.Visibility == Visibility.Visible && FullyWithin(label, PageHost)))
        {
            var ancestors = new List<DependencyObject>();
            for (DependencyObject? current = label; current is not null; current = VisualTreeHelper.GetParent(current)) ancestors.Add(current);
            if (ancestors.OfType<Control>().Any(control => !control.IsEnabled) ||
                ancestors.OfType<UIElement>().Any(element => element.Visibility != Visibility.Visible || Math.Abs(element.Opacity - 1) > .001) ||
                label.Foreground is not SolidColorBrush foreground) continue;
            var background = palette.Surface;
            var supported = true;
            foreach (var ancestor in ancestors.AsEnumerable().Reverse())
            {
                var brush = ancestor switch { Border border => border.Background, Panel panel => panel.Background, Control control => control.Background, _ => null };
                if (brush is SolidColorBrush solid) background = CompositeColor(solid.Color, solid.Opacity, background);
                else if (brush is not null) { supported = false; break; }
            }
            if (!supported) continue;
            var actual = CompositeColor(foreground.Color, foreground.Opacity, background);
            var ratio = ColorContrast(actual, background);
            samples.Add(new { context, text = label.Text, foreground = actual.ToString(), background = background.ToString(), contrast = ratio, minimum = 4.5 });
            if (ratio + .001 < 4.5) throw new IOException($"Rendered text contrast {ratio:F2}:1 is below 4.5:1 for '{label.Text}': {context}");
            checkedCount++;
        }
        if (checkedCount < 3) throw new IOException("Too few rendered text samples for contrast verification: " + context);
    }

    private static Color CompositeColor(Color foreground, double opacity, Color background)
    {
        var alpha = foreground.A / 255d * opacity;
        byte Channel(byte front, byte back) => (byte)Math.Round(front * alpha + back * (1 - alpha));
        return Color.FromArgb(255, Channel(foreground.R, background.R), Channel(foreground.G, background.G), Channel(foreground.B, background.B));
    }

    private static double ColorContrast(Color first, Color second)
    {
        static double Channel(byte value) { var s = value / 255d; return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4); }
        static double Luminance(Color color) => .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
}
