using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckCancelledInlineEditsAsync(string directory)
    {
        var originalConfig = PimConfiguration.Read().Original;
        var originalLanguage = preferences.Language;
        try
        {
            SavePreferences(preferences with { Language = "en-US" });
            foreach (var kind in new[] { "PIM configuration", "Shebang rules" })
            {
                Navigate("settings");
                var editor = (FrameworkElement)(kind == "PIM configuration" ? BuildPimEditor() : BuildShebangEditor());
                PageHost.Children.Clear(); PageHost.Children.Add(editor); Root.UpdateLayout();
                ComboBox? platform = null;
                ComboBoxItem? selectedPlatform = null;
                string? rule = null;
                if (kind == "PIM configuration")
                {
                    platform = Descendants(editor).OfType<ComboBox>().Single(box =>
                        Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(box) == T("Default platform"));
                    selectedPlatform = platform.Items.Cast<ComboBoxItem>().First(item => !ReferenceEquals(item, platform.SelectedItem));
                    platform.SelectedItem = selectedPlatform;
                }
                else
                {
                    rule = "/usr/bin/pydeck_cancel_" + Guid.NewGuid().ToString("N");
                    var template = Descendants(editor).OfType<TextBox>().Single(box =>
                        Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(box) == T("Shebang template"));
                    template.Text = rule;
                    var add = Descendants(editor).OfType<Button>().Single(button =>
                        Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == T("Add or replace rule"));
                    await WaitForSmokeConditionAsync(() => add.IsLoaded && add.IsEnabled, "Draft rule add action did not load");
                    InvokeButton(add);
                    await WaitForSmokeConditionAsync(() => Descendants(editor).OfType<TextBlock>().Any(label => label.Text == rule), "Draft rule was not added");
                }
                var review = Descendants(editor).OfType<Button>().Single(button =>
                    Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == T("Review changes"));
                // Re-open and cancel again: retaining labels alone is insufficient if the edit closure was lost.
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    await WaitForSmokeConditionAsync(() => review.IsLoaded && review.IsEnabled, "Draft review action did not load");
                    InvokeButton(review);
                    var dialog = await WaitForCancellationDialogAsync("Review changes");
                    if (rule is not null && (dialog.Content is not TextBlock text || !text.Text.Contains(rule)))
                        throw new IOException("Review dialog lost the unsaved rule");
                    InvokeButton(Descendants(dialog).OfType<Button>().Single(button => button.Content as string == T("Cancel")));
                    await WaitForSmokeConditionAsync(() => !busy && !confirmationOpen && editor.IsLoaded, "Cancelled review did not restore the original editor");
                    if (!ReferenceEquals(PageHost.Children.Single(), editor) ||
                        platform is not null && !ReferenceEquals(platform.SelectedItem, selectedPlatform) ||
                        rule is not null && !Descendants(editor).OfType<TextBlock>().Any(label => label.Text == rule))
                        throw new IOException("Cancelling review discarded unsaved changes: " + kind);
                }
                if (kind == "Shebang rules") await CaptureAsync(Path.Combine(directory, "24-cancelled-rule-draft.png"));
            }
            Navigate("settings");
            var sourceEditor = BuildSourceEditor();
            PageHost.Children.Clear(); PageHost.Children.Add(sourceEditor); Root.UpdateLayout();
            var sourceInput = Descendants(sourceEditor).OfType<TextBox>().Single();
            sourceInput.Text = "http://invalid.example.test/index.json";
            var sourceSave = Descendants(sourceEditor).OfType<Button>().Single(button =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(button) == T("Save"));
            MessageBar.IsOpen = false;
            await WaitForSmokeConditionAsync(() => sourceSave.IsLoaded && sourceSave.IsEnabled, "Source save action did not load");
            InvokeButton(sourceSave);
            await WaitForSmokeConditionAsync(() => !busy && !confirmationOpen && MessageBar.IsOpen, "Invalid source did not return validation feedback");
            if (!ReferenceEquals(PageHost.Children.Single(), sourceEditor) || sourceInput.Text != "http://invalid.example.test/index.json")
                throw new IOException("Validation failure discarded the source draft");
            if (PimConfiguration.Read().Original != originalConfig) throw new IOException("Cancelled inline review changed the user's PIM configuration");
        }
        finally
        {
            MessageBar.IsOpen = false;
            SavePreferences(preferences with { Language = originalLanguage }); Navigate("settings");
        }
    }

    private async Task CheckNotificationTextAsync(string directory)
    {
        foreach (var language in Strings.Languages)
        {
            SavePreferences(preferences with { Language = language });
            foreach (var severity in new[] { InfoBarSeverity.Success, InfoBarSeverity.Warning, InfoBarSeverity.Error })
            {
                Notify("Configuration saved and Python list refreshed", severity);
                Root.UpdateLayout();
                var labels = Descendants(MessageBar).OfType<TextBlock>().Where(t => t.ActualWidth > 0 && t.ActualHeight > 0).ToArray();
                if (!labels.Any(t => t.Text == MessageBar.Title) || !labels.Any(t => t.Text == MessageBar.Message))
                    throw new IOException("Notification text not rendered: " + language + " / " + severity);
            }
            await CaptureAsync(Path.Combine(directory, "20-notification-" + language + ".png"), MessageBar);
        }
        MessageBar.IsOpen = false;
        SavePreferences(preferences with { Language = "en-US" });
    }

    private async Task CheckRuleEntryAndActivityAsync(string directory)
    {
        static string NormalizeLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var language in Strings.Languages)
        {
            SavePreferences(preferences with { Language = language }); Navigate("settings");
            // Use the real editor and its click handlers without reviewing/saving any config.
            var editor = BuildShebangEditor();
            PageHost.Children.Clear(); PageHost.Children.Add(editor); Root.UpdateLayout();
            var template = Descendants(editor).OfType<TextBox>().Single(box =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(box) == T("Shebang template"));
            var add = Descendants(editor).OfType<Button>().Single(b =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(b) == T("Add or replace rule"));
            var feedback = Descendants(editor).OfType<TextBlock>().Single(t => t.Tag as string == "ShebangFeedback");
            if (add.IsEnabled || template.Text.Length != 0) throw new IOException("Empty rule can be submitted");
            template.Text = "   ";
            await WaitForSmokeConditionAsync(() => !add.IsEnabled, "Whitespace rule stayed enabled");
            if (add.IsEnabled) throw new IOException("Whitespace rule can be submitted");
            MessageBar.IsOpen = false;
            template.Text = "#!invalid";
            await WaitForSmokeConditionAsync(() => add.IsLoaded && add.IsEnabled, "Rule entry did not enable");
            InvokeButton(add);
            await WaitForSmokeConditionAsync(() => feedback.Text == T("Enter a Shebang template without #!"), "Inline rule validation missing");
            if (MessageBar.IsOpen) throw new IOException("Invalid rule raised a global error");
            var target = Descendants(editor).OfType<ComboBox>().Single(box =>
                Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(box) == T("Python version"));
            var index = 0;
            foreach (var item in target.Items.Cast<ComboBoxItem>())
            {
                target.SelectedItem = item;
                var key = "/usr/bin/pydeck_smoke_" + index++;
                template.Text = key;
                await WaitForSmokeConditionAsync(() => add.IsEnabled && feedback.Text == T("Enter a template, such as /usr/bin/my_python, without #!"), "Rule entry did not refresh");
                InvokeButton(add);
                await WaitForSmokeConditionAsync(() => feedback.Text == T("Rule added to the list. Review changes to save"), "Valid rule was rejected");
                if (!Descendants(editor).OfType<TextBlock>().Any(t => t.Text == key) ||
                    !Descendants(editor).OfType<TextBlock>().Any(t => t.Text == (string)item.Tag))
                    throw new IOException("Rule target did not render");
            }
            await CaptureAsync(Path.Combine(directory, "21-rule-entry-" + language + ".png"));
            activityFilter = null; Navigate("activity"); Root.UpdateLayout();
            Log("feedback-info", ActivityLevel.Information);
            Notify("feedback-warning", InfoBarSeverity.Warning);
            ShowError(new IOException("feedback-error"));
            var choice = Descendants(PageHost).OfType<ComboBox>().Single();
            foreach (var level in Enum.GetValues<ActivityLevel>())
            {
                choice.SelectedItem = choice.Items.Cast<ComboBoxItem>().Single(i => (string)i.Tag == level.ToString());
                await WaitForSmokeConditionAsync(() => activityFilter == level, "Activity selection did not update");
                var expected = level switch { ActivityLevel.Error => "feedback-error", ActivityLevel.Warning => "feedback-warning", _ => "feedback-info" };
                if (NormalizeLines(ActivityVisibleText) != NormalizeLines(activityLog.Text(level)) || !ActivityVisibleText.Contains(expected) ||
                    activityRows.Any(row => row.Entry.Level != level))
                    throw new IOException("Activity filter mismatch");
            }
            var errorCount = activityLog.Entries.Count(e => e.Message == "feedback-error");
            if (errorCount != Array.IndexOf(Strings.Languages, language) + 1) throw new IOException("Errors logged twice");
            Log("feedback-live-error", ActivityLevel.Error);
            if (!ActivityVisibleText.Contains("feedback-live-error") || ActivityVisibleText.Contains("feedback-warning")) throw new IOException("Live filtering failed");
            Navigate("runtimes"); Navigate("activity"); Root.UpdateLayout();
            await WaitForSmokeConditionAsync(() => activityFilter == ActivityLevel.Error &&
                NormalizeLines(ActivityVisibleText) == NormalizeLines(activityLog.Text(ActivityLevel.Error)),
                $"Filter lost on navigation: {activityFilter}; UI characters {ActivityVisibleText.Length}; expected {activityLog.Text(ActivityLevel.Error).Length}");
            if (activityList is null || activityList.Tag as string != "ActivityViewer" ||
                Descendants(PageHost).OfType<TextBox>().Any(box => box.IsReadOnly))
                throw new IOException("Activity must render a list viewer instead of a read-only text box");
            var live = activityRows.Last(row => row.Message == "feedback-live-error");
            activityList.ScrollIntoView(live);
            await WaitForSmokeConditionAsync(() => Descendants(activityList).OfType<TextBlock>().Any(label =>
                label.Text == live.Message && label.ActualWidth > 0 && label.ActualHeight > 0), "Activity row binding did not render its message");
            if (!Descendants(activityList).OfType<TextBlock>().Any(label => label.Text == "ERROR") ||
                !Descendants(activityList).OfType<TextBlock>().Any(label => label.Text == live.Time))
                throw new IOException("Activity row did not separate time, level and message");
            await CaptureAsync(Path.Combine(directory, "22-activity-errors-" + language + ".png"));
        }
        activityFilter = null; MessageBar.IsOpen = false;
        SavePreferences(preferences with { Language = "en-US" });
    }
}
