using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private ContentDialog? designRestartDialog;
    private bool restartInProgress;
    private bool restartRequestFailed;
    private bool restartDelayElapsed;
    private bool RestartBlockedByWork => busy || runningOperations.Count != 0;

    private async Task SelectDesignAsync(string design)
    {
        if (closed || restartInProgress || confirmationOpen || cancelDialogOpen || closeDialogOpen) return;
        if (preferences.Design == design) { UpdateDesignSelection(); return; }
        SavePreferences(preferences with { Design = design });
        // Saving can fail (for example, a locked settings file). Only a saved, pending
        // design warrants a restart prompt; choosing the active design removes it.
        if (preferences.Design == design && preferences.Design != ActiveDesign)
            await PromptDesignRestartAsync();
    }

    private async Task PromptDesignRestartAsync()
    {
        if (closed || restartInProgress || preferences.Design == ActiveDesign || designRestartDialog is not null ||
            confirmationOpen || cancelDialogOpen || closeDialogOpen) return;
        var dialog = Dialog("Restart to apply interface style", "", "Restart now");
        dialog.CloseButtonText = T("Later");
        dialog.DefaultButton = ContentDialogButton.Close;
        restartRequestFailed = false;
        restartDelayElapsed = false;
        designRestartDialog = dialog;
        RefreshDesignRestartDialog();
        dialog.Opened += async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1800));
            if (closed || !ReferenceEquals(designRestartDialog, dialog)) return;
            restartDelayElapsed = true;
            RefreshDesignRestartDialog();
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            // Keep the confirmation modal while Windows processes the request. A rejected
            // request leaves the running app and this dialog available to the user.
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try { await RequestAppRestartAsync(); }
            finally { deferral.Complete(); }
        };
        dialog.Closing += (_, args) => args.Cancel = restartInProgress;
        try { await ShowGuardedDialogAsync(dialog); }
        catch (Exception ex) { if (!closed) ShowError(ex); }
        finally { designRestartDialog = null; restartDelayElapsed = false; }
    }

    private void RefreshDesignRestartDialog()
    {
        if (designRestartDialog is not { } dialog) return;
        dialog.IsPrimaryButtonEnabled = restartDelayElapsed && !RestartBlockedByWork && !restartInProgress && preferences.Design != ActiveDesign;
        if (dialog.Content is TextBlock message)
            message.Text = T(restartInProgress ? "Restarting PyDeck…"
                : restartRequestFailed ? "PyDeck could not restart. Close and open it again to apply the saved style."
                : RestartBlockedByWork ? "A task is still running. Your interface choice is saved; you can restart after all tasks finish."
                : "Your interface choice is saved. Restart PyDeck now to apply it? Unsaved edits will be lost.");
    }

    private async Task<bool> RequestAppRestartAsync(string arguments = "")
    {
        // AppInstance.Restart terminates this process without AppWindow.Closing. A build's
        // job would kill its children, so all work must finish before invoking that API.
        if (closed || restartInProgress || RestartBlockedByWork || designRestartDialog is not null && !restartDelayElapsed)
        { RefreshDesignRestartDialog(); return false; }
        // An ordinary fixture must never relaunch outside its isolated profile.
        if (smokeDirectory is not null && !RestartProbe) return false;
        restartInProgress = true;
        RefreshAvailability();
        try
        {
            var previousSave = store.FlushAsync();
            if (previousSave.IsFaulted || previousSave.IsCanceled)
                await store.SaveAsync(preferences);
            await store.FlushAsync();
            // Set the work gate on the UI thread before yielding. StartWork cannot race
            // a queued callback into creating an operation while the restart is pending.
            var reason = await Task.Run(() => AppInstance.Restart(arguments));
            Log("Windows rejected the app restart request: " + reason);
            restartRequestFailed = true; // Success terminates this instance and never returns.
        }
        catch (Exception ex)
        {
            Log("App restart request failed: " + ex.Message);
            restartRequestFailed = true;
        }
        finally
        {
            restartInProgress = false;
            if (!closed) RefreshAvailability();
        }
        if (designRestartDialog is null && !closed)
            Notify("PyDeck could not restart. Close and open it again to apply the saved style.", InfoBarSeverity.Warning);
        return false;
    }
}
