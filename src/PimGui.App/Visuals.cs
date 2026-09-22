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
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (icon is not null) content.Children.Add(new FontIcon { Glyph = icon, FontSize = 15 });
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, Padding = compact ? new(14, 8, 14, 8) : new(18, 11, 18, 11),
            CornerRadius = new(tokens.ActionRadius), MinHeight = compact ? 36 : 44 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        ApplySurfaceResources(button);
        if (primary) { button.Background = Brush(Accent); button.Foreground = Brush(OnAccent); button.BorderThickness = new(0); }
        return button;
    }
}
