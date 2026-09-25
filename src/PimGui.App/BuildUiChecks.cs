using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckBuildUiAsync(string directory)
    {
        if (smokeDirectory is null) throw new IOException("Build UI fixtures require an isolated smoke profile");
        var originalConnected = connected; var originalInstalled = installed; var originalLocal = localRuntimes;
        var originalPreferences = preferences; var originalOptions = buildOptions;
        var originalVersions = buildVersions; var originalPreset = buildPreset;
        var record = new BuildRecord(Guid.NewGuid().ToString("N"), new(), BuildState.Ready, DateTimeOffset.UtcNow);
        try
        {
            Builds.Save(record); ReloadLocalRuntimes();
            connected = false; installed = MergeLocal([]);
            Navigate("runtimes"); Root.UpdateLayout();
            if (VisibleRuntimeCount != 1 || Descendants(PageHost).OfType<StackPanel>().Any(p => p.Tag as string == "ManagerSetup"))
                throw new IOException("Local runtime disappeared when PIM was disconnected");
            var card = Descendants(PageHost).OfType<Border>().Single(b => b.Tag is PythonRuntime);
            var menu = Descendants(card).OfType<Button>().Select(b => b.Flyout).OfType<MenuFlyout>().Single();
            foreach (var title in new[] { "Set as default", "Check for updates", "Reinstall to repair" })
                if (menu.Items.OfType<MenuFlyoutItem>().Single(i => i.Text == T(title)).IsEnabled) throw new IOException("PIM action enabled for a local build");
            if (!menu.Items.OfType<MenuFlyoutItem>().Single(i => i.Text == T("Remove from list")).IsEnabled ||
                !Descendants(card).OfType<Button>().Single(b => AutomationProperties.GetName(b) == T("Terminal")).IsEnabled)
                throw new IOException("Local runtime actions unavailable without PIM");
            await CaptureAsync(Path.Combine(directory, "23-local-build-without-pim.png"));
            foreach (var design in new[] { "Fluent", "Material" })
            foreach (var language in Strings.Languages)
            {
                SavePreferences(preferences with { Design = design, Language = language });
                Navigate("build"); Root.UpdateLayout(); await Task.Delay(80);
                if (buildVersionPicker!.Text != buildOptions.Version)
                    throw new IOException("Source version is blank or differs from the selected build version");
                buildVersions = [BuildRecipe.Version, "3.13.7", "3.12.4"]; RenderPage(); Root.UpdateLayout(); await Task.Delay(80);
                buildVersionPicker!.SelectedItem = "3.12.4";
                if (buildOptions.Version != "3.12.4") throw new IOException("Exact source version selection did not update the build");
                var preset = Descendants(PageHost).OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == T("Build profile"));
                preset.SelectedItem = preset.Items.Cast<ComboBoxItem>().Single(item => item.Tag as string == "Performance");
                Root.UpdateLayout(); await Task.Delay(80);
                if (!buildOptions.Pgo || buildOptions.Version != "3.12.4") throw new IOException("Preset did not preserve the selected source version");
                var scroll = buildScroll!;
                CheckScrollbarClearance(scroll);
                scroll.ChangeView(null, Math.Min(320, scroll.ScrollableHeight), null, true); await Task.Delay(100);
                var previousOffset = scroll.VerticalOffset;
                if (Descendants(PageHost).OfType<ComboBox>().Any(box => AutomationProperties.GetName(box) == T("Architecture")))
                    throw new IOException("Build UI offers an unsupported architecture");
                var symbols = Descendants(PageHost).OfType<ToggleSwitch>().Single(toggle => AutomationProperties.GetName(toggle) == T("Debug symbols"));
                symbols.IsOn = !symbols.IsOn; Root.UpdateLayout();
                await Task.Delay(100);
                if (Math.Abs(buildScroll!.VerticalOffset - previousOffset) > 2)
                    throw new IOException("Build option changes reset the scroll position");
                if (buildOptions.IncludeSymbols == originalOptions.IncludeSymbols) throw new IOException("Build option toggle did not persist");
                Navigate("activity"); Navigate("build"); Root.UpdateLayout();
                if (!Descendants(PageHost).OfType<ToggleSwitch>().Single(toggle => AutomationProperties.GetName(toggle) == T("Debug symbols")).IsOn)
                    throw new IOException("Build option lost during navigation");
                buildOptions = originalOptions; buildPreset = originalPreset;
            }
            SavePreferences(preferences with { Design = "Fluent", Language = "zh-CN" });
            Navigate("build"); Root.UpdateLayout(); await Task.Delay(100);
            buildScroll!.ChangeView(null, 0, null, true); await Task.Delay(100);
            await CaptureAsync(Path.Combine(directory, "24-build-python.png"));
            buildScroll!.ChangeView(null, 280, null, true); await Task.Delay(150);
            await CaptureAsync(Path.Combine(directory, "25-build-scroll.png"));
            Navigate("settings"); Root.UpdateLayout(); await Task.Delay(100);
            CheckScrollbarClearance(settingsScroll!);
            settingsScroll!.ChangeView(null, 210, null, true); await Task.Delay(150);
            await CaptureAsync(Path.Combine(directory, "26-settings-scroll.png"));
            Navigate("build");
            using var operation = BeginOperation(T("Building CPython {0}", BuildRecipe.Version), download: true);
            SetBusy(true);
            operation.Report(OperationPhase.Compiling); UpdateOperationPanel();
            if (!OperationProgressBar.IsIndeterminate || OperationPhaseText.Text != T("Compiling CPython")) throw new IOException("Compile progress invents a percentage");
            operation.Report(OperationPhase.Training); UpdateOperationPanel();
            if (!OperationProgressBar.IsIndeterminate || OperationPhaseText.Text != T("Training PGO")) throw new IOException("PGO training stage is not distinct");
            Navigate("runtimes"); await RequestOperationCancellationAsync();
            if (!operation.IsCancellationRequested) throw new IOException("Build cancellation failed after navigation");
            FinishOperation(operation); SetBusy(false);
        }
        finally
        {
            if (activeOperation is not null) FinishOperation(activeOperation);
            busy = false; Builds.Remove(record.Id);
            localRuntimes = originalLocal; installed = originalInstalled; connected = originalConnected; buildOptions = originalOptions;
            buildVersions = originalVersions; buildPreset = originalPreset;
            currentBuild = null; SavePreferences(originalPreferences); Navigate("runtimes");
        }
    }
    private static void CheckScrollbarClearance(ScrollViewer scroll)
    {
        foreach (var control in Descendants(scroll).OfType<Control>().Where(c => c is ComboBox or ToggleSwitch or RadioButton && c.ActualWidth > 0))
        {
            var bounds = control.TransformToVisual(scroll).TransformBounds(new Windows.Foundation.Rect(0, 0, control.ActualWidth, control.ActualHeight));
            if (bounds.Top < scroll.ViewportHeight && bounds.Bottom > 0 && bounds.Right > scroll.ActualWidth - 20)
                throw new IOException("A visible setting control overlaps the scrollbar interaction area");
        }
    }
}
