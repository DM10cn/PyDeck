using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckWorkIsolationAsync(string directory)
    {
        var saved = preferences;
        static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message) { if (!condition) throw new IOException(message); }
        try
        {
            foreach (var language in Strings.Languages)
            foreach (var design in new[] { "Material", "Fluent" })
            {
                SavePreferences(preferences with { Language = language, Design = design });
                expandedSettings.Add("Network"); Navigate("settings"); Root.UpdateLayout();
                var originalPage = PageHost.Children.Single();
                var draft = Descendants(settingsSections["Network"].Section).OfType<TextBox>().First();
                draft.Text = "http://draft.invalid:8123";
                var theme = Descendants(PageHost).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == T("App theme"));
                var languageChoice = Descendants(PageHost).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == T("Language"));
                using var catalogWork = StartWork(WorkKind.Catalog);
                using var updateWork = StartWork(WorkKind.AppUpdate);
                Require(catalogWork is not null && updateWork is not null, "Independent query refused");
                Require(theme.IsEnabled && languageChoice.IsEnabled && BuildNav.IsEnabled, "Query disabled unrelated UI");
                Require(ReferenceEquals(PageHost.Children.Single(), originalPage) && draft.Text == "http://draft.invalid:8123", "Starting work replaced an editor");
                RefreshWorkPage("catalog"); catalogWork.Dispose();
                Require(busy && !CanWork(WorkKind.AppUpdate) && CanWork(WorkKind.Catalog), "One query unlocked another query");
                Require(ReferenceEquals(PageHost.Children.Single(), originalPage) && draft.Text == "http://draft.invalid:8123", "Background result discarded a draft");
                updateWork.Dispose(); Require(!busy, "Query lease leaked");

                Navigate("build"); Root.UpdateLayout();
                var prepare = Descendants(PageHost).OfType<Button>().Single(c => AutomationProperties.GetName(c) == T("Prepare build tools"));
                using var buildWork = StartWork(WorkKind.Build);
                using var packageWork = StartWork(WorkKind.Packages);
                Require(buildWork is not null && packageWork is not null && !prepare.IsEnabled, "Build did not disable its own action");
                var build = BeginOperation("Build fixture", download: true);
                var packages = BeginOperation("Package fixture", download: true);
                try
                {
                    Require(OperationPicker.Visibility == Visibility.Visible && runningOperations.Count == 2, "Concurrent progress lost");
                    FinalizingOperation(build);
                    Require(operationCanCancel, "Finalizing another task disabled the selected task");
                    await RequestOperationCancellationAsync();
                    Require(packages.IsCancellationRequested && !build.IsCancellationRequested, "Cancel targeted another operation");
                    FinishOperation(packages); packageWork.Dispose();
                    Require(ReferenceEquals(activeOperation, build) && !operationCanCancel && !prepare.IsEnabled, "Completion lost the build or unlocked its controls");
                    Navigate("settings"); Root.UpdateLayout();
                    Require(Descendants(PageHost).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == T("App theme")).IsEnabled, "Build disabled appearance");
                }
                finally { FinishOperation(packages); FinishOperation(build); }
                buildWork.Dispose(); Require(!busy, "Concurrent work leaked");
            }
            var savedCatalog = catalog;
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var pending = StartWork(WorkKind.Catalog))
            using (var cancellation = new CancellationTokenSource())
            {
                catalog = []; catalogCancellation = cancellation; catalogLoading = completed.Task;
                var offline = SwitchCatalogSourceAsync("Offline");
                var online = SwitchCatalogSourceAsync("Online");
                pending!.Dispose(); completed.SetResult();
                await Task.WhenAll(offline, online);
                Require(preferences.CatalogSource == "Online", "Older source switch overwrote the latest request");
                catalog = savedCatalog; catalogCancellation = null; catalogLoading = Task.CompletedTask;
            }
            await CaptureAsync(Path.Combine(directory, "29-isolated-work.png"));
        }
        finally
        {
            foreach (var item in runningOperations.ToArray()) FinishOperation(item.Operation);
            expandedSettings.Remove("Network"); SavePreferences(saved); Navigate("runtimes");
        }
    }
}
