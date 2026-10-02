using Microsoft.UI.Dispatching;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer? searchTimer;
    private Action? pendingSearchRefresh;

    private void ScheduleSearchRefresh(Action refresh)
    {
        if (searchTimer is null)
        {
            searchTimer = DispatcherQueue.CreateTimer();
            searchTimer.IsRepeating = false;
            searchTimer.Interval = TimeSpan.FromMilliseconds(160);
            searchTimer.Tick += (_, _) =>
            {
                var update = pendingSearchRefresh;
                pendingSearchRefresh = null;
                update?.Invoke();
            };
        }
        searchTimer.Stop();
        pendingSearchRefresh = refresh;
        searchTimer.Start();
    }

    private void CancelSearchRefresh()
    {
        searchTimer?.Stop();
        pendingSearchRefresh = null;
    }
}
