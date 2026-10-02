using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckManagement070UiAsync(string directory)
    {
        if (smokeDirectory is null) throw new IOException("Isolated profile required");
        var originalPreferences = preferences; var originalSize = AppWindow.Size;
        var environment = new VirtualEnvironment(Path.Combine(store.DirectoryPath, "package-project", ".venv"), "Not checked", "3.14.7", store.DirectoryPath);
        try
        {
            Environments.Remember(environment); packageEnvironment = environment.Path;
            packageSnapshot = new(true, [new("pip", "26.2.1"), new("colorama", "0.4.6")]);
            packageInput = "requests>=2.32,<3";
            storageExpanded = true;
            storageEntries = [new("Built runtime", Path.Combine(store.DirectoryPath, "Builds", "Runtimes", "fixture"), 64000000, "fixture", Users: 1)];
            foreach (var design in new[] { "Fluent", "Material" })
            foreach (var language in Strings.Languages)
            foreach (var compact in new[] { false, true })
            {
                ApplySmokePreferences(preferences with { Design = design, Language = language });
                var scale = Root.XamlRoot.RasterizationScale;
                AppWindow.Resize(new((int)((compact ? 930 : 1180) * scale), (int)(850 * scale)));
                Navigate("environments"); Root.UpdateLayout(); await Task.Delay(100); Root.UpdateLayout();
                CheckPageGeometry($"packages/{design}/{language}/{compact}");
                if (!Descendants(PageHost).OfType<StackPanel>().Any(p => p.Tag as string == "EnvironmentPackages")) throw new IOException("Package panel missing");
                var removePip = Descendants(PageHost).OfType<Button>().Where(b => AutomationProperties.GetName(b) == T("Uninstall")).ToArray();
                if (removePip.Count(b => !b.IsEnabled) != 1) throw new IOException("pip uninstall must be disabled");
                if (!compact && design == "Fluent" && language == "zh-CN") await CaptureAsync(Path.Combine(directory, "27-environment-packages.png"));
                Navigate("settings"); Root.UpdateLayout(); await Task.Delay(100); Root.UpdateLayout();
                var clean = Descendants(PageHost).OfType<Button>().Single(b => AutomationProperties.GetName(b) == T("Clean files"));
                if (clean.IsEnabled) throw new IOException("Used runtime cleanup enabled");
                if (!compact && design == "Fluent" && language == "zh-CN")
                {
                    settingsScroll!.ChangeView(null, settingsScroll.ScrollableHeight * .65, null, true);
                    await Task.Delay(100); await CaptureAsync(Path.Combine(directory, "28-build-storage.png"));
                }
                Navigate("build"); Root.UpdateLayout(); await Task.Delay(100);
                if (!Descendants(PageHost).OfType<Button>().Any(b => AutomationProperties.GetName(b) == T("Prepare build tools"))) throw new IOException("Build preparation missing");
            }
        }
        finally
        {
            packageEnvironment = ""; packageInput = ""; packageSnapshot = null; storageEntries = null; storageExpanded = false;
            Environments.Remember(environment, remove: true); AppWindow.Resize(originalSize); ApplySmokePreferences(originalPreferences); Navigate("runtimes");
        }
    }
}
