using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;
using System.Diagnostics;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private VirtualEnvironments Environments => new(store.DirectoryPath);
    private string environmentSearch = "";
    private string environmentStatus = "All";
    private UIElement BuildEnvironmentsPage()
    {
        var layout = PageGrid(GridLength.Auto, GridLength.Auto, GridLength.Auto, new(1, GridUnitType.Star));
        Button Action(string title, string icon, Func<Task> action, bool primary = false)
        {
            var button = palette.Action(title, icon, primary, compact: true); button.IsEnabled = !busy;
            button.Click += async (_, _) => await action();
            return button;
        }
        IReadOnlyList<VirtualEnvironment> environments;
        try
        {
            environments = Environments.Read();
        }
        catch (Exception ex)
        {
            At(layout, Header("YOUR PROJECTS", "Virtual environments", "Separate Python environments for your projects"), 0);
            At(layout, palette.Label(T("Environment list unavailable") + "\n" + SensitiveText.Redact(ex.Message), 14), 3);
            return layout;
        }
        var create = Action("Create environment", "\uE710", CreateEnvironmentAsync, primary: true);
        var import = Action("Import environment", "\uE8B5", ImportEnvironmentAsync);
        At(layout, Header("YOUR PROJECTS", "Virtual environments", "Separate Python environments for your projects", environments.Count == 0 ? null : create), 0);
        if (environments.Count == 0)
        {
            var empty = (StackPanel)Empty("\uE8B7", "No environments yet", "Create an environment or import an existing folder");
            empty.Tag = "EnvironmentsEmpty";
            var actions = Toolbar(create, import); actions.HorizontalAlignment = HorizontalAlignment.Center;
            empty.Children.Add(actions);
            At(layout, empty, 3);
            return layout;
        }

        var rows = new StackPanel { Spacing = 8 };
        var count = palette.Label("", palette.Tokens.CaptionFontSize, muted: true);
        void Populate()
        {
            var matches = environments.Where(environment =>
                (environment.Name + " " + environment.Path + " " + environment.Version + " " + environment.BasePath)
                    .Contains(environmentSearch, StringComparison.OrdinalIgnoreCase) &&
                (environmentStatus switch
                {
                    "Ready" => environment.State == "Environment ready",
                    "Unchecked" => environment.State == "Not checked",
                    "Attention" => environment.State is not "Environment ready" and not "Not checked",
                    _ => true
                })).OrderBy(environment => environment.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
            rows.Children.Clear();
            count.Text = T("{0} environments", matches.Length);
            if (matches.Length == 0) rows.Children.Add(Empty("\uE721", "No environments match", "Try another search or status"));
            foreach (var environment in matches) rows.Children.Add(EnvironmentRow(environment));
        }
        var search = new AutoSuggestBox { Text = environmentSearch, PlaceholderText = T("Search environments"), QueryIcon = new SymbolIcon(Symbol.Find),
            FontSize = palette.Tokens.ControlFontSize, MinHeight = palette.Tokens.ControlHeight, MinWidth = 160 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search, T("Search environments"));
        search.TextChanged += (sender, _) => { environmentSearch = sender.Text; Populate(); };
        var status = Choice([("All", "All statuses"), ("Ready", "Ready"), ("Unchecked", "Not checked"), ("Attention", "Needs attention")],
            environmentStatus, value => { environmentStatus = value; Populate(); }, "Environment status");
        var toolbar = new Grid { RowSpacing = 8, ColumnSpacing = palette.Tokens.ToolbarSpacing, Tag = "PageToolbar" };
        toolbar.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); toolbar.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto }); toolbar.RowDefinitions.Add(new() { Height = GridLength.Auto });
        toolbar.Children.Add(search); Grid.SetColumn(status, 1); toolbar.Children.Add(status);
        var utilities = Toolbar(import, Action("Refresh", "\uE72C", RefreshEnvironmentsAsync));
        Grid.SetRow(utilities, 1); Grid.SetColumnSpan(utilities, 2); toolbar.Children.Add(utilities);
        At(layout, toolbar, 1); At(layout, count, 2);
        At(layout, new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 3);
        Populate();
        return layout;
    }
    private UIElement EnvironmentRow(VirtualEnvironment environment)
    {
        var body = new Grid { ColumnSpacing = 12, RowSpacing = 8, Tag = "EnvironmentRow" };
        body.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) }); body.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        body.RowDefinitions.Add(new() { Height = GridLength.Auto }); body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var details = new StackPanel { Spacing = 4 };
        details.Children.Add(palette.Label(environment.Name, 16, true));
        if (environment.Version.Length > 0) details.Children.Add(palette.Label("Python " + environment.Version, palette.Tokens.CaptionFontSize, muted: true));
        var path = palette.Label(environment.Path, palette.Tokens.CaptionFontSize, muted: true); path.IsTextSelectionEnabled = true; details.Children.Add(path);
        if (environment.BasePath.Length > 0)
            details.Children.Add(palette.Label(T("Base interpreter: {0}", environment.BasePath), palette.Tokens.CaptionFontSize, muted: true));
        Grid.SetColumnSpan(details, 2); body.Children.Add(details);
        var state = palette.Label(environment.State, palette.Tokens.CaptionFontSize, muted: environment.State == "Not checked");
        state.VerticalAlignment = VerticalAlignment.Center; Grid.SetRow(state, 1); body.Children.Add(state);
        var terminal = palette.Action("Terminal", "\uE756", compact: true); terminal.IsEnabled = !busy;
        terminal.Click += async (_, _) => await OpenEnvironmentTerminalAsync(environment);
        var more = palette.IconAction(T("Environment actions") + " · " + environment.Name, "\uE712"); more.IsEnabled = !busy;
        var menu = RuntimeMenu();
        foreach (var (title, icon, action) in new (string, string, Func<Task>)[] {
            ("Check environment", "\uE73E", async () => { await CheckEnvironmentAsync(environment); }),
            ("Open folder", "\uE8B7", () => { OpenFolder(new("", "", "", "", "", "", environment.Path, false)); return Task.CompletedTask; }),
            ("Remove from list", "\uE74D", () => RemoveEnvironmentAsync(environment)) })
        {
            var item = new MenuFlyoutItem { Text = T(title), Icon = new FontIcon { Glyph = icon, FontSize = palette.Tokens.ControlIconSize }, IsEnabled = !busy };
            item.Click += async (_, _) => await action(); menu.Items.Add(item);
        }
        more.Flyout = menu;
        var actions = Toolbar(terminal, more); Grid.SetColumn(actions, 1); Grid.SetRow(actions, 1); body.Children.Add(actions);
        return palette.CardBox(body, palette.Tokens.RowPadding);
    }
    private async Task RefreshEnvironmentsAsync()
    {
        if (busy || confirmationOpen) return;
        confirmationOpen = true;
        SetBusy(true, "Checking files");
        try
        {
            await Environments.RefreshAsync(new ProcessRunner(), async verified =>
            {
                var content = new StackPanel { Spacing = 12 };
                foreach (var environment in verified)
                {
                    var item = new StackPanel { Spacing = 4 };
                    item.Children.Add(palette.Label(environment.Name, palette.Tokens.BodyFontSize, true));
                    item.Children.Add(palette.Label("This runs the interpreter in the selected environment", palette.Tokens.CaptionFontSize, muted: true));
                    var path = palette.Label(environment.Executable, palette.Tokens.CaptionFontSize, muted: true);
                    path.IsTextSelectionEnabled = true; item.Children.Add(path); content.Children.Add(item);
                }
                var dialog = Dialog("Recheck environments", "", "Check");
                dialog.Content = new ScrollViewer { Content = content, MaxHeight = 320,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
                return await dialog.ShowAsync() == ContentDialogResult.Primary;
            });
        }
        catch (Exception ex) { ShowError(ex); }
        finally { confirmationOpen = false; SetBusy(false); }
    }
    private async Task ImportEnvironmentAsync()
    {
        if (busy || confirmationOpen) return; confirmationOpen = true;
        try
        {
            var path = await PickFolderAsync(); if (path is null) return;
            var environment = VirtualEnvironments.Inspect(path);
            if (!File.Exists(Path.Combine(path, "pyvenv.cfg"))) throw new IOException("Choose a folder containing pyvenv.cfg");
            Environments.Remember(environment); RenderPage();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { confirmationOpen = false; }
    }
    private async Task CreateEnvironmentAsync()
    {
        if (busy || confirmationOpen) return; confirmationOpen = true;
        string? parent = null, name = null; PythonRuntime? runtime = null;
        try
        {
            if (!connected) { Notify("Connect to Python Install Manager first.", InfoBarSeverity.Warning); return; }
            installed = await client.ListAsync();
            var available = installed.Where(r => !r.IsEmbeddable).ToArray();
            if (available.Length == 0) { Notify("Install a Python version first", InfoBarSeverity.Warning); return; }
            parent = await PickFolderAsync(); if (parent is null) return;
            var box = new TextBox { Header = T("Environment name"), Text = ".venv", MaxLength = 100 };
            var selected = available[0].Id;
            var body = new StackPanel { Spacing = 12, MinWidth = 400 };
            body.Children.Add(palette.Label(parent, 12, muted: true)); body.Children.Add(box);
            body.Children.Add(Choice(available.Select(r => (r.Id, RuntimeTitle(r) + " · " + r.Architecture)).ToArray(), selected, value => selected = value, "Base interpreter"));
            var dialog = Dialog("Create environment", "", "Create"); dialog.Content = body;
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            runtime = available.Single(r => r.Id == selected); name = box.Text;
        }
        catch (Exception ex) { ShowError(ex); return; }
        finally { confirmationOpen = false; }
        if (runtime is null || parent is null || name is null) return;
        var operation = BeginOperation(T("Creating environment"), download: true);
        SetBusy(true, "Creating environment");
        try
        {
            using var lease = client.AcquireConfigurationLock();
            var environment = await Environments.CreateAsync(runtime, parent, name, new ProcessRunner(), operation);
            Notify(environment.State, environment.State == "Environment ready" ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) { Notify("Creation stopped; partial files were kept", InfoBarSeverity.Warning); }
        catch (Exception ex) { ShowError(ex); }
        finally { FinishOperation(operation); SetBusy(false); }
    }
    private async Task<bool> CheckEnvironmentAsync(VirtualEnvironment environment)
    {
        if (busy || confirmationOpen) return false; confirmationOpen = true;
        try
        {
            if (await Dialog("Check environment", T("This runs the interpreter in the selected environment") + "\n\n" + environment.Executable, "Check").ShowAsync() != ContentDialogResult.Primary) return false;
            SetBusy(true, "Checking files");
            var result = await VirtualEnvironments.CheckAsync(environment.Path, new ProcessRunner()); Environments.Remember(result);
            Notify(result.State, result.State == "Environment ready" ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            return result.State == "Environment ready";
        }
        catch (Exception ex) { ShowError(ex); return false; }
        finally { confirmationOpen = false; SetBusy(false); }
    }
    private async Task RemoveEnvironmentAsync(VirtualEnvironment environment)
    {
        if (busy || confirmationOpen) return; confirmationOpen = true;
        try { if (await Dialog("Remove from list", T("Files on disk will be kept") + "\n\n" + environment.Path, "Remove").ShowAsync() == ContentDialogResult.Primary) Environments.Remember(environment, remove: true); }
        catch (Exception ex) { ShowError(ex); }
        finally { confirmationOpen = false; RenderPage(); }
    }
    private async Task OpenEnvironmentTerminalAsync(VirtualEnvironment environment)
    {
        if (!await CheckEnvironmentAsync(environment)) return;
        try
        {
            var current = VirtualEnvironments.Inspect(environment.Path);
            if (current.State != "Not checked") throw new IOException(current.State);
            // Activate by setting the environment directly. Do not execute a project's Activate.ps1.
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
                { UseShellExecute = false, WorkingDirectory = current.Path };
            start.Environment["VIRTUAL_ENV"] = current.Path;
            start.Environment["PATH"] = Path.Combine(current.Path, "Scripts") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            start.Environment.Remove("PYTHONHOME");
            start.ArgumentList.Add("-NoExit"); start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-Command"); start.ArgumentList.Add("function global:prompt { '(' + [IO.Path]::GetFileName($env:VIRTUAL_ENV) + ') PS ' + $pwd + '> ' }; Write-Host $env:VIRTUAL_ENV");
            Process.Start(start);
        }
        catch (Exception ex) { ShowError(ex); }
    }
}
