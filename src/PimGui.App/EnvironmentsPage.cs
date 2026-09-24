using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;
using System.Diagnostics;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private VirtualEnvironments Environments => new(store.DirectoryPath);
    private UIElement BuildEnvironmentsPage()
    {
        var layout = PageGrid(GridLength.Auto, new(1, GridUnitType.Star));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var (title, action) in new (string, Func<Task>)[] {
            ("Create environment", CreateEnvironmentAsync), ("Import environment", ImportEnvironmentAsync), ("Refresh", RefreshEnvironmentsAsync) })
        {
            var button = palette.Action(title, compact: true); button.IsEnabled = !busy;
            button.Click += async (_, _) => await action(); actions.Children.Add(button);
        }
        At(layout, Header("YOUR PROJECTS", "Virtual environments", "Separate Python environments for your projects", actions), 0);
        var rows = new StackPanel { Spacing = 12 };
        try
        {
            var environments = Environments.Read();
            if (environments.Count == 0) rows.Children.Add(Empty("\uE8B7", "No environments yet", "Create an environment or import an existing folder"));
            foreach (var environment in environments)
            {
                var body = new StackPanel { Spacing = 8 };
                body.Children.Add(palette.Label(environment.Name + "  ·  " + environment.Version, 17, true));
                var path = palette.Label(environment.Path, 12, muted: true); path.IsTextSelectionEnabled = true; body.Children.Add(path);
                body.Children.Add(palette.Label(T("Base interpreter: {0}", environment.BasePath), 12, muted: true));
                body.Children.Add(palette.Label(environment.State, 12));
                var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                foreach (var (title, action) in new (string, Func<Task>)[] {
                    ("Check environment", () => CheckEnvironmentAsync(environment)),
                    ("Terminal", () => OpenEnvironmentTerminalAsync(environment)),
                    ("Open folder", () => { OpenFolder(new("", "", "", "", "", "", environment.Path, false)); return Task.CompletedTask; }),
                    ("Remove from list", () => RemoveEnvironmentAsync(environment)) })
                {
                    var button = palette.Action(title, compact: true); button.IsEnabled = !busy;
                    button.Click += async (_, _) => await action(); buttons.Children.Add(button);
                }
                body.Children.Add(buttons); rows.Children.Add(palette.CardBox(body));
            }
        }
        catch (Exception ex) { rows.Children.Add(palette.Label(T("Environment list unavailable") + "\n" + ex.Message, 14)); }
        At(layout, new ScrollViewer { Content = rows }, 1); return layout;
    }
    private async Task RefreshEnvironmentsAsync()
    {
        if (busy) return;
        try { foreach (var e in Environments.Read()) Environments.Remember(VirtualEnvironments.Inspect(e.Path)); }
        catch (Exception ex) { ShowError(ex); }
        await Task.CompletedTask; RenderPage();
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
