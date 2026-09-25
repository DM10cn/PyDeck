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
            SavePreferences(preferences with { Language = "en-US", CatalogSource = "Online", DefaultArchitecture = "x64", CatalogPackageType = "Standard", ShowPreviewReleases = false });
            Navigate("catalog"); Root.UpdateLayout();
            var series = Descendants(PageHost).OfType<Expander>().Single(e => e.Tag as string == "CatalogSeries:3.14");
            series.IsExpanded = true; Root.UpdateLayout();
            var historicalRows = ((StackPanel)series.Content).Children.OfType<Border>().Where(row => row.Tag is PythonRuntime).ToArray();
            if (!historicalRows.Select(row => ((PythonRuntime)row.Tag).Version).SequenceEqual(new[] { "3.14.7", "3.14.6", "3.14.5" }))
                throw new IOException("Historical micro releases sharing a PIM ID were hidden or ordered incorrectly");
            var currentAction = Descendants(historicalRows[0]).OfType<Button>().Single(button => AutomationProperties.GetName(button) == T("Installed"));
            var previousAction = Descendants(historicalRows[1]).OfType<Button>().Single(button => AutomationProperties.GetName(button) == T("Install"));
            if (currentAction.IsEnabled || previousAction.IsEnabled != (connected && client.SupportsMutations && !busy))
                throw new IOException("Installed status was compared by minor ID instead of exact micro release");
            if (Descendants(series).OfType<Expander>().Any()) throw new IOException("Catalog has nested category expanders");
            var type = Descendants(PageHost).OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == T("Package type"));
            type.SelectedItem = type.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == "Embedded");
            Root.UpdateLayout();
            if (VisibleRuntimeCount != 1 || Descendants(PageHost).OfType<Border>().Where(row => row.Tag is PythonRuntime).Any(row => !((PythonRuntime)row.Tag).IsEmbeddable))
                throw new IOException("Package-type filtering did not apply to the entire catalog");
            if (Environments.Read().Count != 0) throw new IOException("Environment layout fixture requires a fresh smoke profile");

            foreach (var design in new[] { "Fluent", "Material" })
            foreach (var language in Strings.Languages)
            foreach (var compact in new[] { false, true })
            {
                SavePreferences(preferences with { Design = design, Language = language, Transparency = "Off" });
                var scale = Root.XamlRoot.RasterizationScale;
                AppWindow.Resize(new SizeInt32((int)((compact ? 930 : 1180) * scale), (int)((compact ? 620 : 850) * scale)));
                foreach (var destination in new[] { "runtimes", "catalog", "environments", "activity", "settings" })
                {
                    Navigate(destination); Root.UpdateLayout(); await Task.Delay(80); Root.UpdateLayout();
                    var context = $"{design}/{language}/{(compact ? "compact" : "normal")}/{destination}";
                    CheckPageGeometry(context);
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
                        if (rows.Length < 6) throw new IOException("Settings rows missing: " + context);
                        RequireCatalogFiltersAbsentFromSettings();
                        foreach (var row in rows)
                        {
                            var control = (FrameworkElement)row.Children[1];
                            if (Grid.GetRow(control) != (row.ActualWidth < 620 ? 1 : 0)) throw new IOException("Settings controls did not adapt to the available width: " + context);
                            RequireHorizontalBounds(control, row, context);
                        }
                        if (compact && language == "ja-JP") await CaptureAsync(Path.Combine(directory, "23-layout-" + design + "-ja-JP-compact.png"));
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
        finally
        {
            if (Environments.Read().Any(environment => environment.Path == fixtureEnvironment.Path)) Environments.Remember(fixtureEnvironment, remove: true);
            installed = originalInstalled; catalog = originalCatalog;
            expandedSeries.Clear(); foreach (var key in originalExpanded) expandedSeries.Add(key);
            activityFilter = originalActivityFilter;
            AppWindow.Resize(originalWindowSize);
            SavePreferences(originalPreferences); Navigate(originalPage); Root.UpdateLayout();
        }
    }

    private void CheckPageGeometry(string context)
    {
        if (palette.Tokens.ControlHeight != 32 || palette.Tokens.ControlFontSize != 14 || palette.Tokens.ControlIconSize != 16)
            throw new IOException("Action metrics must remain 32 DIP / 14 text / 16 icon: " + context);
        var header = Descendants(PageHost).OfType<Grid>().Single(grid => grid.Tag as string == "PageHeader");
        var title = Descendants(header).OfType<TextBlock>().Single(label => label.Tag as string == "PageTitle");
        if (Math.Abs(header.TransformToVisual(PageHost).TransformPoint(new Point()).X) > 1)
            throw new IOException("Page header does not align with content: " + context);
        var actions = Descendants(header).OfType<FrameworkElement>().SingleOrDefault(element => element.Tag as string == "HeaderActions");
        if (actions is not null)
        {
            var titleCenter = title.TransformToVisual(header).TransformPoint(new Point(0, title.ActualHeight / 2)).Y;
            var actionCenter = actions.TransformToVisual(header).TransformPoint(new Point(0, actions.ActualHeight / 2)).Y;
            if (Math.Abs(titleCenter - actionCenter) > 1) throw new IOException("Header actions do not align with the title: " + context);
        }
        foreach (var button in Descendants(PageHost).OfType<Button>().Where(button => (button.Tag as string is "ActionButton" or "IconButton") && button.ActualWidth > 0))
        {
            RequireHorizontalBounds(button, PageHost, context);
            if (Math.Abs(button.MinHeight - palette.Tokens.ControlHeight) > 0.1) throw new IOException("Action minimum height diverged from semantic metrics: " + context);
            var content = (FrameworkElement)button.Content;
            var expectedHeight = Math.Max(palette.Tokens.ControlHeight, content.ActualHeight + button.Padding.Top + button.Padding.Bottom + button.BorderThickness.Top + button.BorderThickness.Bottom);
            if (Math.Abs(button.ActualHeight - expectedHeight) > 1) throw new IOException($"Action height is {button.ActualHeight}, expected {expectedHeight}: {context}");
            if (button.Tag as string == "IconButton" && Math.Abs(button.ActualWidth - palette.Tokens.ControlHeight) > 1) throw new IOException("Icon button is not square: " + context);
            foreach (var label in (content as StackPanel)?.Children.OfType<TextBlock>() ?? [])
                if (Math.Abs(label.FontSize - palette.Tokens.ControlFontSize) > 0.1 || label.IsTextTrimmed)
                    throw new IOException("Action label is clipped or has inconsistent typography: " + context);
            foreach (var icon in new[] { content }.Concat(Descendants(content).OfType<FrameworkElement>()).OfType<FontIcon>())
                if (Math.Abs(icon.FontSize - palette.Tokens.ControlIconSize) > 0.1) throw new IOException("Action icon size is inconsistent: " + context);
            if (content.ActualWidth + button.Padding.Left + button.Padding.Right + button.BorderThickness.Left + button.BorderThickness.Right > button.ActualWidth + 1)
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
