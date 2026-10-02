using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private sealed class RunningOperation(PimOperation operation, string title, bool download)
    {
        public PimOperation Operation { get; } = operation;
        public string Title { get; } = title;
        public bool Download { get; } = download;
        public bool CanCancel { get; set; } = true;
    }
    private readonly List<RunningOperation> runningOperations = [];
    private RunningOperation? selectedOperation;
    private PimOperation? activeOperation => selectedOperation?.Operation;
    private bool operationCanCancel { get => selectedOperation?.CanCancel == true; set { if (selectedOperation is not null) selectedOperation.CanCancel = value; } }
    private bool operationIsDownload => selectedOperation?.Download == true;
    private bool cancelDialogOpen;

    private PimOperation BeginOperation(string title, bool download = false)
    {
        // Read the selected operation at dispatch time, not the producer's old snapshot.
        var operation = new PimOperation(_ => operationRefresh.Request());
        selectedOperation = new(operation, title, download);
        runningOperations.Add(selectedOperation);
        RefreshOperationChoices();
        UpdateOperationPanel();
        return operation;
    }
    private void FinishOperation(PimOperation? operation)
    {
        runningOperations.RemoveAll(item => ReferenceEquals(item.Operation, operation));
        if (ReferenceEquals(activeOperation, operation)) selectedOperation = runningOperations.LastOrDefault();
        RefreshOperationChoices();
        operation?.Dispose(); UpdateOperationPanel();
    }
    private void FinalizingOperation(PimOperation operation)
    {
        var entry = runningOperations.FirstOrDefault(item => ReferenceEquals(item.Operation, operation));
        if (entry is not null) entry.CanCancel = false;
        operation.Report(OperationPhase.Finalizing);
        UpdateOperationPanel();
    }
    private void UpdateOperationPanel()
    {
        palette.ConfigureProgress(BusyProgress);
        RefreshDesignRestartDialog();
        OperationPanel.Visibility = activeOperation is null ? Visibility.Collapsed : Visibility.Visible;
        BusyProgress.Visibility = busy && activeOperation is null ? Visibility.Visible : Visibility.Collapsed;
        if (activeOperation is null) return;
        OperationPanel.Background = Palette.Brush(palette.Card);
        OperationPanel.BorderBrush = Palette.Brush(palette.Line);
        OperationPanel.BorderThickness = new(palette.Tokens.CardBorder);
        OperationPanel.CornerRadius = new(palette.Radius);
        OperationTitleText.Text = selectedOperation!.Title;
        AutomationProperties.SetName(OperationPicker, T("Running tasks"));
        palette.ApplySurfaceResources(OperationPicker);
        OperationTitleText.Foreground = Palette.Brush(palette.Text);
        OperationPhaseText.Foreground = Palette.Brush(palette.Muted);
        var progress = activeOperation.Current;
        var label = T(progress.Phase switch
        {
            OperationPhase.Downloading => "Downloading", OperationPhase.Verifying => "Checking files",
            OperationPhase.Extracting => "Extracting files", OperationPhase.Finalizing => "Finishing up",
            OperationPhase.Compiling => "Compiling CPython", OperationPhase.Assembling => "Assembling runtime", OperationPhase.Testing => "Testing runtime",
            OperationPhase.Training => "Training PGO",
            OperationPhase.ManagingPackages => "Managing packages",
            OperationPhase.Stopping => "Stopping…", _ => "Preparing"
        });
        OperationPhaseText.Text = progress.Percent is { } percent
            ? T(progress.Approximate ? "{0} · about {1}%" : "{0} · {1}%", label, percent) : label;
        if (progress.DownloadedBytes is { } downloaded)
        {
            OperationPhaseText.Text += " · " + TransferUnits.Bytes(downloaded);
            if (progress.TotalBytes is { } total) OperationPhaseText.Text += " / " + TransferUnits.Bytes(total);
            if (progress.BytesPerSecond is { } speed) OperationPhaseText.Text += " · " + TransferUnits.Bytes(speed) + "/s";
            OperationPhaseText.Text += " · " + (progress.Remaining is { } remaining ? T("About {0} s remaining", Math.Ceiling(remaining.TotalSeconds)) : T("Time remaining unknown"));
        }
        OperationProgressBar.IsIndeterminate = progress.Percent is null;
        OperationProgressBar.Value = progress.Percent ?? 0;
        palette.ConfigureProgress(OperationProgressBar);
        AutomationProperties.SetName(OperationProgressBar, OperationPhaseText.Text);
        CancelOperationButton.Content = T(activeOperation.IsCancellationRequested ? "Stopping…" : "Cancel");
        CancelOperationButton.IsEnabled = operationCanCancel && !activeOperation.IsCancellationRequested;
        palette.ConfigureAction(CancelOperationButton, ActionRole.Secondary, compact: true);
    }
    private void RefreshOperationChoices()
    {
        OperationPicker.Items.Clear();
        foreach (var item in runningOperations) OperationPicker.Items.Add(new ComboBoxItem { Content = item.Title, Tag = item });
        OperationPicker.SelectedItem = OperationPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(item => ReferenceEquals(item.Tag, selectedOperation));
        OperationPicker.Visibility = runningOperations.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OperationPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OperationPicker.SelectedItem is ComboBoxItem { Tag: RunningOperation item })
        { selectedOperation = item; UpdateOperationPanel(); }
    }
    private async void CancelOperation_Click(object sender, RoutedEventArgs e) => await RequestOperationCancellationAsync();
    private async Task RequestOperationCancellationAsync()
    {
        var operation = activeOperation;
        if (operation is null || !operationCanCancel || operation.IsCancellationRequested || cancelDialogOpen || confirmationOpen) return;
        if (!operationIsDownload)
        {
            cancelDialogOpen = true;
            try
            {
                var dialog = Dialog("Stop this installation?", "Stopping may leave an incomplete Python installation. You may need to reinstall it. PyDeck will check the version list after stopping.", "Stop installation");
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }
            catch (Exception ex) { ShowError(ex); return; }
            finally { cancelDialogOpen = false; }
        }
        if (!ReferenceEquals(activeOperation, operation) || !operationCanCancel) return;
        operation.Cancel(); UpdateOperationPanel();
        Log("Cancellation requested by the user.");
        StatusText.Text = T("Stopping…");
    }
    private async Task ReconcileCancelledOperationAsync(bool download = false, PythonRuntime? target = null)
    {
        // The caller owns finalization; never change another selected task's cancel state.
        UpdateOperationPanel();
        try
        {
            installed = await ListInstalledAsync();
            if (!download && target is not null && installed.FirstOrDefault(r => r.Id == target.Id) is { } current)
            {
                var health = await RuntimeHealth.CheckAsync(current);
                if (!health.Healthy) { Notify(health.Message, InfoBarSeverity.Warning); StatusText.Text = T("Operation stopped"); return; }
            }
            Notify(download ? "Download stopped. An incomplete bundle may remain in the selected folder."
                : "Installation stopped. The version list has been refreshed; partial files may remain. Reinstall the version if needed.", InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            installed = localRuntimes;
            Log("Post-cancellation refresh failed: " + ex.Message);
            Notify("Stopped, but the version list could not be refreshed. Refresh it before trying again.", InfoBarSeverity.Warning);
        }
        StatusText.Text = T("Operation stopped");
    }
}
