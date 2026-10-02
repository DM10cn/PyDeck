using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private UIElement BuildComponentGallery()
    {
        var back = palette.Action("Settings", "\uE72B", role: ActionRole.Quiet);
        back.Click += (_, _) => Navigate("settings");
        var layout = PageGrid(GridLength.Auto, new(1, GridUnitType.Star));
        At(layout, Header("DESIGN SYSTEM", "Component gallery", "Preview the active design with real controls. These examples do not change your settings.", back), 0);
        var body = new StackPanel { Spacing = palette.Tokens.SectionSpacing };
        var buttons = palette.Section("Actions");
        var feedback = palette.Label("Try an action", muted: true);
        feedback.Tag = "GalleryFeedback";
        AutomationProperties.SetLiveSetting(feedback, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        var actionSamples = new List<FrameworkElement>();
        var enabledSamples = new List<Button>();
        var clicks = 0;
        foreach (var (role, label) in new[] { (ActionRole.Primary, "Primary action"), (ActionRole.Secondary, "Secondary action"),
            (ActionRole.Standard, "Outlined action"), (ActionRole.Quiet, "Quiet action"), (ActionRole.Destructive, "Destructive action") })
        {
            var samples = new StackPanel { Spacing = 8 };
            var action = palette.Action(label, "\uE710", role: role);
            action.HorizontalAlignment = HorizontalAlignment.Left;
            action.Click += (_, _) => feedback.Text = T("Action invoked {0} times", ++clicks);
            samples.Children.Add(action); enabledSamples.Add(action);
            var compact = palette.Action("Compact action", compact: true, role: role);
            compact.HorizontalAlignment = HorizontalAlignment.Left;
            compact.Click += (_, _) => feedback.Text = T("Action invoked {0} times", ++clicks);
            samples.Children.Add(compact);
            var disabled = palette.Action("Disabled", role: role); disabled.IsEnabled = false;
            disabled.HorizontalAlignment = HorizontalAlignment.Left; samples.Children.Add(disabled);
            actionSamples.Add(samples);
        }
        buttons.Children.Add(BuildCompactGrid(actionSamples, 190));
        buttons.Children.Add(feedback);
        buttons.Children.Add(SettingRow("Preview interaction state", "Use Tab to inspect keyboard focus and Space or Enter to activate an action.",
            Choice([("Normal", "Normal"), ("PointerOver", "Hovered"), ("Pressed", "Pressed"), ("Focused", "Keyboard focus")], "Normal", state =>
            {
                foreach (var sample in enabledSamples) { sample.ApplyTemplate(); VisualStateManager.GoToState(sample, state == "Focused" ? "Normal" : state, false); }
                if (state == "Focused")
                    DispatcherQueue.TryEnqueue(() => enabledSamples[0].Focus(FocusState.Keyboard));
            }, "Preview interaction state")));
        body.Children.Add(buttons);

        var inputs = palette.Section("Input and selection");
        var input = new TextBox { PlaceholderText = T("Example text"), MinWidth = 220, MaxWidth = 320 };
        palette.ApplySurfaceResources(input); AutomationProperties.SetName(input, T("Example text"));
        inputs.Children.Add(SettingRow("Text input", null, input));
        var disabledInput = new TextBox { Text = T("Example text"), MinWidth = 220, MaxWidth = 320, IsEnabled = false };
        palette.ApplySurfaceResources(disabledInput); AutomationProperties.SetName(disabledInput, T("Disabled text input"));
        inputs.Children.Add(SettingRow("Disabled text input", null, disabledInput));
        inputs.Children.Add(SettingRow("Choice", null, Choice([("one", "First option"), ("two", "Second option")], "one", _ => { }, "Choice")));
        var disabledChoice = Choice([("one", "First option"), ("two", "Second option")], "one", _ => { }, "Disabled choice");
        disabledChoice.IsEnabled = false;
        inputs.Children.Add(SettingRow("Disabled choice", null, disabledChoice));
        inputs.Children.Add(SettingRow("Switch on", null, Toggle(true, _ => { }, "Switch on")));
        inputs.Children.Add(SettingRow("Switch off", null, Toggle(false, _ => { }, "Switch off")));
        var disabledToggle = Toggle(true, _ => { }, "Disabled switch"); disabledToggle.IsEnabled = false;
        inputs.Children.Add(SettingRow("Disabled switch", null, disabledToggle));
        foreach (var disabled in new[] { false, true })
        {
            var radios = new StackPanel { Spacing = 2 };
            foreach (var label in new[] { "First option", "Second option" })
            {
                var radio = new RadioButton { Content = T(label), GroupName = disabled ? "GalleryDisabledSelection" : "GallerySelection",
                    IsChecked = label == "First option", IsEnabled = !disabled };
                palette.ApplyAccentResources(radio); radios.Children.Add(radio);
            }
            inputs.Children.Add(SettingRow(disabled ? "Disabled selection" : "Selection", null, radios));
        }
        body.Children.Add(inputs);
        var type = palette.Section("Typography and status");
        type.Children.Add(palette.Label("A home for Python.", palette.Tokens.PageTitleSize, true));
        type.Children.Add(palette.Label("Your versions, in one place.", palette.Tokens.BodyFontSize));
        type.Children.Add(palette.Label("Status labels are informational.", palette.Tokens.CaptionFontSize, muted: true));
        type.Children.Add(Toolbar(palette.Badge("Default", true), palette.Badge("Preview"), palette.Badge("Local build")));
        body.Children.Add(type);
        At(layout, PageScroll(body), 1);
        return layout;
    }
}
