using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private readonly WorkCoordinator workCoordinator = new();
    // Predicates may capture their control through a shared closure. An ordinary
    // list of weak keys + strong predicates would still retain the old visual tree.
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, Func<bool>> availability = new();
    private readonly HashSet<string> pendingPageRefresh = [];
    private bool pageRefreshQueued;
    private bool busy => workCoordinator.Any;
    private bool CanWork(WorkKind kind) => workCoordinator.CanStart(kind);
    private IDisposable? StartWork(WorkKind kind, string? status = null)
    {
        var lease = workCoordinator.TryStart(kind, () => { RefreshAvailability(); UpdateOperationPanel(); });
        if (lease is null) return null;
        if (status is not null) StatusText.Text = T(status);
        RefreshAvailability(); UpdateOperationPanel();
        return lease;
    }
    private void BindAvailability(Control control, Func<bool> enabled)
    {
        availability.Remove(control);
        availability.Add(control, enabled); control.IsEnabled = enabled();
    }
    private void RefreshAvailability()
    {
        foreach (var item in availability.ToArray())
            item.Key.IsEnabled = item.Value();
        RefreshDatabaseButton.IsEnabled = CanWork(WorkKind.Runtimes) && CanWork(WorkKind.Catalog);
    }
    private void RefreshWorkPage(params string[] pages)
    {
        foreach (var target in pages) pendingPageRefresh.Add(target);
        if (pageRefreshQueued) return;
        pageRefreshQueued = true;
        // Await continuations run on the dispatcher. Rebuild after their using leases
        // unwind, so empty/error views never get stuck showing a completed task as busy.
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            pageRefreshQueued = false;
            var refresh = pendingPageRefresh.Contains(page); pendingPageRefresh.Clear();
            if (refresh) RenderPage();
        })) { pageRefreshQueued = false; pendingPageRefresh.Clear(); }
    }
}
