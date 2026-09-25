using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;
using Windows.Storage.Pickers;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private UIElement CatalogSourceBar()
    {
        var row = new Grid { ColumnSpacing = palette.Tokens.ToolbarSpacing, Tag = "PageToolbar" };
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var label = palette.Label("Installation source", palette.Tokens.ControlFontSize); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
        var onlineLabel = string.IsNullOrEmpty(preferences.InstallationIndex) ? T("Python.org official catalog") : T("Custom catalog") + " · " + new Uri(preferences.InstallationIndex).Host;
        var source = Choice([("Online", onlineLabel), ("Offline", "Offline folder")], preferences.CatalogSource,
            value => _ = SwitchCatalogSourceAsync(value), "Install from");
        source.HorizontalAlignment = HorizontalAlignment.Stretch; source.MinWidth = 160;
        // Reading the online catalog can be cancelled. Installation cannot.
        source.IsEnabled = !busy || catalogCancellation is not null;
        Grid.SetColumn(source, 1); row.Children.Add(source);
        ToolTipService.SetToolTip(source, OfflineSource ? offlineBundle?.DirectoryPath ?? T("No folder selected") : InstallationSource.Validate(preferences.InstallationIndex));
        if (OfflineSource)
        {
            var choose = palette.Action("Choose folder…", "\uE8B7", compact: true); choose.IsEnabled = !busy;
            choose.Click += async (_, _) => await PickOfflineFolderAsync();
            Grid.SetColumn(choose, 2); row.Children.Add(choose);
        }
        var refresh = palette.IconAction("Refresh catalog", "\uE72C"); refresh.IsEnabled = !busy && (OfflineSource ? offlineBundle is not null : connected);
        refresh.Click += async (_, _) => { if (OfflineSource && offlineBundle is not null) await LoadOfflineFolderAsync(offlineBundle.DirectoryPath); else await LoadCatalogAsync(); };
        Grid.SetColumn(refresh, 3); row.Children.Add(refresh);
        return row;
    }

    private async Task SwitchCatalogSourceAsync(string value)
    {
        if (busy && catalogCancellation is null) return;
        try
        {
            catalogCancellation?.Cancel();
            await catalogLoading;
            if (busy) return;
            var changed = preferences with { CatalogSource = value };
            store.Save(changed); preferences = changed;
            MessageBar.IsOpen = false; search = "";
            StatusText.Text = T(OfflineSource ? "Offline packages" : "Python release catalog");
            RenderPage();
            if (!OfflineSource && catalog is null) await LoadCatalogAsync();
        }
        catch (Exception ex) { ShowError(ex); RenderPage(); }
    }

    private async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Downloads };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFolderAsync())?.Path;
    }
    private async Task PickOfflineFolderAsync()
    {
        if (busy || confirmationOpen) return;
        confirmationOpen = true;
        try { var path = await PickFolderAsync(); if (path is not null && !busy) await LoadOfflineFolderAsync(path); }
        catch (Exception ex) { ShowError(ex); }
        finally { confirmationOpen = false; }
    }
    private async Task LoadOfflineFolderAsync(string path)
    {
        if (busy) return;
        SetBusy(true, "Reading offline packages…");
        try
        {
            var bundle = await Task.Run(() => OfflineBundle.Load(path));
            offlineBundle = bundle;
            MessageBar.IsOpen = false; StatusText.Text = T("Offline packages ready");
            Log($"Loaded offline bundle: {bundle.DirectoryPath} ({bundle.Runtimes.Count} versions).");
        }
        catch (Exception ex) { offlineBundle = null; ShowError(ex); }
        finally { SetBusy(false); }
    }
    private async Task InstallOfflineRuntimeAsync(PythonRuntime runtime)
    {
        if (busy || !connected || offlineBundle is null || confirmationOpen) return;
        confirmationOpen = true;
        string? expectedInstalledVersion = null;
        try
        {
            installed = await client.ListAsync();
            var previous = installed.SingleOrDefault(r => r.Id.Equals(runtime.Id, StringComparison.OrdinalIgnoreCase));
            var replacement = previous is not null && previous.Version != runtime.Version
                ? "\n\n" + T("Python {0} will be replaced with Python {1}. These versions share an installation folder", previous.Version, runtime.Version)
                    + "\n" + T("Packages in that interpreter may need to be reinstalled. Existing virtual environments may need attention") : "";
            var dialog = Dialog(T("Install {0} from this folder?", RuntimeTitle(runtime)),
                offlineBundle.DirectoryPath + "\n\n" + T("Use bundles from a source you trust. A checksum verifies the files, not the publisher.") + replacement, "Install");
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || busy) return;
            if (replacement.Length > 0) expectedInstalledVersion = previous!.Version;
        }
        catch (Exception ex) { ShowError(ex); return; }
        finally { confirmationOpen = false; }
        var bundle = offlineBundle;
        MessageBar.IsOpen = false;
        var operation = BeginOperation(T("Installing {0}…", RuntimeTitle(runtime)));
        SetBusy(true, T("Installing {0}…", RuntimeTitle(runtime)));
        try
        {
            await client.InstallOfflineAsync(bundle, runtime, line => DispatcherQueue.TryEnqueue(() => Log(line, origin: ActivityOrigin.PythonManager)), operation: operation, expectedInstalledVersion: expectedInstalledVersion);
            FinalizingOperation(operation);
            await VerifyOperationAsync(RuntimeAction.Install, runtime);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { await ReconcileCancelledOperationAsync(target: runtime); }
        catch (Exception ex) { try { installed = await client.ListAsync(); } catch { installed = []; } ShowError(ex); }
        finally { FinishOperation(operation); SetBusy(false); UpdateConnection(); RenderPage(); }
    }
    private async Task DownloadOfflineRuntimeAsync(PythonRuntime runtime)
    {
        if (busy || !connected || confirmationOpen) return;
        string? parent;
        confirmationOpen = true;
        try { parent = await PickFolderAsync(); }
        catch (Exception ex) { ShowError(ex); return; }
        finally { confirmationOpen = false; }
        if (parent is null || busy) return;
        MessageBar.IsOpen = false;
        var operation = BeginOperation(T("Downloading {0}…", RuntimeTitle(runtime)), download: true);
        SetBusy(true, T("Downloading {0}…", RuntimeTitle(runtime)));
        try
        {
            var directory = await client.DownloadOfflineAsync(runtime, parent, line => DispatcherQueue.TryEnqueue(() => Log(line, origin: ActivityOrigin.PythonManager)), operation);
            FinalizingOperation(operation);
            Notify(T("Offline bundle saved to {0}", directory), InfoBarSeverity.Success);
            StatusText.Text = T("Offline packages ready");
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { await ReconcileCancelledOperationAsync(download: true); }
        catch (Exception ex) { ShowError(ex); }
        finally { FinishOperation(operation); SetBusy(false); }
    }
}
