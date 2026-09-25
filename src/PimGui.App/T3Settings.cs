using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;
using System.Text.Json.Nodes;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckAppUpdateAsync()
    {
        if (busy || confirmationOpen) return;
        confirmationOpen = true; SetBusy(true, "Checking PyDeck updates");
        try
        {
            using var http = preferences.Network.CreateClient(ProxyPassword());
            var release = await AppUpdates.CheckAsync(http, typeof(MainWindow).Assembly.GetName().Version!);
            if (release is null) { Notify("PyDeck is up to date", InfoBarSeverity.Success); return; }
            if (await Dialog("PyDeck update available", T("Version {0} is available on GitHub", release.Version), "Open release page").ShowAsync() == ContentDialogResult.Primary)
                OpenUrl(release.Page.AbsoluteUri);
        }
        catch (Exception) { Notify("Could not check for updates. Try again later", InfoBarSeverity.Warning); }
        finally { confirmationOpen = false; SetBusy(false); }
    }
    private async Task<bool> ConfirmDownloadOriginAsync(Uri uri)
    {
        if (confirmationOpen || cancelDialogOpen) return false; confirmationOpen = true;
        try { return await Dialog("Review download source", T("This request uses another host. Continue only if you trust it") + "\n\n" + uri.GetLeftPart(UriPartial.Authority), "Continue").ShowAsync() == ContentDialogResult.Primary; }
        finally { confirmationOpen = false; }
    }
    private UIElement BuildSourceEditor()
    {
            var input = new TextBox { Header = T("HTTPS PIM index"), Text = preferences.InstallationIndex, PlaceholderText = InstallationSource.Official, MaxLength = 2048,
                FontSize = palette.Tokens.ControlFontSize, MinHeight = palette.Tokens.ControlHeight };
            var body = new StackPanel { Spacing = 12 };
            body.Children.Add(palette.Label("This source applies only to PyDeck, not pip or external terminals", palette.Tokens.CaptionFontSize, muted: true));
            body.Children.Add(input);
            var reset = palette.Action("Restore official source", compact: true); reset.Click += (_, _) => input.Text = "";
            var actions = Toolbar(reset); body.Children.Add(actions);
        InlineAction(actions, "Save", async result => {
            var index = InstallationSource.Validate(input.Text.Trim());
            if (index != InstallationSource.Official && await Dialog("Trust this source?", index + "\n\n" + T("A checksum verifies files, not the publisher"), "Trust source").ShowAsync() != ContentDialogResult.Primary) return;
            var updated = preferences with { InstallationIndex = index == InstallationSource.Official ? "" : index };
            store.Save(updated); preferences = updated; catalog = null;
            result.Applied = true;
            Notify("Source saved. Reload the catalog to continue", InfoBarSeverity.Informational);        });
        return body;
    }
    private UIElement BuildShebangEditor()
    {
            var snapshot = PimConfiguration.Read();
            var rules = snapshot.Values["shebang_templates"] is JsonObject original ? (JsonObject)original.DeepClone() : new JsonObject();
            if (snapshot.Values["shebang_templates"] is not null and not JsonObject) throw new IOException("The existing Shebang configuration cannot be edited safely");
            var edits = new JsonObject();
            var body = new StackPanel { Spacing = 12 };
            var existingMode = snapshot.Values["shebang_can_run_anything"];
            body.Children.Add(SettingRow("Run non-Python programs", null, Choice([("", "PIM default"), ("true", "Allow"), ("false", "Block")],
                existingMode?.ToJsonString() ?? "", value => edits["shebang_can_run_anything"] = value == "" ? null : JsonValue.Create(value == "true"), "Run non-Python programs")));
            body.Children.Add(palette.Label("Rules select Python versions without changing your scripts", 12, muted: true));
            var list = new StackPanel { Spacing = 8 }; body.Children.Add(list);
            var template = new TextBox { Header = T("Shebang template"), PlaceholderText = "/usr/bin/my_python", MaxLength = 512,
                FontSize = palette.Tokens.ControlFontSize, MinHeight = palette.Tokens.ControlHeight };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(template, T("Shebang template"));
            var feedback = palette.Label("Enter a template, such as /usr/bin/my_python, without #!", 12, muted: true);
            feedback.Tag = "ShebangFeedback";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(feedback, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
            var targets = new List<(string, string)> { ("py", "Default interpreter"), ("pyw", "Default interpreter (windowed)") };
            foreach (var runtime in installed) { targets.Add(("py -V:" + runtime.Selector, runtime.Selector)); targets.Add(("pyw -V:" + runtime.Selector, runtime.Selector + " · " + T("Windowed"))); }
            string selected = "py"; body.Children.Add(template); body.Children.Add(feedback);
            body.Children.Add(SettingRow("Python version", null, Choice(targets.ToArray(), selected, value => selected = value, "Python version")));
            bool changed = false;
            void RenderRules()
            {
                list.Children.Clear();
                foreach (var pair in rules.ToArray())
                {
                    var remove = palette.Action("Remove", compact: true);
                    remove.Click += (_, _) => { rules.Remove(pair.Key); changed = true; RenderRules(); };
                    list.Children.Add(SettingRow(pair.Key, pair.Value is JsonValue value && value.TryGetValue<string>(out var command) ? command : pair.Value?.ToJsonString(), remove));
                }
            }
            RenderRules();
            var add = palette.Action("Add or replace rule", compact: true);
            add.IsEnabled = !string.IsNullOrWhiteSpace(template.Text);
            template.TextChanged += (_, _) =>
            {
                add.IsEnabled = !string.IsNullOrWhiteSpace(template.Text);
                feedback.Text = T("Enter a template, such as /usr/bin/my_python, without #!");
            };
            add.Click += (_, _) =>
            {
                try
                {
                    var key = template.Text.Trim();
                    ShebangRules.ValidateMapping(key, selected);
                    if (!rules.ContainsKey(key) && rules.Count >= 200) throw new ArgumentException("Up to 200 rules are supported");
                    rules[key] = selected; changed = true; RenderRules();
                    feedback.Text = T("Rule added to the list. Review changes to save");
                }
                catch (ArgumentException ex) { feedback.Text = T(ex.Message); template.Focus(FocusState.Programmatic); }
                catch (Exception ex) { ShowError(ex); }
            };
            add.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(add);
            if (snapshot.Overrides.Count > 0) body.Children.Add(palette.Label(string.Join("\n", snapshot.Overrides), 12));
        InlineAction(body, "Review changes", async result => {
            if (changed) edits["shebang_templates"] = rules;
            if (edits.Count == 0) return;
            string DisplayMode(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var allowed) ? T(allowed ? "Allow" : "Block") : T("PIM default");
            string DisplayRules(JsonNode? node) => node is JsonObject values && values.Count > 0
                ? string.Join("\n", values.Select(p => p.Key + " → " + (p.Value is JsonValue v && v.TryGetValue<string>(out var command) ? command : p.Value?.ToJsonString()))) : T("PIM default");
            var review = string.Join("\n\n", edits.Select(pair => pair.Key == "shebang_can_run_anything"
                ? T("Run non-Python programs") + ": " + DisplayMode(snapshot.Values[pair.Key]) + " → " + DisplayMode(pair.Value)
                : T("Shebang rules") + "\n" + DisplayRules(pair.Value)));
            if (await Dialog("Review changes", review, "Save").ShowAsync() != ContentDialogResult.Primary) return;
            using (client.AcquireConfigurationLock()) PimConfiguration.Save(snapshot, edits);
            result.Applied = true;
            Notify("Shebang rules saved", InfoBarSeverity.Success);        }, snapshot.Overrides.Count == 0);
        return body;
    }
}
