using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace PimGui.App;

internal sealed class Palette(DesignTokens tokens)
{
    public DesignTokens Tokens => tokens;
    public Color Shell => tokens.Shell;
    public Color Surface => tokens.Surface;
    public Color Card => tokens.Card;
    public Color Text => tokens.Text;
    public Color Muted => tokens.Muted;
    public Color Accent => tokens.Accent;
    public Color OnAccent => tokens.OnAccent;
    public Color AccentContainer => tokens.AccentContainer;
    public Color Line => tokens.Line;
    public Color Green => tokens.Green;
    public double Radius => tokens.CardRadius;
    public static Color C(string hex) => Color.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex.Substring(2, 2), 16), Convert.ToByte(hex.Substring(4, 2), 16));
    public static SolidColorBrush Brush(Color color) => new(color);
    public void ApplySurfaceResources(Control control)
    {
        control.Background = Brush(tokens.ControlFill);
        foreach (var key in new[] { "ExpanderBackground", "ExpanderHeaderBackground", "ExpanderContentBackground", "ExpanderDropDownBackground" })
            control.Resources[key] = Brush(Card);
    }
    public void ApplyAccentResources(Control control)
    {
        // WinUI's default resources use StaticResource aliases; override the consuming control roles.
        foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
        {
            foreach (var key in new[] { "ToggleSwitchFillOn", "ToggleSwitchStrokeOn", "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedStroke" })
                control.Resources[key + suffix] = Brush(Accent);
            control.Resources["RadioButtonCheckGlyphFill" + suffix] = Brush(OnAccent);
            control.Resources["ToggleSwitchKnobFillOn" + suffix] = Brush(OnAccent);
        }
    }
    public TextBlock Label(string text, double size = 14, bool bold = false, bool muted = false)
        => new() { Text = T(text), FontSize = size, FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            Foreground = Brush(muted ? Muted : Text), TextWrapping = TextWrapping.Wrap };
    public Border CardBox(UIElement child, double padding = 20) => new()
    { Child = child, Padding = new(padding), Background = Brush(Card), BorderBrush = Brush(Line), BorderThickness = new(tokens.CardBorder), CornerRadius = new(Radius) };
    public Border Chip(string text, bool accent = false) => new()
    {
        Background = Brush(accent ? AccentContainer : Surface), CornerRadius = new(tokens.ChipRadius), Padding = new(10, 4, 10, 4),
        Child = new TextBlock { Text = T(text), FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(accent ? Accent : Muted) }
    };
    public Button Action(string label, string? icon = null, bool primary = false, bool compact = false)
    {
        label = T(label);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = tokens.ControlSpacing };
        if (icon is not null) content.Children.Add(new FontIcon { Glyph = icon, FontSize = tokens.ControlIconSize, IsTextScaleFactorEnabled = false, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = label, FontSize = tokens.ControlFontSize, FontWeight = Microsoft.UI.Text.FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, Padding = new(tokens.ControlHorizontalPadding, tokens.ControlVerticalPadding, tokens.ControlHorizontalPadding, tokens.ControlVerticalPadding),
            FontSize = tokens.ControlFontSize, CornerRadius = new(tokens.ActionRadius), MinHeight = tokens.ControlHeight,
            VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Tag = "ActionButton" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        ApplySurfaceResources(button);
        if (primary) { button.Background = Brush(Accent); button.Foreground = Brush(OnAccent); button.BorderThickness = new(0); }
        return button;
    }
    public Button IconAction(string label, string icon)
    {
        var button = new Button { Content = new FontIcon { Glyph = icon, FontSize = tokens.ControlIconSize, IsTextScaleFactorEnabled = false },
            Width = tokens.ControlHeight, MinHeight = tokens.ControlHeight, Padding = new(0),
            CornerRadius = new(tokens.ActionRadius), VerticalAlignment = VerticalAlignment.Center, Tag = "IconButton" };
        ApplySurfaceResources(button);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, T(label));
        ToolTipService.SetToolTip(button, T(label));
        return button;
    }
    public StackPanel Section(string title, params UIElement[] content)
    {
        var section = new StackPanel { Spacing = 12, Tag = "SettingsSection" };
        section.Children.Add(Label(title, tokens.SectionTitleSize, true));
        foreach (var child in content) section.Children.Add(child);
        return section;
    }
}
