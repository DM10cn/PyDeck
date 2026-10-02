using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace PimGui.App;

internal static class DesignExpanderStyles
{
    public static void Install(ResourceDictionary resources, DesignTokens tokens)
    {
        // Expander's default template is supplied by the native control itself. Its
        // named style is not available in Application.Resources during startup.
        // Keep Style/Template unset and customize consuming resources/properties only.
        if (tokens.Design == "Material")
        {
            void Brush(string key, Color color) => resources[key] = Palette.Brush(color);
            Brush("ExpanderHeaderBackground", tokens.Card);
            Brush("ExpanderContentBackground", tokens.Card);
            Brush("ExpanderContentBorderBrush", tokens.OutlineVariant);
            var disabled = tokens.HighContrast ? tokens.Muted : DesignComponents.Mix(tokens.Surface, tokens.Text, tokens.DisabledTextOpacity);
            Brush("ExpanderHeaderDisabledForeground", disabled);
            Brush("ExpanderHeaderDisabledBorderBrush", disabled);
            foreach (var state in new[] { "", "PointerOver", "Pressed" })
            {
                var opacity = state switch { "PointerOver" => tokens.HoverStateOpacity, "Pressed" => tokens.PressedStateOpacity, _ => 0 };
                Brush("ExpanderHeaderForeground" + state, tokens.Text);
                Brush("ExpanderHeaderBorder" + state + "Brush", tokens.OutlineVariant);
                Brush("ExpanderChevron" + state + "Foreground", tokens.Text);
                Brush("ExpanderChevron" + state + "Background", opacity == 0 ? Microsoft.UI.Colors.Transparent : DesignComponents.Mix(tokens.Card, tokens.Text, opacity));
                Brush("ExpanderChevronBorder" + state + "Brush", Microsoft.UI.Colors.Transparent);
            }
        }
    }

    public static void Configure(Expander expander, ResourceDictionary resources, DesignTokens tokens)
    {
        if (tokens.Design == "Material")
        {
            expander.CornerRadius = new(tokens.CardRadius);
            expander.FontFamily = new FontFamily(tokens.FontFamily);
            expander.FontSize = tokens.BodyFontSize;
            expander.Foreground = Palette.Brush(tokens.Text);
            expander.Background = Palette.Brush(tokens.Card);
            expander.BorderBrush = Palette.Brush(tokens.OutlineVariant);
            expander.FocusVisualPrimaryBrush = Palette.Brush(tokens.Accent);
            expander.FocusVisualSecondaryBrush = Palette.Brush(tokens.Card);
        }
        foreach (var entry in resources)
            if (entry.Key is string key && key.StartsWith("Expander", StringComparison.Ordinal))
                expander.Resources[key] = entry.Value;
    }
}

public sealed partial class MainWindow
{
    // DataTemplate instances cannot use the C# factory; configure once when loaded.
    private void OnDesignExpanderLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is Expander expander) palette.ApplySurfaceResources(expander);
    }
}
