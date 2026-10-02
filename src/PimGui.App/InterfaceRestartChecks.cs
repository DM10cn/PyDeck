using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckInterfaceRestartAsync(string directory)
    {
        if (smokeDirectory is null || !Path.GetFullPath(store.DirectoryPath).StartsWith(Path.GetFullPath(smokeDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Restart dialog fixtures require an isolated profile");
        var original = preferences;
        var originalPage = page;
        var networkWasExpanded = expandedSettings.Contains("Network");
        Task? selection = null;
        IDisposable? work = null;
        PimOperation? operation = null;
        try
        {
            foreach (var design in new[] { "Fluent", "Material" })
            foreach (var language in Strings.Languages)
            {
                ApplySmokePreferences(preferences with { Design = design, Language = language, Theme = "Dark" });
                expandedSettings.Add("Network");
                Navigate("settings"); Root.UpdateLayout();
                var pageVisual = PageHost.Children.Single();
                var draft = Descendants(settingsSections["Network"].Section).OfType<TextBox>().First();
                const string unsaved = "http://unsaved-restart-fixture.invalid:8123";
                draft.Text = unsaved;
                var target = design == "Material" ? "Fluent" : "Material";
                var opened = System.Diagnostics.Stopwatch.StartNew();
                selection = SelectDesignAsync(target);
                var dialog = await WaitForInterfaceRestartDialogAsync();
                if (dialog.Title as string != T("Restart to apply interface style") ||
                    dialog.PrimaryButtonText != T("Restart now") || dialog.CloseButtonText != T("Later") ||
                    dialog.DefaultButton != ContentDialogButton.Close || dialog.IsPrimaryButtonEnabled || !RestartDialogButton(dialog, "Later").IsEnabled)
                    throw new IOException($"Restart dialog captions or safe default are incorrect: {design}/{language}");
                if (language != "en-US" && (dialog.PrimaryButtonText == "Restart now" || dialog.CloseButtonText == "Later"))
                    throw new IOException("Restart dialog captions are untranslated: " + language);
                if (preferences.Design != target || store.Load().Design != target || ActiveDesign != design ||
                    !ReferenceEquals(pageVisual, PageHost.Children.Single()) || !draft.IsLoaded || draft.Text != unsaved)
                    throw new IOException("Opening restart confirmation changed the active interface or discarded a draft");
                if (language == "en-US")
                {
                    await Task.Delay(1000);
                    if (dialog.IsPrimaryButtonEnabled || !RestartDialogButton(dialog, "Later").IsEnabled)
                        throw new IOException("Restart became available before its confirmation delay or Later was disabled");
                    await WaitForSmokeConditionAsync(() => dialog.IsPrimaryButtonEnabled, "Idle restart did not become available after its confirmation delay");
                    if (opened.Elapsed < TimeSpan.FromMilliseconds(1800))
                        throw new IOException("Restart confirmation delay was shorter than 1.8 seconds");
                }
                if (design == "Material") await CaptureAsync(Path.Combine(directory, "interface-restart-" + language + ".png"));
                InvokeButton(RestartDialogButton(dialog, "Later"));
                await WaitForSmokeConditionAsync(() => selection.IsCompleted, "Later did not finish the interface selection");
                await selection; selection = null;
                if (designRestartDialog is not null || restartInProgress || closed || ActiveDesign != design ||
                    preferences.Design != target || store.Load().Design != target || !draft.IsLoaded || draft.Text != unsaved)
                    throw new IOException("Later did not retain the saved selection and current session");
                selection = SelectDesignAsync(target);
                await WaitForSmokeConditionAsync(() => selection.IsCompleted, "Selecting the saved interface style opened another dialog");
                await selection; selection = null;
                if (designRestartDialog is not null || RestartDialogIsOpen())
                    throw new IOException("Selecting the saved interface style repeated the restart prompt");
            }

            ApplySmokePreferences(preferences with { Design = "Material", Language = "en-US" });
            Navigate("settings"); Root.UpdateLayout();
            using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                selection = SelectDesignAsync("Fluent");
                await WaitForSmokeConditionAsync(() => selection.IsCompleted, "A failed style save opened a restart prompt");
                await selection; selection = null;
                if (preferences.Design != "Material" || designRestartDialog is not null || RestartDialogIsOpen() || restartInProgress ||
                    designChoices.Values.Count(choice => choice.IsChecked == true) != 1 || designChoices["Material"].IsChecked != true)
                    throw new IOException("A failed style save changed the selection or requested a restart");
            }
            if (store.Load().Design != "Material") throw new IOException("A failed style save changed the persisted style");
            MessageBar.IsOpen = false;

            work = StartWork(WorkKind.Build) ?? throw new IOException("Could not start the restart protection work fixture");
            operation = BeginOperation("Restart protection fixture");
            selection = SelectDesignAsync("Fluent");
            var busyDialog = await WaitForInterfaceRestartDialogAsync();
            await WaitForSmokeConditionAsync(() => !busyDialog.IsPrimaryButtonEnabled, "Restart was enabled during active work");
            await Task.Delay(1850);
            if (busyDialog.IsPrimaryButtonEnabled) throw new IOException("The confirmation delay enabled Restart while work remained active");
            if (!workCoordinator.Contains(WorkKind.Build) || operation.IsCancellationRequested || restartInProgress || !RestartDialogButton(busyDialog, "Later").IsEnabled)
                throw new IOException("Opening the busy restart dialog affected a task or disabled Later");
            work.Dispose(); work = null;
            if (busyDialog.IsPrimaryButtonEnabled || operation.IsCancellationRequested || await RequestAppRestartAsync())
                throw new IOException("A remaining operation did not prevent restart");
            FinishOperation(operation); operation = null;
            await WaitForSmokeConditionAsync(() => busyDialog.IsPrimaryButtonEnabled, "Restart did not become available when work completed");
            if (selection.IsCompleted || restartInProgress || closed)
                throw new IOException("Work completion automatically restarted the application");

            // New work can arrive after the dialog was enabled. Both the displayed action and
            // the final restart entry point must recheck it rather than trust the earlier state.
            work = StartWork(WorkKind.Build) ?? throw new IOException("Could not start the late work fixture");
            await WaitForSmokeConditionAsync(() => !busyDialog.IsPrimaryButtonEnabled, "Late work left Restart now enabled");
            InvokeButton(RestartDialogButton(busyDialog, "Later"));
            await WaitForSmokeConditionAsync(() => selection.IsCompleted, "The busy restart prompt did not close");
            await selection; selection = null;
            var restarted = await RequestAppRestartAsync();
            if (restarted || restartInProgress || closed || !workCoordinator.Contains(WorkKind.Build) || preferences.Design != "Fluent" || store.Load().Design != "Fluent")
                throw new IOException("The final restart guard ignored late work or lost the saved style");
            work.Dispose(); work = null;
            MessageBar.IsOpen = false;
        }
        finally
        {
            designRestartDialog?.Hide();
            if (selection is not null)
            {
                try { await selection.WaitAsync(TimeSpan.FromSeconds(2)); }
                catch (Exception) { /* Preserve the original fixture failure after dismissing its dialog. */ }
            }
            if (operation is not null) FinishOperation(operation);
            work?.Dispose();
            if (!networkWasExpanded) expandedSettings.Remove("Network");
            if (!closed && !restartInProgress) { ApplySmokePreferences(original); Navigate(originalPage); }
        }
    }

    private async Task<ContentDialog> WaitForInterfaceRestartDialogAsync()
    {
        await WaitForSmokeConditionAsync(() => designRestartDialog is { IsLoaded: true } && RestartDialogIsOpen(), "Interface restart confirmation did not open");
        var dialog = designRestartDialog!;
        await WaitForSmokeConditionAsync(() => Descendants(dialog).OfType<Button>().Any(button => button.Content as string == T("Later")), "Interface restart buttons did not load");
        return dialog;
    }

    private bool RestartDialogIsOpen() => VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot)
        .SelectMany(popup => new[] { popup.Child }.Concat(Descendants(popup.Child)))
        .OfType<ContentDialog>().Any(dialog => dialog.Title as string == T("Restart to apply interface style"));

    private Button RestartDialogButton(ContentDialog dialog, string label) =>
        Descendants(dialog).OfType<Button>().Single(button => button.Content as string == T(label));
}
