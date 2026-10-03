using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;
using Windows.UI;

namespace PimGui.App;

internal static class DesignExpanderStyles
{
    private sealed class CatalogSegmentState
    {
        public required DesignTokens Tokens;
        public bool First;
        public bool Last;
    }
    private static readonly ConditionalWeakTable<Expander, CatalogSegmentState> catalogSegments = new();

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
            var outline = tokens.HighContrast ? tokens.Outline : Microsoft.UI.Colors.Transparent;
            Brush("ExpanderContentBorderBrush", outline);
            resources["ExpanderHeaderBorderThickness"] = new Thickness(tokens.HighContrast ? 1 : 0);
            resources["ExpanderContentDownBorderThickness"] = tokens.HighContrast ? new Thickness(1, 0, 1, 1) : new Thickness(0);
            resources["ExpanderContentUpBorderThickness"] = tokens.HighContrast ? new Thickness(1, 1, 1, 0) : new Thickness(0);
            var disabled = tokens.HighContrast ? tokens.Muted : DesignComponents.Mix(tokens.Surface, tokens.Text, tokens.DisabledTextOpacity);
            Brush("ExpanderHeaderDisabledForeground", disabled);
            Brush("ExpanderHeaderDisabledBorderBrush", tokens.HighContrast ? disabled : Microsoft.UI.Colors.Transparent);
            foreach (var state in new[] { "", "PointerOver", "Pressed" })
            {
                var opacity = state switch { "PointerOver" => tokens.HoverStateOpacity, "Pressed" => tokens.PressedStateOpacity, _ => 0 };
                Brush("ExpanderHeaderForeground" + state, tokens.Text);
                Brush("ExpanderHeaderBorder" + state + "Brush", outline);
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
            expander.BorderBrush = Palette.Brush(tokens.HighContrast ? tokens.Outline : Microsoft.UI.Colors.Transparent);
            expander.BorderThickness = new(tokens.HighContrast ? 1 : 0);
            expander.FocusVisualPrimaryBrush = Palette.Brush(tokens.Accent);
            expander.FocusVisualSecondaryBrush = Palette.Brush(tokens.Card);
        }
        foreach (var entry in resources)
            if (entry.Key is string key && key.StartsWith("Expander", StringComparison.Ordinal))
                expander.Resources[key] = entry.Value;
    }

    public static void ConfigureCatalogSegment(Expander header, ResourceDictionary resources, DesignTokens tokens,
        bool first, bool last, bool expanded)
    {
        Configure(header, resources, tokens);
        header.Padding = new(0);
        // Releases are sibling rows in the virtualized list. The native Expander still
        // owns keyboard/UIA expansion, but its empty content must not reserve a row.
        if (!catalogSegments.TryGetValue(header, out var state))
        {
            state = new() { Tokens = tokens };
            catalogSegments.Add(header, state);
            header.Loaded += (_, _) => ApplyCatalogParts(header);
            header.Expanding += (_, _) => header.DispatcherQueue.TryEnqueue(() => ApplyCatalogParts(header));
            header.Collapsed += (_, _) => header.DispatcherQueue.TryEnqueue(() => ApplyCatalogParts(header));
        }
        state.Tokens = tokens; state.First = first; state.Last = last;
        if (tokens.Design == "Material")
        {
            header.MinHeight = 44;
            header.CornerRadius = SegmentCorner(first, last && !expanded);
            header.Resources["ExpanderHeaderBorderThickness"] = new Thickness(tokens.HighContrast ? 1 : 0);
            header.Resources["ExpanderHeaderPadding"] = new Thickness(16, 0, 0, 0);
            foreach (var suffix in new[] { "", "PointerOver", "Pressed" })
            {
                var opacity = suffix switch { "PointerOver" => tokens.HoverStateOpacity, "Pressed" => tokens.PressedStateOpacity, _ => 0 };
                var surface = tokens.HighContrast ? tokens.Card : tokens.SurfaceContainer;
                header.Resources["ExpanderHeaderBackground" + suffix] = Palette.Brush(DesignComponents.Mix(surface, tokens.Text, opacity));
                header.Resources["ExpanderHeaderBorder" + suffix + "Brush"] = Palette.Brush(tokens.HighContrast ? tokens.Outline : Microsoft.UI.Colors.Transparent);
            }
        }
        if (header.IsLoaded) ApplyCatalogParts(header);
    }

    private static CornerRadius SegmentCorner(bool first, bool last)
        => new(first ? 16 : 4, first ? 16 : 4, last ? 16 : 4, last ? 16 : 4);

    private static void ApplyCatalogParts(Expander header)
    {
        if (!catalogSegments.TryGetValue(header, out var state)) return;
        foreach (var part in Parts(header))
        {
            if (part is Border { Name: "ExpanderContent" or "ExpanderContentClip" } content)
            {
                content.MinHeight = 0; content.MaxHeight = 0; content.Height = 0;
                content.Padding = new(0); content.BorderThickness = new(0);
            }
            if (state.Tokens.Design == "Material" && part is ToggleButton { Name: "ExpanderHeader" } toggle)
            {
                toggle.CornerRadius = SegmentCorner(state.First, state.Last && !header.IsExpanded);
                toggle.MinHeight = 44;
                toggle.FocusVisualPrimaryBrush = Palette.Brush(state.Tokens.Accent);
                toggle.FocusVisualSecondaryBrush = Palette.Brush(state.Tokens.Card);
            }
        }
    }

    private static IEnumerable<DependencyObject> Parts(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Parts(child)) yield return descendant;
        }
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
