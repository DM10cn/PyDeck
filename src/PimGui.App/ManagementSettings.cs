using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;
using System.Text.Json.Nodes;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private string? ProxyPassword() => preferences.Network.Mode == "Custom"
        ? ProxyCredential.Load(preferences.Network.Address, preferences.Network.Username) : null;
    private PimClient CreateClient() => new(new ProcessRunner(() => preferences.Network, ProxyPassword))
        { Network = () => preferences.Network, ProxyPassword = ProxyPassword,
          SelectedSource = () => preferences.InstallationIndex, ConfirmDownloadOrigin = ConfirmDownloadOriginAsync };

    private readonly HashSet<string> expandedSettings = [];
    private PathReport? lastPathReport;
    private UIElement ManagementSettings()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(palette.Label("Python management", 19, true));
        panel.Children.Add(palette.Label(T("PIM version: {0}", client.ManagerVersion), 12, muted: true));
        foreach (var (title, build) in new (string, Func<UIElement>)[] {
            ("PIM configuration", BuildPimEditor), ("PATH and aliases", BuildPathEditor), ("Refresh aliases", BuildAliasEditor),
            ("Network", BuildNetworkEditor), ("Installation source", BuildSourceEditor), ("Shebang rules", BuildShebangEditor) })
        {
            var section = new Expander { Header = T(title), Tag = "Management:" + title, IsExpanded = expandedSettings.Contains(title),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                CornerRadius = new(palette.Radius), IsEnabled = !busy && (connected || title is "Network" or "Installation source") && (title != "Shebang rules" || client.SupportsMutations) };
            palette.ApplySurfaceResources(section);
            void Populate() { try { section.Content = build(); } catch (Exception ex) { section.Content = palette.Label(T(ex.Message), 13); } }
            if (section.IsExpanded) Populate();
            section.Expanding += (_, _) => { expandedSettings.Add(title); if (section.Content is null) Populate(); };
            section.Collapsed += (_, _) => expandedSettings.Remove(title);
            panel.Children.Add(section);
        }
        return palette.CardBox(panel);
    }
    private void InlineAction(StackPanel body, string label, Func<Task> action, bool enabled = true)
    {
        var button = palette.Action(label, compact: true); button.IsEnabled = enabled && !busy;
        button.HorizontalAlignment = HorizontalAlignment.Left;
        button.Click += async (_, _) => await SaveInlineAsync(action); body.Children.Add(button);
    }
    private async Task SaveInlineAsync(Func<Task> action)
    {
        if (busy || confirmationOpen) return; confirmationOpen = true;
        SetBusy(true);
        try { await action(); }
        catch (Exception ex) { ShowError(new IOException(SensitiveText.Redact(ex.Message))); }
        finally { confirmationOpen = false; SetBusy(false); }
    }
    private UIElement BuildNetworkEditor()
    {
            var previous = preferences;
            var mode = preferences.Network.Mode;
            var body = new StackPanel { Spacing = 12 };
            var address = new TextBox { Header = T("Proxy address"), Text = preferences.Network.Address, PlaceholderText = "http://localhost:7890" };
            var username = new TextBox { Header = T("Username"), Text = preferences.Network.Username };
            var password = new PasswordBox { Header = T("Password"), PlaceholderText = T("Leave blank to keep the saved password") };
            var clear = new CheckBox { Content = T("Clear saved password") };
            void UpdateFields() { address.IsEnabled = username.IsEnabled = password.IsEnabled = mode == "Custom"; }
            body.Children.Add(Choice([("System", "Use system settings"), ("Direct", "Direct connection"), ("Custom", "HTTP proxy")], mode, value => { mode = value; UpdateFields(); }, "Network"));
            UpdateFields();
            body.Children.Add(address); body.Children.Add(username); body.Children.Add(password); body.Children.Add(clear);
            body.Children.Add(palette.Label("Passwords are stored in Windows Credential Manager", 12, muted: true));
        InlineAction(body, "Save", async () => {
            var settings = new NetworkSettings(mode, address.Text.Trim(), username.Text.Trim()).Validate();
            var secret = clear.IsChecked == true ? "" : password.Password.Length > 0 ? password.Password : ProxyCredential.Load(settings.Address, settings.Username) ?? "";
            var next = preferences with { Network = settings };
            store.Save(next);
            try { ProxyCredential.Save(settings.Address, settings.Username, secret); }
            catch { store.Save(previous); throw; }
            preferences = next;
            catalog = null;
            Notify("Network settings saved", InfoBarSeverity.Success);            await Task.CompletedTask;
        });
        return body;
    }
    private UIElement BuildPimEditor()
    {
            var snapshot = PimConfiguration.Read();
            var body = new StackPanel { Spacing = 12 };
            body.Children.Add(palette.Label(snapshot.Path, 12, muted: true));
            var edits = new JsonObject();
            var defaults = new List<(string, string)> { ("", "PIM default") };
            defaults.AddRange(installed.Select(r => (r.Selector, RuntimeTitle(r) + " · " + r.Architecture)));
            var savedTag = snapshot.Values["default_tag"]?.GetValue<string>() ?? "";
            if (savedTag.Length > 0 && !defaults.Any(p => p.Item1 == savedTag)) defaults.Add((savedTag, savedTag));
            body.Children.Add(SettingRow("Default interpreter", null, Choice(defaults.ToArray(), savedTag, value => edits["default_tag"] = value.Length > 0 ? JsonValue.Create(value) : null, "Default interpreter")));
            body.Children.Add(SettingRow("Default platform", null, Choice([("", "PIM default"), ("-64", "x64"), ("-32", "x86"), ("-arm64", "ARM64")], snapshot.Values["default_platform"]?.GetValue<string>() ?? "",
                value => edits["default_platform"] = value.Length > 0 ? JsonValue.Create(value) : null, "Default platform")));
            foreach (var (key, label) in new[] { ("automatic_install", "Automatic installation"), ("include_unmanaged", "Include unmanaged Python") })
                body.Children.Add(SettingRow(label, null, Choice([("", "PIM default"), ("true", "On"), ("false", "Off")], snapshot.Values[key]?.ToJsonString() ?? "",
                    value => edits[key] = value.Length > 0 ? JsonValue.Create(value == "true") : null, label)));
            body.Children.Add(palette.Label("These settings also affect PIM outside PyDeck", 12, muted: true));
            if (snapshot.Overrides.Count > 0) body.Children.Add(palette.Label(T("Configuration overrides: {0}", string.Join(", ", snapshot.Overrides)), 12, muted: true));
            var backups = Directory.Exists(Path.GetDirectoryName(snapshot.Path)) ? Directory.GetFiles(Path.GetDirectoryName(snapshot.Path)!, Path.GetFileName(snapshot.Path) + ".pydeck-*.bak").OrderDescending().Take(20).ToArray() : [];
            string? backup = null;
            if (backups.Length > 0)
            {
                body.Children.Add(palette.Label("Restore backup", 14, true));
                body.Children.Add(Choice(new[] { ("", "Choose a backup") }.Concat(backups.Select(p => (p, Path.GetFileName(p)))).ToArray(), "", value => backup = value.Length > 0 ? value : null, "Restore backup"));
            }
        InlineAction(body, "Review changes", async () => {
            if (edits.Count == 0 && backup is null) return;
            string SettingName(string key) => T(key switch { "default_tag" => "Default interpreter", "default_platform" => "Default platform", "automatic_install" => "Automatic installation", _ => "Include unmanaged Python" });
            string DisplayValue(JsonNode? value) => value is null ? T("PIM default") : value.ToJsonString() switch
                { "true" => T("On"), "false" => T("Off"), "\"-64\"" => "x64", "\"-32\"" => "x86", "\"-arm64\"" => "ARM64", _ => value.ToString() };
            var review = backup is null ? string.Join("\n", edits.Select(item => SettingName(item.Key) + ": " + DisplayValue(snapshot.Values[item.Key]) + " → " + DisplayValue(item.Value)))
                : T("Restore configuration from {0}", Path.GetFileName(backup));
            if (await Dialog("Review changes", review, "Save").ShowAsync() != ContentDialogResult.Primary) return;
            using (client.AcquireConfigurationLock())
            {
                if (backup is null) PimConfiguration.Save(snapshot, edits); else PimConfiguration.Restore(snapshot, backup);
            }
            installed = await client.ListAsync(); catalog = null;
            if (edits["default_tag"] is { } requested && installed.FirstOrDefault(r => r.IsDefault)?.Selector != requested.GetValue<string>())
                Notify("Your preference was saved, but PIM reports a different effective default. A custom configuration or policy may override it.", InfoBarSeverity.Warning);
            else Notify("Configuration saved and Python list refreshed", InfoBarSeverity.Success);        }, snapshot.Overrides.Count == 0);
        return body;
    }
    private UIElement BuildPathEditor()
    {
        var body = new StackPanel { Spacing = 12 };
        InlineAction(body, "Run diagnostics", async () => {
            installed = await client.ListAsync();
            lastPathReport = await PathDiagnostics.ProbeKnownAsync(PathDiagnostics.Inspect(installed, client.Executable), installed, client.Executable);
        });
        if (lastPathReport is not { } report) return body;

            body.Children.Add(palette.Label(T("PyDeck default: {0}", report.DefaultVersion ?? T("Not found")), 16, true));
            foreach (var entry in report.Commands)
            {
                var indicator = entry.Command == "pymanager" && entry.Version is not null ? "" : entry.MatchesDefault ? "  ✓" : "  ⚠";
                body.Children.Add(palette.Label(entry.Command + "  ·  " + (entry.Version ?? T("Not verified")) + indicator, 14, true));
                var path = palette.Label(entry.ActualPath ?? entry.Path ?? T("Not found"), 12, muted: true); path.IsTextSelectionEnabled = true; body.Children.Add(path);
                foreach (var shadowed in entry.Candidates.Skip(1)) body.Children.Add(palette.Label(T("Shadowed: {0}", shadowed), 12, muted: true));
            }
            if (report.PathChanged) body.Children.Add(palette.Label("PATH changed since PyDeck started. Restart PyDeck and your terminal to compare again", 12));
            if (report.WindowsAppsMissing) body.Children.Add(palette.Label("WindowsApps is missing from PATH", 12));
            if (report.GlobalAliasesMissing) body.Children.Add(palette.Label("The default PIM alias folder is missing from PATH; a custom configuration may use another folder", 12));
            body.Children.Add(palette.Label("Unknown commands are not executed. Shell functions and virtual environments may resolve differently", 12, muted: true));
            var aliases = palette.Action("Windows app execution aliases", "\uE713");
            aliases.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:appsfeatures-app") { UseShellExecute = true }); } catch (Exception ex) { ShowError(ex); } }; body.Children.Add(aliases);
        return body;
    }
    private UIElement BuildAliasEditor()
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(palette.Label("Ask PIM to rebuild registrations and global aliases for all managed versions", 13));
        var button = palette.Action("Refresh aliases", compact: true); button.IsEnabled = client.SupportsMutations && !busy;
        button.Click += async (_, _) => await RefreshAliasesAsync(); body.Children.Add(button);
        return body;
    }
    private async Task RefreshAliasesAsync()
    {
        if (busy || !connected || confirmationOpen) return;
        confirmationOpen = true;
        try
        {
            if (await Dialog("Refresh aliases", "PIM will rebuild registrations and global aliases for all managed versions", "Refresh").ShowAsync() != ContentDialogResult.Primary) return;
            SetBusy(true, "Checking files");
            await client.RefreshRegistrationsAsync(line => DispatcherQueue.TryEnqueue(() => Log(line)));
            installed = await client.ListAsync();
            Notify("Registrations refreshed. Run PATH diagnostics to check command resolution", InfoBarSeverity.Informational);
        }
        catch (Exception ex) { try { installed = await client.ListAsync(); } catch { installed = []; } ShowError(ex); }
        finally { confirmationOpen = false; SetBusy(false); RenderPage(); }
    }
    private async Task CheckRuntimeAsync(PythonRuntime runtime)
    {
        if (busy || confirmationOpen) return;
        SetBusy(true, "Checking files");
        try
        {
            installed = await client.ListAsync();
            var current = installed.SingleOrDefault(r => r.Id == runtime.Id && r.Prefix == runtime.Prefix);
            if (current is null) throw new IOException("This Python entry has changed. Refresh the list before trying again.");
            var health = await RuntimeHealth.CheckAsync(current);
            Notify(health.Message, health.Healthy ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); RenderPage(); }
    }
    private async Task VerifyOperationAsync(RuntimeAction action, PythonRuntime runtime)
    {
        try
        {
            var result = await RuntimeHealth.VerifyAsync(client, action, runtime);
            installed = result.Runtimes;
            Notify(result.Message, result.Confirmed ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            StatusText.Text = T(result.Confirmed ? "Operation verified" : "Result not confirmed");
        }
        catch (Exception ex)
        { installed = []; Log(SensitiveText.Redact(ex.Message)); Notify("Result not confirmed. Refresh before trying again", InfoBarSeverity.Warning); StatusText.Text = T("Result not confirmed"); }
    }
}
