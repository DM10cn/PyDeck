using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckLiveMaterialInteractionsAsync(List<string> checks)
    {
        var original = preferences; var originalPage = page; var originalSize = AppWindow.Size;
        try
        {
            ApplySmokePreferences(preferences with { Design = "Material", Theme = "Dark", Language = "en-US", Transparency = "Off" });
            var destinations = new[] { ("runtimes", "My Python"), ("catalog", "Install Python"), ("environments", "Virtual environments"),
                ("build", "Build Python"), ("activity", "Activity"), ("settings", "Settings") };
            foreach (var width in new[] { 930, 1180 })
            {
                Navigate("runtimes");
                var scale = Root.XamlRoot.RasterizationScale;
                AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(850 * scale)));
                Root.UpdateLayout(); await Task.Delay(80); Root.UpdateLayout();
                var shell = Descendants(ShellHost).OfType<Grid>().Single(grid => grid.Tag as string == "MaterialShell");
                var navigationWidth = shell.ColumnDefinitions[0].ActualWidth;
                var navigation = Descendants(ShellHost).OfType<RadioButton>().Where(item => item.GroupName == "MaterialNavigation").ToArray();
                if (navigation.Length != destinations.Length) throw new IOException("Compact Material navigation lost a destination");
                foreach (var (id, title) in destinations)
                {
                    var item = navigation.Single(item => item.Tag as string == id);
                    if (!item.IsTabStop || AutomationProperties.GetName(item) != T(title) || item.ActualHeight < 32)
                        throw new IOException("Navigation destination lost keyboard access, accessible name or hit area: " + id);
                    var itemBounds = item.TransformToVisual(shell).TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
                    if (itemBounds.Left < -.5 || itemBounds.Right > navigationWidth + .5 || itemBounds.Top < -.5 || itemBounds.Bottom > shell.ActualHeight + .5)
                        throw new IOException($"Material navigation item {id} exceeds its drawer at {width} DIP: x={itemBounds.Left:F1}..{itemBounds.Right:F1}, drawer={navigationWidth:F1}");
                    var icon = ((StackPanel)item.Content).Children.OfType<FontIcon>().Single();
                    var iconBounds = icon.TransformToVisual(item).TransformBounds(new Rect(0, 0, icon.ActualWidth, icon.ActualHeight));
                    if (icon.ActualWidth <= 0 || icon.ActualHeight <= 0 || iconBounds.Left < -.5 || iconBounds.Right > item.ActualWidth + .5 ||
                        iconBounds.Top < -.5 || iconBounds.Bottom > item.ActualHeight + .5)
                        throw new IOException($"Material navigation icon {id} is clipped by its item at {width} DIP: {iconBounds}, item={item.ActualWidth:F1}x{item.ActualHeight:F1}");
                    var label = ((StackPanel)item.Content).Children.OfType<TextBlock>().Single();
                    if (label.Visibility != (width == 930 ? Visibility.Collapsed : Visibility.Visible))
                        throw new IOException("Material navigation label did not follow compact drawer state");
                    var peer = FrameworkElementAutomationPeer.FromElement(item) ?? FrameworkElementAutomationPeer.CreatePeerForElement(item);
                    ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
                    Root.UpdateLayout();
                    if (page != id || item.IsChecked != true || navigation.Count(candidate => candidate.IsChecked == true) != 1)
                        throw new IOException("Native navigation selection did not reach the requested page: " + id);
                }
            }
            Navigate("components"); Root.UpdateLayout(); await Task.Delay(60); Root.UpdateLayout();
            var pageVisual = PageHost.Children.Single();
            var primary = Descendants(PageHost).OfType<Button>().Single(button => AutomationProperties.GetName(button) == T("Primary action"));
            if (!primary.Focus(FocusState.Keyboard)) throw new IOException("Material motion fixture could not receive keyboard focus");
            var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
            foreach (var enabled in new[] { false, true, false, true })
            {
                palette.UpdateMotion(Root, enabled); Root.UpdateLayout();
                var transitions = Descendants(Root).OfType<FrameworkElement>().Where(element => element.Tag as string == "MaterialMotionRoot")
                    .SelectMany(element => VisualStateManager.GetVisualStateGroups(element)).SelectMany(group => group.Transitions).ToArray();
                if (transitions.Length == 0 || transitions.Any(transition => !transition.GeneratedDuration.HasTimeSpan ||
                    transition.GeneratedDuration.TimeSpan.TotalMilliseconds != (enabled && !palette.Tokens.HighContrast ? palette.Tokens.StateDurationMs : 0)))
                    throw new IOException("Existing Material templates did not update their live animation durations");
                for (var index = 0; index < 8; index++)
                {
                    VisualStateManager.GoToState(primary, "PointerOver", true);
                    VisualStateManager.GoToState(primary, "Pressed", true);
                }
                VisualStateManager.GoToState(primary, "Normal", false);
                if (!ReferenceEquals(pageVisual, PageHost.Children.Single()) || !primary.IsLoaded ||
                    !ReferenceEquals(focused, FocusManager.GetFocusedElement(Root.XamlRoot)))
                    throw new IOException("Motion preference update rebuilt controls or lost keyboard focus");
            }
            checks.Add("Material compact/expanded navigation exposes and selects all six destinations through native UI Automation. Updating live animation durations off/on preserves the current page, loaded controls and keyboard focus; rapid state changes settle without a command.");
        }
        finally
        {
            palette.UpdateMotion(Root, systemUi.AnimationsEnabled); AppWindow.Resize(originalSize);
            ApplySmokePreferences(original); Navigate(originalPage);
        }
    }
}
