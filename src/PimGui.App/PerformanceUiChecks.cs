using Microsoft.UI.Xaml;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckBatchedUpdatesAsync()
    {
        var originalFilter = activityFilter;
        PimOperation? operation = null;
        try
        {
            activityFilter = null; activityLog.Clear(); Navigate("activity");
            var originalPage = PageHost.Children.Single();
            var output = CreateOutputLogger(ActivityOrigin.PythonManager);
            operation = BeginOperation("Progress flood fixture", download: true);
            // Hold this UI turn while producers flood notifications, then allow dispatch.
            Parallel.For(0, 3000, i => { output("WARN burst " + i); operation.Transfer(i, 3000, null, null); });
            operation.Transfer(3000, 3000, null, null);
            await WaitForSmokeConditionAsync(() => activityRows.Count == 2000 && OperationProgressBar.Value == 100,
                "Batched activity/progress did not publish the latest state");
            if (!ReferenceEquals(PageHost.Children.Single(), originalPage) || activityRows.Any(row => row.Entry.Level != ActivityLevel.Warning))
                throw new IOException("Background notifications rebuilt the page or lost severity");
            output("WARN pending clear"); activityLog.Clear(); RefreshActivityOutput(reset: true);
            FinishOperation(operation); operation = null;
            await Task.Delay(40);
            if (activityRows.Count != 0 || OperationPanel.Visibility != Visibility.Collapsed)
                throw new IOException("Queued notification resurrected cleared activity or a finished operation");
            Navigate("settings"); var editor = PageHost.Children.Single();
            output("WARN offscreen activity"); await Task.Delay(40);
            if (!ReferenceEquals(PageHost.Children.Single(), editor)) throw new IOException("Hidden activity replaced settings");
            Navigate("activity");
            if (!ActivityVisibleText.Contains("offscreen activity")) throw new IOException("Hidden activity was discarded");
        }
        finally { if (operation is not null) FinishOperation(operation); activityFilter = originalFilter; activityLog.Clear(); Navigate("runtimes"); }
    }
}
