using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;
using System.Text.Json;

namespace PimGui.App;

public sealed partial class MainWindow
{
    // Tests explicitly replace the presentation to cover both trees in an isolated host.
    // Production preference saves never call this path.
    private void ApplySmokePreferences(AppSettings changed)
    {
        if (smokeDirectory is null || !Path.GetFullPath(store.DirectoryPath).StartsWith(Path.GetFullPath(smokeDirectory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Presentation fixtures require an isolated profile");
        var design = changed.Normalize().Design;
        SavePreferences(changed);
        if (ActiveDesign != design) { InitializePresentation(design); ApplyAppearance(); }
    }
    private void ApplySmokeAppearance(string design, string theme) => ApplySmokePreferences(preferences with { Design = design, Theme = theme });

    private async Task RunDesignProbeAsync(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new List<string>();
        try
        {
            connected = true;
            await CheckSearchControlsAsync(directory);
            checks.Add("Search uses the actual native editor with correct Fluent/Material corner, fill, focus and text alignment; native clearing and debounced filtering retain the control.");
            await CheckSelectorsAsync(directory);
            checks.Add("Native selectors retain expansion, selection and scrolling with Material tonal popup rows, rounded corners and checkmarks.");
            await CheckExpandersAsync(directory);
            checks.Add("All settings expanders share native behavior and Material rounded header/content corners, including custom colors and build workspace management.");
            await CheckProgressColorsAsync(directory);
            checks.Add("Refresh and operation indicators use the current Material primary and tonal track colors in both progress modes; Fluent and contrast roles remain native.");
            await CheckDeferredDesignAsync();
            checks.Add("Style choice persists while active presentation, backdrop, task lease and settings controls remain intact; next startup selection reads the saved style.");
            await CheckInterfaceRestartAsync(directory);
            checks.Add("Restart prompt is localized and cancellable; pending style persists, failed saves do not prompt, and active or late-arriving work prevents restart without cancellation.");
            await CheckMaterialColorsAsync(directory);
            checks.Add("Windows image decoder to native Monet pipeline, transparent samples, metadata cache, manual color persistence, light/dark palettes and deferred wallpaper updates preserve form drafts.");
            await CheckDualColorUiAsync(directory);
            checks.Add("Dual-color mode saves both custom seeds, renders both families in light/dark themes and updates when only the second color changes.");
            await CheckDesignComponentsAsync(directory);
            checks.Add("Both component dictionaries, five action roles, compact sizes, native invocation, switches, radio selection, state templates and typography rendered in light/dark and four languages.");
            await CheckPageLayoutsAsync(directory);
            checks.Add("192 page combinations: six pages, four languages, two designs, light/dark and normal/compact windows; bounds, labels, settings rows, runtime identities and action geometry.");
            await CheckBuildUiAsync(directory);
            checks.Add("Build controls retain configuration, draft, scroll, cancellation and source behavior in both presentations.");
            await CheckInteractivePerformanceAsync(checks);
            await CheckBatchedUpdatesAsync();
            checks.Add("Search coalescing, build option updates, bounded logs and progress retain their performance checks.");
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(new { passed = false, checks, error = ex.ToString(), activeDesign = ActiveDesign, preferences }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { CancelSearchRefresh(); Close(); }
    }

    private async Task CheckDeferredDesignAsync()
    {
        var original = preferences;
        var networkWasExpanded = expandedSettings.Contains("Network");
        try
        {
            foreach (var design in new[] { "Material", "Fluent" })
            {
                ApplySmokePreferences(preferences with { Design = design, Theme = "Dark", Transparency = "On" });
                expandedSettings.Add("Network");
                Navigate("settings"); Root.UpdateLayout();
                var visual = PageHost.Children.Single(); var controller = SystemBackdrop;
                var draft = Descendants(settingsSections["Network"].Section).OfType<TextBox>().First();
                draft.Text = "http://unsaved-design-fixture.invalid:8123";
                var dictionaries = Application.Current.Resources.MergedDictionaries.ToArray();
                using (var work = StartWork(WorkKind.Build))
                {
                    if (work is null) throw new IOException("Could not create the isolated task fixture");
                    var pending = design == "Material" ? "Fluent" : "Material";
                    SavePreferences(preferences with { Design = pending });
                    Root.UpdateLayout();
                    if (ActiveDesign != design || !ReferenceEquals(visual, PageHost.Children.Single()) || !ReferenceEquals(controller, SystemBackdrop) ||
                        !dictionaries.SequenceEqual(Application.Current.Resources.MergedDictionaries) || !workCoordinator.Contains(WorkKind.Build) ||
                        store.Load().Design != pending || designRestartNotice?.IsOpen != true)
                        throw new IOException("Deferred style selection disturbed the active session");
                    if (draft.Text != "http://unsaved-design-fixture.invalid:8123" || !draft.IsLoaded)
                        throw new IOException("Style selection discarded an unsaved settings draft");
                    using (var locked = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        designChoices[design].IsChecked = true;
                        SavePreferences(preferences with { Design = design });
                        if (preferences.Design != pending || designChoices.Values.Count(choice => choice.IsChecked == true) != 1 || designChoices[pending].IsChecked != true)
                            throw new IOException("Failed preference save left an incorrect selected style");
                    }
                    MessageBar.IsOpen = false;
                    SavePreferences(preferences with { Theme = "Light" });
                    if (ActiveDesign != design || preferences.Design != pending || Root.RequestedTheme != ElementTheme.Light)
                        throw new IOException("Theme change applied the pending style early");
                    if (design == "Material" && (SystemBackdrop is not null || palette.Surface.A != 255))
                        throw new IOException("Pending Fluent enabled transparency in Material");
                }
                InitializePresentation(store.Load().Design); ApplyAppearance();
                if (ActiveDesign != preferences.Design || designRestartNotice?.IsOpen != false)
                    throw new IOException("Startup selection did not consume the persisted style");
            }
            await Task.Delay(50);
        }
        finally
        {
            if (!networkWasExpanded) expandedSettings.Remove("Network");
            ApplySmokePreferences(original);
        }
    }

    private async Task CheckDesignComponentsAsync(string directory)
    {
        var original = preferences;
        try
        {
            foreach (var design in new[] { "Fluent", "Material" })
            foreach (var theme in new[] { "Light", "Dark" })
            foreach (var language in Strings.Languages)
            {
                ApplySmokePreferences(preferences with { Design = design, Theme = theme, Language = language, Transparency = "Off" });
                Navigate("components"); Root.UpdateLayout(); await Task.Delay(60); Root.UpdateLayout();
                if (Application.Current.Resources.MergedDictionaries.Count(dictionary => dictionary.ContainsKey("PyDeckDesignDictionary")) != 1)
                    throw new IOException("Multiple design dictionaries are active");
                var actions = Descendants(PageHost).OfType<Button>().Where(button => button.Tag as string == "ActionButton").ToArray();
                if (actions.Count(button => !button.IsEnabled) != 5) throw new IOException("Gallery lacks disabled action variants");
                var primary = actions.Single(button => AutomationProperties.GetName(button) == T("Primary action"));
                InvokeButton(primary);
                await WaitForSmokeConditionAsync(() => Descendants(PageHost).OfType<TextBlock>().Any(label => label.Tag as string == "GalleryFeedback" && label.Text == T("Action invoked {0} times", 1)), "Native action invocation failed");
                foreach (var action in actions)
                {
                    action.ApplyTemplate();
                    if (action.IsEnabled && (!VisualStateManager.GoToState(action, "PointerOver", false) || !VisualStateManager.GoToState(action, "Pressed", false) || !VisualStateManager.GoToState(action, "Normal", false)))
                        throw new IOException("Action template lacks interaction states: " + design);
                }
                var toggle = Descendants(PageHost).OfType<ToggleSwitch>().Single(control => AutomationProperties.GetName(control) == T("Switch off"));
                var peer = new Microsoft.UI.Xaml.Automation.Peers.ToggleSwitchAutomationPeer(toggle);
                ((Microsoft.UI.Xaml.Automation.Provider.IToggleProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Toggle)).Toggle();
                if (!toggle.IsOn) throw new IOException("Switch lost native automation semantics");
                if (design == "Material" && (SystemBackdrop is not null || PopupBrush() is not SolidColorBrush || palette.Card.A != 255))
                    throw new IOException("Material gallery retained translucent surfaces");
                if (language is "en-US" or "zh-CN") await CaptureAsync(Path.Combine(directory, $"gallery-{design}-{theme}-{language}.png"));
                CheckPageGeometry($"gallery/{design}/{theme}/{language}");
                if (language == "zh-CN")
                {
                    var scroll = Descendants(PageHost).OfType<ScrollViewer>().Single(view => view.Tag as string == "PageScroll");
                    scroll.ChangeView(null, scroll.ScrollableHeight, null, true);
                    await CaptureAsync(Path.Combine(directory, $"gallery-inputs-{design}-{theme}-{language}.png"));
                }
            }
        }
        finally { ApplySmokePreferences(original); }
    }
}
