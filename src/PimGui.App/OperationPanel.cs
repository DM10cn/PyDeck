using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private PimOperation? activeOperation;
    private string operationTitle = "";
    private bool operationCanCancel;
    private bool operationIsDownload;
    private bool cancelDialogOpen;

    private PimOperation BeginOperation(string title, bool download = false)
    {
        operationTitle = title; operationCanCancel = true; operationIsDownload = download;
        PimOperation? operation = null;
        operation = new PimOperation(_ => DispatcherQueue.TryEnqueue(() =>
        {
            // Ignore callbacks queued by a previous operation after navigation or completion.
            if (ReferenceEquals(activeOperation, operation)) UpdateOperationPanel();
        }));
        activeOperation = operation;
        UpdateOperationPanel();
        return operation;
    }
    private void FinishOperation(PimOperation? operation)
    {
        if (ReferenceEquals(activeOperation, operation)) { activeOperation = null; operationCanCancel = false; }
        operation?.Dispose(); UpdateOperationPanel();
    }
    private void FinalizingOperation(PimOperation operation)
    {
        operationCanCancel = false;
        operation.Report(OperationPhase.Finalizing);
        UpdateOperationPanel();
    }
    private void UpdateOperationPanel()
    {
        OperationPanel.Visibility = activeOperation is null ? Visibility.Collapsed : Visibility.Visible;
        BusyProgress.Visibility = busy && activeOperation is null ? Visibility.Visible : Visibility.Collapsed;
        if (activeOperation is null) return;
        OperationPanel.Background = Palette.Brush(palette.Card);
        OperationPanel.BorderBrush = Palette.Brush(palette.Line);
        OperationPanel.BorderThickness = new(palette.Tokens.CardBorder);
        OperationPanel.CornerRadius = new(palette.Radius);
        OperationTitleText.Text = operationTitle;
        OperationTitleText.Foreground = Palette.Brush(palette.Text);
        OperationPhaseText.Foreground = Palette.Brush(palette.Muted);
        var progress = activeOperation.Current;
        var label = T(progress.Phase switch
        {
            OperationPhase.Downloading => "Downloading", OperationPhase.Verifying => "Checking files",
            OperationPhase.Extracting => "Extracting files", OperationPhase.Finalizing => "Finishing up",
            OperationPhase.Stopping => "Stopping…", _ => "Preparing"
        });
        OperationPhaseText.Text = progress.Percent is { } percent
            ? T(progress.Approximate ? "{0} · about {1}%" : "{0} · {1}%", label, percent) : label;
        OperationProgressBar.IsIndeterminate = progress.Percent is null;
        OperationProgressBar.Value = progress.Percent ?? 0;
        OperationProgressBar.Foreground = Palette.Brush(palette.Accent);
        AutomationProperties.SetName(OperationProgressBar, OperationPhaseText.Text);
        CancelOperationButton.Content = T(activeOperation.IsCancellationRequested ? "Stopping…" : "Cancel");
        CancelOperationButton.IsEnabled = operationCanCancel && !activeOperation.IsCancellationRequested;
        palette.ApplySurfaceResources(CancelOperationButton);
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
    private async Task ReconcileCancelledOperationAsync(bool download = false)
    {
        operationCanCancel = false; UpdateOperationPanel();
        try
        {
            installed = await client.ListAsync();
            Notify(download ? "Download stopped. An incomplete bundle may remain in the selected folder."
                : "Installation stopped. The version list has been refreshed; partial files may remain. Reinstall the version if needed.", InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            Log("Post-cancellation refresh failed: " + ex.Message);
            Notify("Stopped, but the version list could not be refreshed. Refresh it before trying again.", InfoBarSeverity.Warning);
        }
        StatusText.Text = T("Operation stopped");
    }
}
