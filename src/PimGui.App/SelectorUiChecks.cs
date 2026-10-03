using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckSelectorsAsync(string directory)
    {
        if (smokeDirectory is null) throw new IOException("Isolated selector test profile required");
        var original = preferences;
        var originalPage = page;
        ComboBox? openSelector = null;
        try
        {
            foreach (var design in new[] { "Fluent", "Material" })
            foreach (var theme in new[] { "Light", "Dark" })
            {
                var context = $"Selector/{design}/{theme}";
                ApplySmokePreferences(preferences with { Design = design, Theme = theme, Language = "zh-CN", Transparency = "Off", MaterialColorSource = "Custom", MaterialSeed = 0xff287c60 });
                Navigate("components");
                // Use the production Choice factory so the check covers its explicit
                // ComboBoxItem containers and component resources, not a parallel fixture style.
                var changes = 0;
                var options = Enumerable.Range(0, 40).Select(index => (index.ToString(), $"Python {index + 1} · 测试选项")).ToArray();
                var selector = Choice(options, "3", _ => changes++, "Selector fixture");
                openSelector = selector;
                selector.Width = 320;
                selector.MaxDropDownHeight = 260;
                var disabledSelector = Choice(options, "1", _ => throw new IOException("Disabled selector changed"), "Disabled selector fixture");
                disabledSelector.Width = 320; disabledSelector.IsEnabled = false;
                var fixture = new StackPanel { Spacing = 20, Margin = new(24), HorizontalAlignment = HorizontalAlignment.Left };
                fixture.Children.Add(palette.Label("Selector fixture", 20, true));
                fixture.Children.Add(selector); fixture.Children.Add(disabledSelector);
                PageHost.Children.Clear(); PageHost.Children.Add(fixture);
                Root.UpdateLayout(); await Task.Delay(80);
                selector.ApplyTemplate(); Root.UpdateLayout();
                await WaitForSmokeConditionAsync(() => selector.IsLoaded && selector.XamlRoot == Root.XamlRoot && selector.ActualWidth > 0 && selector.ActualHeight > 0,
                    context + ": Selector fixture is not attached to the live XAML root");
                void Require(bool condition, string detail) { if (!condition) throw new IOException(context + ": " + detail); }
                void RequireColor(Brush brush, Color expected, string detail) => Require(brush is SolidColorBrush solid && solid.Color == expected,
                    detail + $"; expected {expected}, actual {(brush is SolidColorBrush actual ? actual.Color.ToString() : brush?.GetType().Name)}");
                // Use the peer owned by the live element. An unattached manually constructed
                // peer can become unavailable across an awaited screenshot/layout turn.
                var peer = (ComboBoxAutomationPeer)(FrameworkElementAutomationPeer.FromElement(selector) ?? FrameworkElementAutomationPeer.CreatePeerForElement(selector));
                var expand = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);
                Require(expand is not null && peer.IsKeyboardFocusable(), "Native expansion/keyboard automation was lost");
                Require(selector.Focus(FocusState.Keyboard), "Selector rejected keyboard focus");
                Require(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot), selector), "Keyboard focus did not reach the native ComboBox");
                Require(VisualStateManager.GoToState(selector, "PointerOver", false) && VisualStateManager.GoToState(selector, "Pressed", false) &&
                    VisualStateManager.GoToState(selector, "Normal", false), "Native outer selector interaction states missing");
                Require(!new ComboBoxAutomationPeer(disabledSelector).IsEnabled(), "Disabled selector remains enabled in UIA");
                if (design == "Material")
                {
                    var face = Descendants(selector).OfType<Border>().Single(part => part.Name == "Background");
                    Require(face.BorderThickness == new Thickness(palette.Tokens.HighContrast ? 1 : 0),
                        "Collapsed Material selector retained a decorative outline");
                    RequireColor(face.Background, palette.Tokens.HighContrast ? palette.Card : palette.Tokens.ControlFill,
                        "Collapsed selector lost its independent tonal surface");
                    var nativeGlyph = Descendants(selector).OfType<AnimatedIcon>().Single(part => part.Name == "DropDownGlyph");
                    var arrow = Descendants(selector).OfType<PathIcon>().Single(part => part.Name == "MaterialSelectorArrow");
                    Require(nativeGlyph.Source is not null && nativeGlyph.Opacity == 0 && !arrow.IsHitTestVisible &&
                        arrow.Data is PathGeometry triangle &&
                        triangle.Figures.Count == 1 && triangle.Figures[0].IsClosed && triangle.Figures[0].Segments.Count == 2,
                        "Collapsed selector did not preserve its native animated source and separate filled triangle");
                    foreach (var state in new[] { "PointerOver", "Pressed", "Disabled", "Normal" })
                    {
                        Require(VisualStateManager.GoToState(selector, state, false), "Collapsed selector state missing: " + state);
                        RequireColor(face.Background, ((SolidColorBrush)selector.Resources["ComboBoxBackground" + (state == "Normal" ? "" : state)]).Color,
                            "Collapsed selector lost tonal state feedback: " + state);
                        Require(face.BorderThickness == new Thickness(palette.Tokens.HighContrast ? 1 : 0),
                            "Collapsed selector regained a decorative outline in " + state);
                    }
                    var focusHalo = Descendants(selector).OfType<Border>().Single(part => part.Name == "HighlightBackground");
                    Require(selector.UseSystemFocusVisuals && selector.FocusVisualPrimaryThickness == new Thickness(2) &&
                        selector.FocusVisualSecondaryThickness == new Thickness(0),
                        "Removing decorative outline also removed the single keyboard focus boundary");
                    Require(focusHalo.BorderThickness == new Thickness(0) &&
                        focusHalo.Background is SolidColorBrush { Color.A: 0 } &&
                        focusHalo.BorderBrush is SolidColorBrush { Color.A: 0 },
                        "Native selector halo paints an additional outline behind system focus");
                }
                await CaptureAsync(Path.Combine(directory, $"selector-{design}-{theme}-closed.png"));

                try { expand!.Expand(); }
                catch (Exception error)
                {
                    throw new IOException(context + $": Native UIA expansion failed (loaded={selector.IsLoaded}, enabled={selector.IsEnabled}, root={selector.XamlRoot == Root.XamlRoot}, popup={selector.IsDropDownOpen}, size={selector.ActualWidth:F1}x{selector.ActualHeight:F1})", error);
                }
                Popup? popup = null;
                await WaitForSmokeConditionAsync(() =>
                {
                    popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).FirstOrDefault(candidate => candidate.Child is not null &&
                        Descendants(candidate.Child).OfType<ComboBoxItem>().Any(item => ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), selector)));
                    return selector.IsDropDownOpen && popup?.Child is FrameworkElement { ActualHeight: > 0 };
                }, context + ": Native selector popup did not open");
                var popupVisual = popup!.Child;
                var popupParts = new[] { popupVisual }.Concat(Descendants(popupVisual)).ToArray();
                var popupBorder = popupParts.OfType<Border>().First(part => part.Name == "PopupBorder");
                var scroll = popupParts.OfType<ScrollViewer>().First(part => part.Name == "ScrollViewer");
                var selected = (ComboBoxItem)selector.SelectedItem;
                selected.ApplyTemplate(); Root.UpdateLayout();
                Require(ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(selected), selector), "Selected native item lost its owner");
                Require(scroll.ScrollableHeight > 0, "Long dropdown lost native scrolling");
                if (design == "Material")
                {
                    Require(popupBorder.CornerRadius == new CornerRadius(MaterialSelectorTemplates.PopupRadius), "Rendered Material popup corners are incorrect");
                    RequireColor(popupBorder.Background, palette.Tokens.HighContrast ? palette.Card : palette.Tokens.SurfaceContainer, "Popup is not an opaque tonal surface");
                    var selectedParts = Descendants(selected).OfType<FrameworkElement>().ToArray();
                    Require(!selectedParts.Any(part => part.Name == "Pill"), "Fluent selection pill leaked into a Material item");
                    var row = selectedParts.OfType<Grid>().Single(part => part.Name == "LayoutRoot");
                    var content = selectedParts.OfType<ContentPresenter>().Single(part => part.Name == "ContentPresenter");
                    var check = selectedParts.OfType<FontIcon>().Single(part => part.Name == "SelectionCheck");
                    Require(row.CornerRadius == new CornerRadius(MaterialSelectorTemplates.ItemRadius), "Selected row is not rounded");
                    Require(VisualStateManager.GoToState(selected, "Selected", false), "Native selected state missing");
                    Root.UpdateLayout();
                    RequireColor(row.Background, palette.Tokens.SecondaryContainer, "Selection uses wrong container role");
                    RequireColor(content.Foreground, palette.Tokens.OnSecondaryContainer, "Selection text uses wrong paired role");
                    Require(check.Opacity == 1 && AutomationProperties.GetAccessibilityView(check) == AccessibilityView.Raw,
                        "Selected checkmark is missing or duplicates UIA content");
                    foreach (var state in new[] { "Normal", "PointerOver", "Pressed", "Disabled", "SelectedUnfocused", "SelectedPointerOver", "SelectedPressed", "SelectedDisabled" })
                    {
                        Require(VisualStateManager.GoToState(selected, state, false), "Missing item state " + state);
                        Root.UpdateLayout();
                        RequireColor(row.Background, ((SolidColorBrush)selector.Resources["PyDeckSelectorItemBackground" + (state == "Normal" ? "" : state)]).Color, "Incorrect item state " + state);
                    }
                    VisualStateManager.GoToState(selected, "Selected", false);
                    Require(selected.UseSystemFocusVisuals && selected.FocusVisualPrimaryBrush is SolidColorBrush focus && focus.Color == palette.Accent,
                        "Native keyboard focus outline lost its palette");
                }
                else
                {
                    Require(!Descendants(selected).OfType<FontIcon>().Any(part => part.Name == "SelectionCheck"), "Material item template leaked into Fluent");
                }
                // Render the actual Popup child: a Root-only RenderTargetBitmap omits
                // detached popup surfaces and would not catch the reported regression.
                await CaptureAsync(Path.Combine(directory, $"selector-{design}-{theme}-popup.png"), popupVisual);
                scroll.ChangeView(null, Math.Min(100, scroll.ScrollableHeight), null, true);
                await WaitForSmokeConditionAsync(() => scroll.VerticalOffset > 0, context + ": Popup did not scroll");
                scroll.ChangeView(null, 0, null, true); await Task.Delay(80); Root.UpdateLayout();
                var next = (ComboBoxItem)selector.Items[1]; next.ApplyTemplate();
                // ItemsControl exposes selection on its data-item peer. The visual
                // container peer forwards events but does not own this pattern.
                var nextPeer = peer.GetChildren().Single(child => child.GetName() == T(options[1].Item2));
                var selection = (ISelectionItemProvider)nextPeer.GetPattern(PatternInterface.SelectionItem);
                Require(selection is not null, "Native selection automation unavailable");
                selection!.Select();
                await WaitForSmokeConditionAsync(() => selector.SelectedIndex == 1 && changes == 1, context + ": UIA selection did not reach production Choice callback exactly once");
                Require(selection.IsSelected, "Native UIA selection status is wrong");
                if (selector.IsDropDownOpen) expand.Collapse();
                await WaitForSmokeConditionAsync(() => !selector.IsDropDownOpen, context + ": Native collapse failed");
                Require(selector.Focus(FocusState.Keyboard), "Keyboard focus could not return after popup selection");
                Require(ReferenceEquals(selector, fixture.Children.OfType<ComboBox>().First()) && selector.IsLoaded, "Selecting replaced the selector control");
                openSelector = null;
            }
        }
        finally
        {
            if (openSelector is not null) openSelector.IsDropDownOpen = false;
            ApplySmokePreferences(original); Navigate(originalPage);
        }
    }
}
