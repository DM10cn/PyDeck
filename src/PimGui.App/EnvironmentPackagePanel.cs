using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private string packageEnvironment = "";
    private string packageInput = "";
    private PackageSnapshot? packageSnapshot;
    private EnvironmentPackages Packages => new(store.DirectoryPath, preferences.Network, ProxyPassword());

    private UIElement PackagePanel(VirtualEnvironment environment)
    {
        var snapshot = packageSnapshot;
        var panel = new StackPanel { Spacing = 10, Tag = "EnvironmentPackages" };
        panel.Children.Add(palette.Label("Packages", 16, true));
        panel.Children.Add(palette.Label(environment.Executable, 12, muted: true));
        Button Action(string title, Func<Task> action, bool enabled = true)
        {
            var button = palette.Action(title, compact: true, role: title == "Uninstall" ? ActionRole.Destructive : ActionRole.Secondary); BindAvailability(button, () => CanWork(WorkKind.Packages) && enabled);
            button.Click += async (_, _) => await action(); return button;
        }
        panel.Children.Add(Toolbar(Action("Refresh packages", () => OpenPackagesAsync(environment)),
            Action(snapshot?.HasPip == true ? "Upgrade pip" : "Prepare pip", () => ChangePackagesAsync(environment,
                snapshot?.HasPip == true ? PackageAction.UpgradePip : PackageAction.PreparePip, []))));
        var input = new TextBox { Text = packageInput, Header = T("Package requirements"), PlaceholderText = "requests>=2.32\nnumpy==2.3.3",
            AcceptsReturn = true, MaxLength = 65536, MinHeight = 64, MaxHeight = 140, IsEnabled = CanWork(WorkKind.Packages) };
        BindAvailability(input, () => CanWork(WorkKind.Packages));
        input.TextChanged += (_, _) => packageInput = input.Text; panel.Children.Add(input);
        panel.Children.Add(palette.Label("Names and version constraints, one per line; PyPI only", 12, muted: true));
        panel.Children.Add(PackageToolbar(Action("Install packages", async () =>
        {
            try { await ChangePackagesAsync(environment, PackageAction.Install, EnvironmentPackages.ParseRequirements(packageInput)); }
            catch (Exception ex) { ShowError(ex); }
        }, snapshot?.HasPip == true), Action("Import requirements", ImportRequirementsAsync),
            Action("Export requirements", ExportRequirementsAsync, snapshot is not null)));
        if (snapshot is null) panel.Children.Add(palette.Label("Refresh packages to view the current state", 13, muted: true));
        else
        {
            if (snapshot.DependencyIssue.Length > 0)
                panel.Children.Add(new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning, Title = T("Package dependencies need attention"), Message = snapshot.DependencyIssue });
            panel.Children.Add(palette.Label(T("{0} packages", snapshot.Packages.Count), 12, muted: true));
            var rows = new StackPanel { Spacing = 6 };
            void Populate(string filter)
            {
                CancelSearchRefresh();
                rows.Children.Clear();
                foreach (var package in snapshot.Packages.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                {
                    var row = new Grid { ColumnSpacing = 8 };
                    row.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                    var name = palette.Label(package.Name + "  " + package.Version, 14); name.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(name);
                    var buttons = Toolbar(Action("Update", () => ChangePackagesAsync(environment, PackageAction.Upgrade, [package.Name]), snapshot.HasPip),
                        Action("Uninstall", () => ChangePackagesAsync(environment, PackageAction.Uninstall, [package.Name]), snapshot.HasPip && !package.Name.Equals("pip", StringComparison.OrdinalIgnoreCase)));
                    Grid.SetColumn(buttons, 1); row.Children.Add(buttons); rows.Children.Add(row);
                }
            }
            var search = new TextBox { PlaceholderText = T("Search packages") };
            search.TextChanged += (_, _) => ScheduleSearchRefresh(() => Populate(search.Text));
            search.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) Populate(search.Text); };
            panel.Children.Add(search); Populate("");
            panel.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new(0, 0, 20, 0) });
        }
        var close = Action("Close packages", () => { packageEnvironment = ""; packageSnapshot = null; RenderPage(); return Task.CompletedTask; });
        panel.Children.Add(close); return panel;
    }
    private StackPanel PackageToolbar(params UIElement[] children)
    {
        var toolbar = Toolbar(children);
        toolbar.SizeChanged += (_, e) => toolbar.Orientation = e.NewSize.Width < 620 ? Orientation.Vertical : Orientation.Horizontal;
        return toolbar;
    }
    private async Task OpenPackagesAsync(VirtualEnvironment environment)
    {
        if (confirmationOpen) return;
        using var work = StartWork(WorkKind.Packages);
        if (work is null) return;
        if (packageEnvironment != environment.Path)
        {
            confirmationOpen = true;
            try { if (await Dialog("Packages", T("This runs the interpreter in the selected environment") + "\n\n" + environment.Executable, "Continue").ShowAsync() != ContentDialogResult.Primary) return; }
            finally { confirmationOpen = false; }
            packageInput = "";
        }
        packageEnvironment = environment.Path; packageSnapshot = null; StatusText.Text = T("Checking files");
        RefreshWorkPage("environments");
        try { packageSnapshot = await Packages.ListAsync(environment.Path); }
        catch (Exception ex) { ShowError(ex); }
        finally { RefreshWorkPage("environments"); }
    }
    private async Task ChangePackagesAsync(VirtualEnvironment environment, PackageAction action, string[] requirements)
    {
        if (confirmationOpen) return;
        using var work = StartWork(WorkKind.Packages);
        if (work is null) return;
        confirmationOpen = true;
        try
        {
            var description = T("Changes apply only to this virtual environment. Package installation can execute package build code") + "\n\n" + environment.Path + "\n\n" +
                T(action switch { PackageAction.PreparePip => "Prepare pip", PackageAction.UpgradePip => "Upgrade pip", PackageAction.Uninstall => "Uninstall", PackageAction.Upgrade => "Update", _ => "Install packages" }) + "\n" + string.Join('\n', requirements);
            var dialog = Dialog("Review package changes", "", "Continue");
            dialog.Content = new ScrollViewer { Content = palette.Label(description, 14), MaxHeight = 350, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        finally { confirmationOpen = false; }
        var operation = BeginOperation(T("Managing packages"), download: true);
        operation.Report(OperationPhase.ManagingPackages);
        packageSnapshot = null; StatusText.Text = T("Managing packages");
        try
        {
            packageSnapshot = await Packages.ChangeAsync(environment.Path, action, requirements,
                CreateOutputLogger(), operation.Token);
            Notify(packageSnapshot.DependencyIssue.Length == 0 ? "Package changes completed" : "Package dependencies need attention",
                packageSnapshot.DependencyIssue.Length == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) { Notify("Package operation stopped; refresh to check partial changes", InfoBarSeverity.Warning); }
        catch (Exception ex) { ShowError(ex); }
        finally
        {
            // pip is not transactional: reconcile after success, failure and cancellation.
            try { packageSnapshot = await Packages.ListAsync(environment.Path); }
            catch { packageSnapshot = null; }
            FinishOperation(operation); RefreshWorkPage("environments");
        }
    }
    private async Task ImportRequirementsAsync()
    {
        if (!CanWork(WorkKind.Packages) || confirmationOpen) return; confirmationOpen = true;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add(".txt");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (await picker.PickSingleFileAsync() is { } file)
            {
                SafeFiles.RequireNoLinks(file.Path); packageInput = string.Join('\n', EnvironmentPackages.ParseRequirements(SafeFiles.ReadText(file.Path))); RenderPage();
            }
        }
        catch (Exception ex) { ShowError(ex); }
        finally { confirmationOpen = false; }
    }
    private async Task ExportRequirementsAsync()
    {
        if (!CanWork(WorkKind.Packages) || confirmationOpen || packageSnapshot is null) return; confirmationOpen = true;
        try
        {
            var text = EnvironmentPackages.Export(packageSnapshot);
            var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = "requirements" };
            picker.FileTypeChoices.Add("Requirements", new List<string> { ".txt" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (await picker.PickSaveFileAsync() is { } file) { SafeFiles.RequireNoLinks(file.Path); await File.WriteAllTextAsync(file.Path, text); }
        }
        catch (Exception ex) { ShowError(ex); }
        finally { confirmationOpen = false; }
    }
}
