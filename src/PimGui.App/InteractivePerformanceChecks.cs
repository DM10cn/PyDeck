using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;
using System.Diagnostics;
using System.Text.Json;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task RunPerformanceProbeAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        try
        {
            // No discovery, network requests, installer calls, or access to the real profile.
            connected = true;
            await CheckInteractivePerformanceAsync(checks);
            await CheckBatchedUpdatesAsync();
            checks.Add("Background log/progress flood, hidden activity, clear and completion");
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = false, checks, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { CancelSearchRefresh(); Close(); }
    }

    private async Task CheckInteractivePerformanceAsync(List<string> checks)
    {
        if (smokeDirectory is null) throw new IOException("Isolated test profile required");
        var originalPreferences = preferences; var originalCatalog = catalog; var originalOptions = buildOptions;
        var originalPreset = buildPreset; var originalInstalled = installed;
        var originalEnvironmentSearch = environmentSearch; var originalPackageEnvironment = packageEnvironment;
        var originalPackageSnapshot = packageSnapshot;
        try
        {
            var callbacks = 0;
            for (var i = 0; i < 1000; i++) ScheduleSearchRefresh(() => callbacks++);
            await WaitForSmokeConditionAsync(() => callbacks == 1, "Search flood did not coalesce");
            ScheduleSearchRefresh(() => callbacks++); Navigate("settings");
            await Task.Delay(220);
            if (callbacks != 1 || pendingSearchRefresh is not null) throw new IOException("Search survived page disposal");
            checks.Add("1000 search requests become one update; navigation cancels pending work");

            catalog = Enumerable.Range(0, 30).Select(i => new PythonRuntime("fixture-" + i, "PythonCore", "3.14-64",
                "3.14." + i, "Python 3.14." + i, "", "", false)).ToArray();
            installed = [];
            ApplySmokePreferences(preferences with { CatalogSource = "Online", DefaultArchitecture = "x64", CatalogPackageType = "Standard" });
            Navigate("catalog"); Root.UpdateLayout();
            var content = PageHost.Children.Single();
            var projection = runtimeSnapshot;
            var searchBox = Descendants(PageHost).OfType<AutoSuggestBox>().Single();
            foreach (var query in new[] { "3", "3.", "3.1", "3.14", "3.14.29" }) searchBox.Text = query;
            await WaitForSmokeConditionAsync(() => VisibleRuntimeCount == 1, "Debounced catalog search did not show the final query");
            if (!ReferenceEquals(content, PageHost.Children.Single()) || !ReferenceEquals(projection, runtimeSnapshot))
                throw new IOException("Searching rebuilt the page or catalog projection");
            catalog = [catalog[29] with { Version = "3.14.99", DisplayName = "Python 3.14.99" }];
            PopulateRuntimes();
            if (VisibleRuntimeCount != 0 || ReferenceEquals(projection, runtimeSnapshot)) throw new IOException("Catalog replacement reused stale results");
            checks.Add("Catalog search preserves controls/index; replaced catalog invalidates the index");

            var environment = new VirtualEnvironment(Path.Combine(store.DirectoryPath, "performance-fixture"));
            Environments.Remember(environment); packageEnvironment = environment.Path;
            packageSnapshot = new(true, [new("pip", "26.2"), new("numpy", "2.3"), new("requests", "2.32")]);
            Navigate("environments"); Root.UpdateLayout();
            var packageSearch = Descendants(PageHost).OfType<TextBox>().Single(box => box.PlaceholderText == T("Search packages"));
            packageSearch.Text = "numpy";
            await WaitForSmokeConditionAsync(() => Descendants(PageHost).OfType<Button>().Count(button => AutomationProperties.GetName(button) == T("Update")) == 1,
                "Package search did not refresh");
            var environmentSearchBox = Descendants(PageHost).OfType<AutoSuggestBox>().Single();
            environmentSearchBox.Text = "does-not-exist";
            await WaitForSmokeConditionAsync(() => !Descendants(PageHost).OfType<Grid>().Any(grid => grid.Tag as string == "EnvironmentRow"), "Environment search did not refresh");
            Environments.Remember(environment, remove: true); packageEnvironment = ""; packageSnapshot = null;
            checks.Add("Package and environment search render the final query");

            foreach (var design in new[] { "Fluent", "Material" })
            foreach (var language in Strings.Languages)
            {
                ApplySmokePreferences(preferences with { Design = design, Language = language });
                buildOptions = new(); buildPreset = "Standard"; Navigate("build"); Root.UpdateLayout();
                var originalPage = PageHost.Children.Single(); var scroll = buildScroll;
                ComboBox Choice(string title) => Descendants(PageHost).OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == T(title));
                ToggleSwitch Component(string title) => Descendants(PageHost).OfType<ToggleSwitch>().Single(toggle => AutomationProperties.GetName(toggle) == T(title));
                static void Select(ComboBox box, string value) => box.SelectedItem = box.Items.Cast<ComboBoxItem>().Single(item => (string)item.Tag == value);
                var preset = Choice("Build profile"); var type = Choice("Build type");
                Select(preset, "Performance");
                if (!Component("Profile-guided optimization").IsOn || buildPreset != "Performance") throw new IOException("Preset synchronization recursed");
                Select(type, "Debug");
                if (Component("Profile-guided optimization").IsOn || buildOptions.Pgo) throw new IOException("Debug retained PGO");
                Component("Profile-guided optimization").IsOn = true;
                if (buildOptions.Configuration != "Release" || ((ComboBoxItem)type.SelectedItem).Tag as string != "Release") throw new IOException("PGO did not select Release");
                Component("SSL").IsOn = false;
                if (Component("pip").IsOn || buildOptions.IncludePip) throw new IOException("SSL off retained pip");
                Component("pip").IsOn = true;
                if (!Component("SSL").IsOn || !buildOptions.IncludeSsl) throw new IOException("pip did not enable SSL");
                var clock = Stopwatch.StartNew(); var allocated = GC.GetAllocatedBytesForCurrentThread();
                var symbols = Component("Debug symbols");
                for (var i = 0; i < 80; i++) symbols.IsOn = !symbols.IsOn;
                clock.Stop(); allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
                if (!ReferenceEquals(originalPage, PageHost.Children.Single()) || !ReferenceEquals(scroll, buildScroll) || !symbols.IsLoaded)
                    throw new IOException("Component changes rebuilt the page");
                buildOptions.Validate();
                checks.Add($"{design}/{language}: preset/component links remain consistent; 80 toggles preserve page and scroll, {clock.Elapsed.TotalMilliseconds:F1} ms / {allocated} managed bytes");
            }
        }
        finally
        {
            CancelSearchRefresh(); catalog = originalCatalog; installed = originalInstalled;
            buildOptions = originalOptions; buildPreset = originalPreset;
            environmentSearch = originalEnvironmentSearch; packageEnvironment = originalPackageEnvironment; packageSnapshot = originalPackageSnapshot;
            ApplySmokePreferences(originalPreferences); Navigate("runtimes");
        }
    }
}
