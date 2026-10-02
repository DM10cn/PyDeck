using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace PimGui.App;

internal enum ActionRole { Standard, Primary, Secondary, Quiet, Destructive }

// Each implementation owns its templates. Pages request a semantic component, not a design flag.
internal abstract class DesignComponents
{
    protected DesignTokens Tokens { get; }
    public ResourceDictionary Resources { get; } = new();
    protected bool AnimationsEnabled { get; }

    protected DesignComponents(DesignTokens tokens)
    {
        Tokens = tokens;
        try { AnimationsEnabled = !tokens.HighContrast && new UISettings().AnimationsEnabled; }
        catch { AnimationsEnabled = false; }
        Resources["PyDeckDesignDictionary"] = tokens.Design;
        Resources["PyDeckAnimationsEnabled"] = AnimationsEnabled;
        AddBrush("PyDeckText", tokens.Text);
        AddBrush("PyDeckMuted", tokens.Muted);
        AddBrush("PyDeckSurface", tokens.Surface);
        AddBrush("PyDeckFocus", tokens.Accent);
        AddBrush("PyDeckFocusContrast", tokens.Surface);
        AddBrush("PyDeckActionStateLayer", tokens.Accent);
        AddBrush("PyDeckActionDisabledBackground", Mix(tokens.Surface, tokens.Text, tokens.DisabledContainerOpacity));
        AddBrush("PyDeckActionDisabledForeground", DisabledText());
        AddBrush("PyDeckActionDisabledOutline", DisabledText());
        Resources["PyDeckActionPressedCornerRadius"] = new CornerRadius(tokens.PressedActionRadius);
        Resources["PyDeckFontFamily"] = new FontFamily(tokens.FontFamily);
        InstallNativeResources();
        DesignExpanderStyles.Install(Resources, tokens);
        DesignScrollStyles.Install(Resources, tokens);
    }

    public static DesignComponents Create(DesignTokens tokens) => tokens.Design == "Fluent"
        ? new FluentComponents(tokens) : new MaterialExpressiveComponents(tokens);

    protected void AddBrush(string name, Color color) => Resources[name] = Palette.Brush(color);

    public void ConfigureExpander(Expander expander) => DesignExpanderStyles.Configure(expander, Resources, Tokens);

    public Expander Expander(object header, UIElement? content, bool expanded)
    {
        var expander = new Expander
        {
            Header = header, Content = content, IsExpanded = expanded,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        ConfigureExpander(expander);
        return expander;
    }

    // Without an explicit BasedOn, a custom WinUI style can fall back to legacy
    // framework templates instead of inheriting the current Windows App SDK style.
    protected static Style NativeStyle(Type type, string? key = null) => new(type)
    {
        BasedOn = (Style)Application.Current.Resources[key ?? $"Default{type.Name}Style"]
    };

    internal static Color Mix(Color background, Color foreground, double opacity) => Color.FromArgb(255,
        (byte)Math.Round(background.R + (foreground.R - background.R) * opacity),
        (byte)Math.Round(background.G + (foreground.G - background.G) * opacity),
        (byte)Math.Round(background.B + (foreground.B - background.B) * opacity));

    private void InstallNativeResources()
    {
        // Native edit/selection controls still own text input, popup placement and accessibility.
        // Explicit state resources give them a complete palette in both designs and contrast themes.
        foreach (var suffix in new[] { "", "PointerOver", "Pressed", "Focused", "Disabled" })
        {
            var opacity = suffix switch { "PointerOver" => Tokens.HoverStateOpacity, "Pressed" => Tokens.PressedStateOpacity, _ => 0 };
            var fill = suffix == "Disabled" ? Mix(Tokens.Surface, Tokens.Text, .04) : Mix(Tokens.ControlFill, Tokens.Text, opacity);
            var text = suffix == "Disabled" ? DisabledText() : Tokens.Text;
            var stroke = suffix == "Focused" ? Tokens.Accent : Tokens.Outline;
            foreach (var key in new[] { "TextControlBackground", "ComboBoxBackground", "ButtonBackground" }) AddBrush(key + suffix, fill);
            foreach (var key in new[] { "TextControlForeground", "ComboBoxForeground", "ButtonForeground", "RadioButtonForeground", "ToggleSwitchContentForeground" }) AddBrush(key + suffix, text);
            foreach (var key in new[] { "TextControlBorderBrush", "ComboBoxBorderBrush", "ButtonBorderBrush" }) AddBrush(key + suffix, stroke);
        }
        foreach (var suffix in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            var disabled = suffix == "Disabled";
            var opacity = suffix switch { "PointerOver" => Tokens.HoverStateOpacity, "Pressed" => Tokens.PressedStateOpacity, _ => 0 };
            var accent = disabled ? DisabledText() : Mix(Tokens.Accent, Tokens.OnAccent, opacity);
            var off = disabled ? Mix(Tokens.Surface, Tokens.Text, Tokens.DisabledContainerOpacity) : Mix(Tokens.SurfaceHighest, Tokens.Text, opacity);
            foreach (var key in new[] { "ToggleSwitchFillOn", "ToggleSwitchStrokeOn", "RadioButtonOuterEllipseCheckedFill", "RadioButtonOuterEllipseCheckedStroke" }) AddBrush(key + suffix, accent);
            foreach (var key in new[] { "ToggleSwitchKnobFillOn", "RadioButtonCheckGlyphFill" }) AddBrush(key + suffix, disabled ? Tokens.Surface : Tokens.OnAccent);
            AddBrush("ToggleSwitchFillOff" + suffix, off);
            AddBrush("ToggleSwitchStrokeOff" + suffix, disabled ? DisabledText() : Tokens.Outline);
            AddBrush("ToggleSwitchKnobFillOff" + suffix, disabled ? DisabledText() : Tokens.Outline);
            AddBrush("RadioButtonOuterEllipseFill" + suffix, off);
            AddBrush("RadioButtonOuterEllipseStroke" + suffix, disabled ? DisabledText() : Tokens.Outline);
            AddBrush("RadioButtonCheckGlyphStroke" + suffix, disabled ? DisabledText() : Tokens.OnAccent);
            AddBrush("RadioButtonBackground" + suffix, Microsoft.UI.Colors.Transparent);
            AddBrush("RadioButtonBorderBrush" + suffix, Microsoft.UI.Colors.Transparent);
            foreach (var state in new[] { "Unchecked", "Checked", "Indeterminate" })
            {
                var selected = state != "Unchecked";
                AddBrush("CheckBoxBackground" + state + suffix, Microsoft.UI.Colors.Transparent);
                AddBrush("CheckBoxBorderBrush" + state + suffix, Microsoft.UI.Colors.Transparent);
                AddBrush("CheckBoxForeground" + state + suffix, disabled ? DisabledText() : Tokens.Text);
                AddBrush("CheckBoxCheckBackgroundFill" + state + suffix, selected ? accent : off);
                AddBrush("CheckBoxCheckBackgroundStroke" + state + suffix, selected ? accent : disabled ? DisabledText() : Tokens.Outline);
                AddBrush("CheckBoxCheckGlyphForeground" + state + suffix, disabled ? Tokens.Surface : Tokens.OnAccent);
            }
        }
        AddBrush("ToggleSwitchHeaderForeground", Tokens.Text);
        AddBrush("ToggleSwitchKnobStrokeOn", Tokens.OnAccent);
        AddBrush("PyDeckSwitchStateLayer", Tokens.Text);
        AddBrush("AutoSuggestBoxSuggestionsListBackground", Tokens.Card);
        AddBrush("AutoSuggestBoxSuggestionsListBorderBrush", Tokens.Outline);
        Resources["PyDeckHoverOpacity"] = Tokens.HighContrast ? .18 : Tokens.HoverStateOpacity;
        Resources["PyDeckPressedOpacity"] = Tokens.HighContrast ? .28 : Tokens.PressedStateOpacity;

        var input = NativeStyle(typeof(TextBox));
        input.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(Tokens.InputRadius)));
        input.Setters.Add(new Setter(Control.MinHeightProperty, Tokens.ControlHeight));
        input.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily(Tokens.FontFamily)));
        Resources[typeof(TextBox)] = input;
        var selector = NativeStyle(typeof(ComboBox));
        selector.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(Tokens.InputRadius)));
        selector.Setters.Add(new Setter(Control.MinHeightProperty, Tokens.ControlHeight));
        selector.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily(Tokens.FontFamily)));
        Resources[typeof(ComboBox)] = selector;
        var suggest = NativeStyle(typeof(AutoSuggestBox));
        suggest.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(Tokens.InputRadius)));
        suggest.Setters.Add(new Setter(Control.MinHeightProperty, Tokens.ControlHeight));
        suggest.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily(Tokens.FontFamily)));
        Resources[typeof(AutoSuggestBox)] = suggest;
        foreach (var type in new[] { typeof(CheckBox), typeof(RadioButton) })
        {
            var choice = NativeStyle(type);
            choice.Setters.Add(new Setter(Control.MinHeightProperty, Tokens.ControlHeight));
            choice.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily(Tokens.FontFamily)));
            choice.Setters.Add(new Setter(Control.FontSizeProperty, Tokens.BodyFontSize));
            choice.Setters.Add(new Setter(Control.ForegroundProperty, Palette.Brush(Tokens.Text)));
            choice.Setters.Add(new Setter(Control.UseSystemFocusVisualsProperty, true));
            choice.Setters.Add(new Setter(Control.FocusVisualPrimaryBrushProperty, Palette.Brush(Tokens.Accent)));
            choice.Setters.Add(new Setter(Control.FocusVisualSecondaryBrushProperty, Palette.Brush(Tokens.Surface)));
            Resources[type] = choice;
        }
    }

    protected Color DisabledText() => Tokens.HighContrast ? Tokens.Muted : Mix(Tokens.Surface, Tokens.Text, Tokens.DisabledTextOpacity);

    protected abstract Style CreateActionStyle();
    protected virtual void ConfigureShape(Button button, bool compact) { }
    protected virtual void ConfigureSearchEditor(Style editor)
    {
        editor.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(Tokens.InputRadius)));
        editor.Setters.Add(new Setter(Control.MinHeightProperty, Tokens.ControlHeight));
        editor.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily(Tokens.FontFamily)));
        // The native template binds both its text viewport and placeholder to this
        // alignment. Stretch the viewport; align the editable text at its leading edge.
        editor.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        editor.Setters.Add(new Setter(TextBox.TextAlignmentProperty, TextAlignment.Left));
        editor.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
    }

    protected virtual void ConfigureSearch(AutoSuggestBox search) { }

    public AutoSuggestBox Search(string placeholder, string text)
    {
        var editor = NativeStyle(typeof(TextBox), "AutoSuggestBoxTextBoxStyle");
        ConfigureSearchEditor(editor);
        var search = new AutoSuggestBox
        {
            Style = NativeStyle(typeof(AutoSuggestBox)), TextBoxStyle = editor,
            PlaceholderText = placeholder, Text = text, QueryIcon = new SymbolIcon(Symbol.Find),
            CornerRadius = new(Tokens.InputRadius), MinWidth = 120, MinHeight = Tokens.ControlHeight,
            FontSize = Tokens.ControlFontSize, FontFamily = new FontFamily(Tokens.FontFamily),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center, Tag = "SearchBox"
        };
        ConfigureSearch(search);
        return search;
    }
    protected void InstallActionStyles()
    {
        var actionStyle = CreateActionStyle();
        actionStyle.Setters.Add(new Setter(Control.BackgroundProperty, Palette.Brush(Tokens.ControlFill)));
        actionStyle.Setters.Add(new Setter(Control.ForegroundProperty, Palette.Brush(Tokens.Text)));
        actionStyle.Setters.Add(new Setter(Control.BorderBrushProperty, Palette.Brush(Tokens.Outline)));
        actionStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(Tokens.ActionRadius)));
        actionStyle.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily(Tokens.FontFamily)));
        actionStyle.Setters.Add(new Setter(Control.UseSystemFocusVisualsProperty, true));
        actionStyle.Setters.Add(new Setter(Control.FocusVisualPrimaryBrushProperty, Palette.Brush(Tokens.Accent)));
        actionStyle.Setters.Add(new Setter(Control.FocusVisualSecondaryBrushProperty, Palette.Brush(Tokens.Surface)));
        actionStyle.Setters.Add(new Setter(Control.FocusVisualPrimaryThicknessProperty, new Thickness(2)));
        actionStyle.Setters.Add(new Setter(Control.FocusVisualSecondaryThicknessProperty, new Thickness(1)));
        actionStyle.Setters.Add(new Setter(Control.FocusVisualMarginProperty, new Thickness(-3)));
        Resources["PyDeckActionStyle"] = actionStyle;
        foreach (var role in Enum.GetValues<ActionRole>())
        {
            var colors = ActionColors(role);
            var style = new Style(typeof(Button)) { BasedOn = actionStyle };
            style.Setters.Add(new Setter(Control.BackgroundProperty, Palette.Brush(colors.Background)));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Palette.Brush(colors.Foreground)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Palette.Brush(colors.Outline)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(colors.Border)));
            Resources[$"PyDeck{role}ActionStyle"] = style;
        }
    }

    protected readonly record struct ActionColorsToken(Color Background, Color Foreground, Color Outline, double Border);
    protected abstract ActionColorsToken ActionColors(ActionRole role);

    public Button Action(string label, string? icon, ActionRole role, bool compact)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Tokens.ControlSpacing };
        if (icon is not null) content.Children.Add(new FontIcon { Glyph = icon, FontSize = compact ? Tokens.CompactIconSize : Tokens.ControlIconSize,
            IsTextScaleFactorEnabled = false, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = label, FontSize = Tokens.ControlFontSize, LineHeight = Tokens.Typography(TextRole.Label).LineHeight,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight, FontFamily = new FontFamily(Tokens.FontFamily),
            FontWeight = Tokens.Typography(TextRole.Label).Emphasized ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, Tag = "ActionButton" };
        ConfigureAction(button, role, compact);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        return button;
    }

    public void ConfigureAction(Button button, ActionRole role, bool compact)
    {
        var colors = ActionColors(role);
        var horizontal = compact ? Tokens.CompactHorizontalPadding : Tokens.ControlHorizontalPadding;
        var vertical = compact ? Tokens.CompactVerticalPadding : Tokens.ControlVerticalPadding;
        // Existing XAML shell buttons keep their content, name and handlers. Style explicitly so
        // native TextBox/ComboBox internal buttons retain the templates required by their owners.
        button.ClearValue(Control.BackgroundProperty);
        button.ClearValue(Control.ForegroundProperty);
        button.ClearValue(Control.BorderBrushProperty);
        button.ClearValue(Control.BorderThicknessProperty);
        button.Style = (Style)Resources[$"PyDeck{role}ActionStyle"];
        button.Padding = new(horizontal, vertical, horizontal, vertical);
        button.MinHeight = compact ? Tokens.CompactControlHeight : Tokens.ControlHeight;
        button.CornerRadius = new(Tokens.ActionRadius);
        button.VerticalAlignment = VerticalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.FontSize = Tokens.ControlFontSize;
        button.FontFamily = new FontFamily(Tokens.FontFamily);
        button.FontWeight = Tokens.Typography(TextRole.Label).Emphasized ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        ConfigureShape(button, compact);
        button.Resources["PyDeckActionRole"] = role.ToString();
        button.Resources["PyDeckActionCompact"] = compact;
        foreach (var suffix in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            var disabled = suffix == "Disabled";
            var opacity = suffix switch { "PointerOver" => Tokens.HoverStateOpacity, "Pressed" => Tokens.PressedStateOpacity, _ => 0 };
            var disabledFill = role == ActionRole.Quiet || role == ActionRole.Standard ? Tokens.Surface : Mix(Tokens.Surface, Tokens.Text, Tokens.DisabledContainerOpacity);
            button.Resources["ButtonBackground" + suffix] = Palette.Brush(disabled ? disabledFill : Mix(colors.Background.A == 0 ? Tokens.Surface : colors.Background, colors.Foreground, opacity));
            button.Resources["ButtonForeground" + suffix] = Palette.Brush(disabled ? DisabledText() : colors.Foreground);
            button.Resources["ButtonBorderBrush" + suffix] = Palette.Brush(disabled ? DisabledText() : colors.Outline);
        }
        button.Resources["PyDeckActionStateLayer"] = Palette.Brush(colors.Foreground);
        button.Resources["PyDeckActionDisabledBackground"] = button.Resources["ButtonBackgroundDisabled"];
        button.Resources["PyDeckActionDisabledForeground"] = button.Resources["ButtonForegroundDisabled"];
        button.Resources["PyDeckActionDisabledOutline"] = button.Resources["ButtonBorderBrushDisabled"];
        button.Resources["PyDeckActionPressedCornerRadius"] = new CornerRadius(Tokens.PressedActionRadius);
        button.Resources["PyDeckHoverOpacity"] = Tokens.HighContrast ? .18 : Tokens.HoverStateOpacity;
        button.Resources["PyDeckPressedOpacity"] = Tokens.HighContrast ? .28 : Tokens.PressedStateOpacity;
    }

    public Button IconAction(string label, string icon)
    {
        var button = Action(label, null, ActionRole.Quiet, false);
        button.Content = new FontIcon { Glyph = icon, FontSize = Tokens.ControlIconSize, IsTextScaleFactorEnabled = false };
        button.Width = Tokens.ControlHeight;
        button.Padding = new(0);
        button.Tag = "IconButton";
        ToolTipService.SetToolTip(button, label);
        return button;
    }

    public void ConfigureDesignChoice(ToggleButton button, bool selected)
    {
        // This is a native choice control. Override its consuming state resources locally,
        // without changing ToggleButton templates used by any other control.
        foreach (var selection in new[] { "", "Checked", "Indeterminate" })
        foreach (var interaction in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            var chosen = selection.Length > 0;
            var disabled = interaction == "Disabled";
            var fill = chosen ? Tokens.SecondaryContainer : Tokens.ControlFill;
            var foreground = chosen ? Tokens.OnSecondaryContainer : Tokens.Text;
            var outline = chosen ? Tokens.Accent : Tokens.Outline;
            var opacity = interaction switch { "PointerOver" => Tokens.HoverStateOpacity, "Pressed" => Tokens.PressedStateOpacity, _ => 0 };
            if (disabled) { fill = Mix(Tokens.Surface, Tokens.Text, Tokens.DisabledContainerOpacity); foreground = DisabledText(); outline = DisabledText(); }
            else if (opacity > 0) fill = Mix(fill, foreground, opacity);
            var suffix = selection + interaction;
            button.Resources["ToggleButtonBackground" + suffix] = Palette.Brush(fill);
            button.Resources["ToggleButtonForeground" + suffix] = Palette.Brush(foreground);
            button.Resources["ToggleButtonBorderBrush" + suffix] = Palette.Brush(outline);
        }
        button.Background = Palette.Brush(selected ? Tokens.SecondaryContainer : Tokens.ControlFill);
        button.Foreground = Palette.Brush(selected ? Tokens.OnSecondaryContainer : Tokens.Text);
        button.BorderBrush = Palette.Brush(selected ? Tokens.Accent : Tokens.Outline);
        button.BorderThickness = new(selected ? 2 : 1);
        button.CornerRadius = new(Tokens.ActionRadius);
        button.FontFamily = new FontFamily(Tokens.FontFamily);
        button.UseSystemFocusVisuals = true;
        button.FocusVisualPrimaryBrush = Palette.Brush(Tokens.Accent);
        button.FocusVisualSecondaryBrush = Palette.Brush(Tokens.Surface);
        // The preview label was created through Palette.Label, which normally fixes its text
        // color. Here it must inherit the ContentPresenter's checked/disabled state foreground.
        static void InheritTextColor(UIElement element)
        {
            if (element is TextBlock label) label.ClearValue(TextBlock.ForegroundProperty);
            else if (element is Panel panel) foreach (var child in panel.Children) InheritTextColor(child);
            else if (element is Border { Child: { } child }) InheritTextColor(child);
        }
        if (button.Content is UIElement content) InheritTextColor(content);
        button.IsChecked = selected;
    }

    public virtual void ConfigureAccent(Control control)
    {
        foreach (var pair in Resources)
            if (pair.Key is string key && (key.StartsWith("ToggleSwitch", StringComparison.Ordinal) || key.StartsWith("RadioButton", StringComparison.Ordinal) || key.StartsWith("CheckBox", StringComparison.Ordinal) || key.StartsWith("PyDeckSwitch", StringComparison.Ordinal) || key is "PyDeckHoverOpacity" or "PyDeckPressedOpacity"))
                control.Resources[key] = pair.Value;
        control.FontFamily = new FontFamily(Tokens.FontFamily);
        control.Foreground = Palette.Brush(Tokens.Text);
        control.UseSystemFocusVisuals = true;
        control.FocusVisualPrimaryBrush = Palette.Brush(Tokens.Accent);
        control.FocusVisualSecondaryBrush = Palette.Brush(Tokens.Surface);
    }

    public virtual void ConfigureSelector(ComboBox selector) { }

    public void ConfigureProgress(ProgressBar progress) => DesignProgress.Configure(progress, Tokens);
}

internal sealed class FluentComponents : DesignComponents
{
    public FluentComponents(DesignTokens tokens) : base(tokens)
    {
        MaterialSelectorTemplates.InstallPaletteResources(Resources, tokens);
        // WinUI's TextControlElevationBorderBrush/FocusedBrush and translucent fill roles
        // implement Fluent's input effects. Flat app brushes had removed those states.
        foreach (var key in Resources.Keys.OfType<string>().Where(key => key.StartsWith("TextControl", StringComparison.Ordinal)).ToArray())
            Resources.Remove(key);
        // Preserve the SDK's native input styles, including its separate AutoSuggestBox editor.
        Resources.Remove(typeof(TextBox));
        Resources.Remove(typeof(AutoSuggestBox));
        InstallActionStyles();
    }
    protected override Style CreateActionStyle() => NativeStyle(typeof(Button));
    protected override ActionColorsToken ActionColors(ActionRole role) => role switch
    {
        ActionRole.Primary => new(Tokens.Accent, Tokens.OnAccent, Tokens.Accent, Tokens.HighContrast ? 1 : 0),
        ActionRole.Secondary => new(Tokens.SecondaryContainer, Tokens.OnSecondaryContainer, Tokens.Line, 1),
        ActionRole.Quiet => new(Microsoft.UI.Colors.Transparent, Tokens.Text, Microsoft.UI.Colors.Transparent, 0),
        ActionRole.Destructive => new(Tokens.ControlFill, Tokens.Error, Tokens.Outline, 1),
        _ => new(Tokens.ControlFill, Tokens.Text, Tokens.Line, 1)
    };
}

internal sealed class MaterialExpressiveComponents : DesignComponents
{
    public MaterialExpressiveComponents(DesignTokens tokens) : base(tokens)
    {
        InstallMaterialSearchResources();
        MaterialSelectorTemplates.Install(Resources, tokens);
        // Material's radio has a primary ring and center on a surface, rather than a filled Fluent disc.
        foreach (var suffix in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            var color = suffix == "Disabled" ? DisabledText() : tokens.Accent;
            AddBrush("RadioButtonOuterEllipseCheckedFill" + suffix, tokens.Surface);
            AddBrush("RadioButtonCheckGlyphFill" + suffix, color);
            AddBrush("RadioButtonCheckGlyphStroke" + suffix, color);
        }
        Resources["RadioButtonCheckGlyphSize"] = 10.0;
        Resources["RadioButtonBorderThemeThickness"] = 2.0;
        InstallActionStyles();
        var toggle = (Style)XamlReader.Load(MaterialTemplates.ToggleSwitch(AnimationsEnabled ? tokens.StateDurationMs : 0));
        Resources["PyDeckToggleSwitchStyle"] = toggle;
        Resources[typeof(ToggleSwitch)] = toggle;
    }
    private void InstallMaterialSearchResources()
    {
        // Filled desktop search/edit fields use matching MCU surface/on-surface roles.
        // Local copies on the search control also cover its editor and native helper buttons.
        foreach (var state in new[] { "", "PointerOver", "Focused", "Disabled" })
        {
            var disabled = state == "Disabled";
            var fill = state switch
            {
                "PointerOver" => Mix(Tokens.ControlFill, Tokens.Text, Tokens.HoverStateOpacity),
                "Focused" => Tokens.SurfaceHighest,
                "Disabled" => Mix(Tokens.Surface, Tokens.Text, .04),
                _ => Tokens.ControlFill
            };
            AddBrush("TextControlBackground" + state, fill);
            AddBrush("TextControlForeground" + state, disabled ? DisabledText() : Tokens.Text);
            AddBrush("TextControlPlaceholderForeground" + state, disabled ? DisabledText() : Tokens.Muted);
            AddBrush("TextControlBorderBrush" + state, disabled ? DisabledText() : state == "Focused" ? Tokens.Accent : Tokens.Outline);
            AddBrush("TextControlHeaderForeground" + state, disabled ? DisabledText() : Tokens.Text);
        }
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            var disabled = state == "Disabled";
            var opacity = state switch { "PointerOver" => Tokens.HoverStateOpacity, "Pressed" => Tokens.PressedStateOpacity, _ => 0 };
            AddBrush("TextControlButtonBackground" + state, opacity == 0 ? Microsoft.UI.Colors.Transparent : Mix(Tokens.ControlFill, Tokens.Text, opacity));
            AddBrush("TextControlButtonForeground" + state, disabled ? DisabledText() : Tokens.Muted);
            AddBrush("TextControlButtonBorderBrush" + state, Microsoft.UI.Colors.Transparent);
        }
        AddBrush("TextControlSelectionHighlightColor", Tokens.Accent);
        Resources["TextControlBorderThemeThickness"] = new Thickness(1);
        Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(2);
        Resources["TextControlThemePadding"] = new Thickness(14, 8, 10, 8);
    }

    protected override void ConfigureSearch(AutoSuggestBox search)
    {
        foreach (var resource in Resources)
            if (resource.Key is string key && (key.StartsWith("TextControl", StringComparison.Ordinal) || key.StartsWith("AutoSuggestBox", StringComparison.Ordinal)))
                search.Resources[key] = resource.Value;
        search.Background = Palette.Brush(Tokens.ControlFill);
        search.Foreground = Palette.Brush(Tokens.Text);
        search.BorderBrush = Palette.Brush(Tokens.Outline);
        search.BorderThickness = new(1);
        AutoSuggestBoxHelper.SetKeepInteriorCornersSquare(search, false);
    }

    public override void ConfigureSelector(ComboBox selector)
        => MaterialSelectorTemplates.Configure(selector, Resources, Tokens);

    protected override void ConfigureSearchEditor(Style editor)
    {
        base.ConfigureSearchEditor(editor);
        editor.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 8, 10, 8)));
        editor.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        // Keep native TextBoxStyle/Template: replacing it loses AutoSuggestBox's query/clear parts.
    }
    protected override Style CreateActionStyle() => (Style)XamlReader.Load(MaterialTemplates.Button(AnimationsEnabled ? Tokens.StateDurationMs : 0));
    protected override void ConfigureShape(Button button, bool compact) => button.CornerRadius = new(compact ? 16 : Tokens.ActionRadius);
    protected override ActionColorsToken ActionColors(ActionRole role) => role switch
    {
        ActionRole.Primary => new(Tokens.Accent, Tokens.OnAccent, Tokens.Accent, Tokens.HighContrast ? 1 : 0),
        ActionRole.Secondary => new(Tokens.SecondaryContainer, Tokens.OnSecondaryContainer, Tokens.SecondaryContainer, Tokens.HighContrast ? 1 : 0),
        ActionRole.Quiet => new(Microsoft.UI.Colors.Transparent, Tokens.Accent, Microsoft.UI.Colors.Transparent, 0),
        ActionRole.Destructive => new(Tokens.Error, Tokens.OnError, Tokens.Error, Tokens.HighContrast ? 1 : 0),
        _ => new(Microsoft.UI.Colors.Transparent, Tokens.Accent, Tokens.Outline, 1)
    };
    public override void ConfigureAccent(Control control)
    {
        base.ConfigureAccent(control);
        if (control is ToggleSwitch toggle)
        {
            toggle.Style = (Style)Resources["PyDeckToggleSwitchStyle"];
            toggle.MinHeight = Tokens.ControlHeight;
        }
    }
}
