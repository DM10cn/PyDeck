using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckExpandersAsync(string directory)
    {
        var originalPreferences = preferences;
        var originalStorageExpanded = storageExpanded;
        try
        {
            foreach (var design in new[] { "Fluent", "Material" })
            foreach (var theme in new[] { "Light", "Dark" })
            foreach (var category in design == "Material" ? new[] { "appearance", "storage" } : new[] { "storage" })
            {
                ApplySmokePreferences(preferences with { Design = design, Theme = theme, MaterialColorSource = "Custom", Transparency = "Off" });
                OpenSettingsCategoryForSmoke(category);
                var expanders = Descendants(PageHost).OfType<Expander>().ToArray();
                var targets = expanders.Where(expander => expander.Header is string).ToArray();
                if (targets.Length == 0)
                    throw new IOException("Settings category lacks its expander fixture: " + category);
                foreach (var expander in expanders)
                {
                    expander.ApplyTemplate();
                    if (expander.ReadLocalValue(Control.StyleProperty) != DependencyProperty.UnsetValue ||
                        expander.ReadLocalValue(Control.TemplateProperty) != DependencyProperty.UnsetValue)
                        throw new IOException($"{design}: expander replaced its native SDK style or template");
                    if (design == "Material" && expander.CornerRadius != new CornerRadius(palette.Tokens.CardRadius))
                        throw new IOException($"Material expander {expander.Header} missed the shared card shape");
                }
                foreach (var (expander, index) in targets.Select((item, index) => (item, index)))
                {
                    var context = $"Expander/{design}/{theme}/{category}/{expander.Header}";
                    var peer = FrameworkElementAutomationPeer.FromElement(expander) ?? FrameworkElementAutomationPeer.CreatePeerForElement(expander);
                    var provider = (IExpandCollapseProvider)peer.GetPattern(PatternInterface.ExpandCollapse);
                    foreach (var expanded in new[] { false, true })
                    {
                        if (expanded) provider.Expand(); else provider.Collapse();
                        await Task.Delay(360); Root.UpdateLayout();
                        if (expander.IsExpanded != expanded) throw new IOException(context + ": native expand/collapse failed");
                        var header = Descendants(expander).OfType<ToggleButton>().Single(part => part.Name == "ExpanderHeader");
                        var grid = Descendants(header).OfType<Grid>().Single(part => part.Name == "ToggleButtonGrid");
                        var content = Descendants(expander).OfType<Border>().Single(part => part.Name == "ExpanderContent");
                        var radius = expander.CornerRadius;
                        var expectedHeader = expanded ? new CornerRadius(radius.TopLeft, radius.TopRight, 0, 0) : radius;
                        var expectedContent = new CornerRadius(0, 0, radius.BottomRight, radius.BottomLeft);
                        if (header.CornerRadius != expectedHeader || grid.CornerRadius != expectedHeader || content.CornerRadius != expectedContent)
                            throw new IOException(context + $": rendered corners disagree with native expanded shape ({header.CornerRadius}/{grid.CornerRadius}/{content.CornerRadius})");
                        if (design == "Material" && (grid.Background is not SolidColorBrush fill || fill.Color != palette.Card ||
                            content.Background is not SolidColorBrush body || body.Color != palette.Card))
                            throw new IOException(context + ": header/content do not share their Monet container");
                        if (design == "Material" && !palette.Tokens.HighContrast)
                        {
                            var states = expanded ? new[] { "CheckedPointerOver", "CheckedPressed", "CheckedDisabled", "Checked" }
                                : new[] { "PointerOver", "Pressed", "Disabled", "Normal" };
                            foreach (var interaction in states)
                            {
                                if (!VisualStateManager.GoToState(header, interaction, false))
                                    throw new IOException(context + ": missing native header state " + interaction);
                                if (grid.BorderThickness != new Thickness(0) ||
                                    grid.BorderBrush is not SolidColorBrush { Color.A: 0 } ||
                                    content.BorderBrush is not SolidColorBrush { Color.A: 0 })
                                    throw new IOException(context + ": decorative outline remains in " + interaction);
                            }
                        }
                        if (theme == "Dark")
                        {
                            expander.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
                            await CaptureAsync(Path.Combine(directory, $"expander-{design}-{category}-{index}-{(expanded ? "expanded" : "collapsed")}.png"), expander);
                        }
                    }
                }
            }
        }
        finally
        {
            storageExpanded = originalStorageExpanded;
            ApplySmokePreferences(originalPreferences);
        }
    }
}
