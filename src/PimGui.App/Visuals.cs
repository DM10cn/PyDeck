using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace PimGui.App;

internal sealed class Palette(DesignTokens tokens)
{
    private readonly DesignComponents components = DesignComponents.Create(tokens);
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
    public void InstallResources(ResourceDictionary root)
    {
        // Native control internals and popup roots can create a template before they acquire
        // the page's resource scope. Keep the single active design at application scope so
        // those templates resolve exactly the same tokens as normal page controls.
        var resourceHost = Application.Current.Resources;
        // Replace in place: removing first can synchronously re-evaluate live ThemeResource
        // expressions while their dictionary is absent, including templates on outgoing pages.
        var activeIndex = -1;
        for (var index = 0; index < resourceHost.MergedDictionaries.Count; index++)
            if (resourceHost.MergedDictionaries[index].ContainsKey("PyDeckDesignDictionary")) { activeIndex = index; break; }
        if (activeIndex < 0) resourceHost.MergedDictionaries.Add(components.Resources);
        else resourceHost.MergedDictionaries[activeIndex] = components.Resources;
        for (var index = resourceHost.MergedDictionaries.Count - 1; index > activeIndex && activeIndex >= 0; index--)
            if (resourceHost.MergedDictionaries[index].ContainsKey("PyDeckDesignDictionary")) resourceHost.MergedDictionaries.RemoveAt(index);
        if (!ReferenceEquals(root, resourceHost))
            for (var index = root.MergedDictionaries.Count - 1; index >= 0; index--)
                if (root.MergedDictionaries[index].ContainsKey("PyDeckDesignDictionary")) root.MergedDictionaries.RemoveAt(index);
    }
    public void ApplySurfaceResources(Control control)
    {
        if (control is Expander expander) { components.ConfigureExpander(expander); return; }
        control.Background = Brush(tokens.ControlFill);
        if (control is ComboBox selector) components.ConfigureSelector(selector);
        foreach (var key in new[] { "ExpanderBackground", "ExpanderHeaderBackground", "ExpanderContentBackground", "ExpanderDropDownBackground" })
            control.Resources[key] = Brush(Card);
    }
    public void ApplyAccentResources(Control control)
    {
        components.ConfigureAccent(control);
    }
    public void FinishReleaseGroup(Expander group, StackPanel rows)
    {
        if (tokens.Design != "Material") return;
        group.BorderBrush = Brush(Microsoft.UI.Colors.Transparent);
        if (rows.Children.LastOrDefault() is Border last) last.BorderThickness = new(0);
    }
    public TextBlock Label(string text, double size = 14, bool bold = false, bool muted = false)
        => new() { Text = T(text), FontSize = size, FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            FontFamily = new FontFamily(tokens.FontFamily), LineHeight = tokens.LineHeightFor(size), LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = Brush(muted ? Muted : Text), TextWrapping = TextWrapping.Wrap };
    public TextBlock Label(string text, TextRole role, bool muted = false)
    {
        var typography = tokens.Typography(role);
        var label = Label(text, typography.Size, typography.Emphasized, muted);
        label.LineHeight = typography.LineHeight;
        return label;
    }
    public Border CardBox(UIElement child, double padding = 20) => new()
    { Child = child, Padding = new(padding), Background = Brush(Card), BorderBrush = Brush(Line), BorderThickness = new(tokens.CardBorder), CornerRadius = new(Radius) };
    public Border RuntimeCardBox(UIElement child, bool isDefault, double? padding = null)
    {
        var card = CardBox(child, padding ?? tokens.RowPadding);
        if (tokens.Design == "Material")
        {
            // Default is a model status, not selection: retain the badge and use only a
            // modest tonal difference, without a permanent accent border or extra height.
            card.Background = Brush(tokens.HighContrast ? tokens.Card : isDefault
                ? DesignComponents.Mix(tokens.SurfaceContainer, tokens.PrimaryContainer, .28)
                : tokens.SurfaceContainer);
            card.CornerRadius = new(tokens.CardRadius);
        }
        return card;
    }
    public CornerRadius CatalogSegmentCorner(bool first, bool last) => tokens.Design == "Material"
        ? new(first ? 16 : 4, first ? 16 : 4, last ? 16 : 4, last ? 16 : 4)
        : new(tokens.CardRadius);
    public void ApplyCatalogSegment(Expander header, bool first, bool last, bool expanded)
        => DesignExpanderStyles.ConfigureCatalogSegment(header, components.Resources, tokens, first, last, expanded);
    public void UpdateMotion(DependencyObject root, bool enabled) => components.UpdateMotion(root, enabled);
    public void ApplyMotion(DependencyObject root) => components.ApplyMotion(root);
    public Border Badge(string text, bool accent = false) => new()
    {
        Background = Brush(accent ? AccentContainer : Surface), CornerRadius = new(tokens.ChipRadius), Padding = new(10, 4, 10, 4),
        Child = new TextBlock { Text = T(text), FontSize = tokens.CaptionFontSize, LineHeight = tokens.Typography(TextRole.Caption).LineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontFamily = new FontFamily(tokens.FontFamily),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(accent ? tokens.OnAccentContainer : Muted) },
        Tag = "StatusBadge"
    };
    public Border Chip(string text, bool accent = false) => Badge(text, accent);
    public Button Action(string label, string? icon = null, bool primary = false, bool compact = false, ActionRole role = ActionRole.Standard)
        => components.Action(T(label), icon, primary ? ActionRole.Primary : role, compact);
    public Button RuntimeTerminalAction()
        => components.Action(T("Terminal"), "\uE756", tokens.Design == "Material" ? ActionRole.Secondary : ActionRole.Quiet, true);
    public void ConfigureAction(Button button, ActionRole role = ActionRole.Standard, bool compact = false)
        => components.ConfigureAction(button, role, compact);
    public void ConfigureProgress(ProgressBar progress) => components.ConfigureProgress(progress);
    public void ConfigureDesignChoice(Microsoft.UI.Xaml.Controls.Primitives.ToggleButton button, bool selected)
        => components.ConfigureDesignChoice(button, selected);
    public AutoSuggestBox Search(string placeholder, string text = "") => components.Search(T(placeholder), text);
    public Expander Expander(object header, UIElement? content = null, bool expanded = false)
        => components.Expander(header is string text ? T(text) : header, content, expanded);
    public Button IconAction(string label, string icon) => components.IconAction(T(label), icon);
    public StackPanel Section(string title, params UIElement[] content)
    {
        var section = new StackPanel { Spacing = 12, Tag = "SettingsSection" };
        section.Children.Add(Label(title, tokens.SectionTitleSize, true));
        foreach (var child in content) section.Children.Add(child);
        return section;
    }
}
