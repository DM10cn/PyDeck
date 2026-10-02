using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private IReadOnlyList<StorageEntry>? storageEntries;
    private bool storageExpanded;
    private async Task PrepareBuildDependenciesAsync()
    {
        if (confirmationOpen) return;
        using var work = StartWork(WorkKind.Build);
        if (work is null) return;
        var operation = BeginOperation(T("Preparing build tools"), download: true); StatusText.Text = T("Preparing build tools");
        try
        {
            buildBootstrap = await Task.Run(() => new BuildDependencies(store.DirectoryPath).PrepareAsync(preferences.Network, ProxyPassword(), operation));
            buildTools = null;
            try
            {
                buildToolChoices = await BuildToolchain.DetectAllAsync(buildBootstrap, operation.Token); buildTools = buildToolChoices[0];
                Notify("Build tools ready", InfoBarSeverity.Success);
            }
            catch (IOException ex) { Notify(T("Build Python is ready; install the missing C++ tools and check again") + "\n" + T(ex.Message), InfoBarSeverity.Warning); }
        }
        catch (OperationCanceledException) { Notify("Preparation stopped", InfoBarSeverity.Warning); }
        catch (Exception ex) { ShowError(ex); }
        finally { FinishOperation(operation); RefreshWorkPage("build"); }
    }
    private UIElement StorageSettings()
    {
        var expander = palette.Expander("Build storage", StorageSettingsContent(), storageExpanded);
        expander.Expanding += (_, _) => storageExpanded = true; expander.Collapsed += (_, _) => storageExpanded = false;
        settingsSections["Build storage"] = (expander, StorageSettingsContent);
        return expander;
    }
    private UIElement StorageSettingsContent()
    {
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(palette.Label("Scan build downloads, temporary files and runtimes. History and logs are kept", 13, muted: true));
        var scan = palette.Action("Scan storage", compact: true); BindAvailability(scan, () => CanWork(WorkKind.Storage));
        scan.Click += async (_, _) =>
        {
            using var work = StartWork(WorkKind.Storage, "Scanning storage");
            if (work is null) return;
            try { storageEntries = await Task.Run(() => new BuildStorage(store.DirectoryPath).Scan()); }
            catch (Exception ex) { ShowError(ex); }
            finally { RefreshSettingsSection("Build storage"); }
        };
        panel.Children.Add(scan);
        if (storageEntries is not null)
        {
            panel.Children.Add(palette.Label(T("Total: {0}", TransferUnits.Bytes(storageEntries.Sum(e => e.Bytes))), 14, true));
            foreach (var entry in storageEntries)
            {
                var row = new StackPanel { Spacing = 4 };
                row.Children.Add(palette.Label(T(entry.Kind) + " · " + TransferUnits.Bytes(entry.Bytes), 14, true));
                var path = palette.Label(entry.Path, 12, muted: true); path.IsTextSelectionEnabled = true; row.Children.Add(path);
                if (entry.Users > 0) row.Children.Add(palette.Label(T("Used by {0} registered environments", entry.Users), 12));
                var clean = palette.Action("Clean files", compact: true, role: ActionRole.Destructive); BindAvailability(clean, () => CanWork(WorkKind.Storage) && entry.Users == 0);
                clean.HorizontalAlignment = HorizontalAlignment.Left;
                clean.Click += async (_, _) => await CleanStorageAsync(entry); row.Children.Add(clean);
                panel.Children.Add(palette.CardBox(row, 12));
            }
        }
        return panel;
    }
    private async Task CleanStorageAsync(StorageEntry entry)
    {
        if (confirmationOpen) return;
        using var work = StartWork(WorkKind.Storage);
        if (work is null) return; confirmationOpen = true;
        try
        {
            var message = T("These files will be permanently deleted") + "\n\n" + entry.Path + "\n" + TransferUnits.Bytes(entry.Bytes);
            if (entry.Kind is "Built runtime" or "Build tools") message += "\n\n" + T("Only registered environments are checked. Unregistered projects may still use this Python");
            if (await Dialog("Clean files", message, "Delete").ShowAsync() != ContentDialogResult.Primary) return;
        }
        finally { confirmationOpen = false; }
        StatusText.Text = T("Cleaning storage");
        try
        {
            await Task.Run(() => new BuildStorage(store.DirectoryPath).Clean(entry));
            ReloadLocalRuntimes(); storageEntries = await Task.Run(() => new BuildStorage(store.DirectoryPath).Scan());
            Notify("Storage cleaned", InfoBarSeverity.Success);
        }
        catch (Exception ex) { storageEntries = null; ReloadLocalRuntimes(); ShowError(ex); }
        finally { RefreshSettingsSection("Build storage"); RefreshWorkPage("runtimes", "build"); }
    }
    private string RuntimeUsageText(PythonRuntime runtime)
    {
        var matches = RuntimeUsage.Find(runtime, Environments.Read());
        return T("Used by {0} registered environments", matches.Count) + "\n" +
            string.Join('\n', matches.Select(e => e.Name + " · " + e.Path)) + "\n\n" + T("Only registered environments are checked. Unregistered projects may still use this Python");
    }
    private async Task ShowRuntimeUsageAsync(PythonRuntime runtime)
    {
        if (confirmationOpen) return; confirmationOpen = true;
        try
        {
            var dialog = Dialog("Runtime usage", "", "Close");
            dialog.Content = new ScrollViewer { Content = palette.Label(RuntimeUsageText(runtime), 14), MaxHeight = 360,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            await dialog.ShowAsync();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { confirmationOpen = false; }
    }
}
