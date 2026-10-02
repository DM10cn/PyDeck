using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;
using Windows.UI;

namespace PimGui.App;

// The SDK ComboBox owns popup positioning, editing, scrolling, keyboard and UIA.
// Only the item visuals are replaced. Its native Popup/PopupBorder/ScrollViewer/
// ItemsPresenter parts remain intact; no page or item click handlers emulate selection.
internal static class MaterialSelectorTemplates
{
    internal const double PopupRadius = 16;
    internal const double ItemRadius = 12;
    private sealed class SelectorState { public required DesignTokens Tokens; }
    private static readonly ConditionalWeakTable<ComboBox, SelectorState> configured = new();

    public static void Install(ResourceDictionary resources, DesignTokens tokens)
    {
        void Brush(string key, Color color) => resources[key] = Palette.Brush(color);
        var surface = tokens.HighContrast ? tokens.Card : tokens.SurfaceContainer;
        var disabled = tokens.HighContrast ? tokens.Muted : DesignComponents.Mix(surface, tokens.Text, tokens.DisabledTextOpacity);
        Brush("ComboBoxDropDownBackground", surface);
        Brush("ComboBoxDropDownForeground", tokens.Text);
        Brush("ComboBoxDropDownBorderBrush", tokens.HighContrast ? tokens.Outline : tokens.OutlineVariant);
        Brush("ComboBoxItemPillFillBrush", Microsoft.UI.Colors.Transparent);
        Brush("ComboBoxBackgroundFocused", tokens.ControlFill);
        Brush("ComboBoxBackgroundBorderBrushFocused", tokens.Accent);
        Brush("ComboBoxForegroundFocused", tokens.Text);
        Brush("ComboBoxForegroundFocusedPressed", tokens.Text);
        foreach (var suffix in new[] { "", "PointerOver", "Pressed", "Focused", "FocusedPressed", "Disabled" })
        {
            Brush("ComboBoxDropDownGlyphForeground" + suffix, suffix == "Disabled" ? disabled : tokens.Muted);
            Brush("ComboBoxPlaceHolderForeground" + suffix, suffix == "Disabled" ? disabled : tokens.Muted);
            Brush("ComboBoxHeaderForeground" + suffix, suffix == "Disabled" ? disabled : tokens.Text);
        }
        InstallPaletteResources(resources, tokens);
        resources["PyDeckSelectorPopupCornerRadius"] = new CornerRadius(PopupRadius);
        resources["ComboBoxDropdownBorderPadding"] = new Thickness(4);
        resources["ComboBoxDropdownContentMargin"] = new Thickness(0, 4, 0, 4);
        resources["ComboBoxDropdownBorderThickness"] = new Thickness(tokens.HighContrast ? 1 : 0);
        var item = (Style)XamlReader.Load(ItemStyle());
        item.BasedOn = (Style)Application.Current.Resources["DefaultComboBoxItemStyle"];
        // This dictionary is still being constructed. Outer style setters resolve
        // ThemeResource immediately, before the application can see these keys.
        item.Setters.Add(new Setter(Control.FontFamilyProperty, resources["PyDeckFontFamily"]));
        item.Setters.Add(new Setter(Control.ForegroundProperty, resources["PyDeckSelectorItemForeground"]));
        item.Setters.Add(new Setter(Control.BackgroundProperty, resources["PyDeckSelectorItemBackground"]));
        item.Setters.Add(new Setter(Control.FocusVisualPrimaryBrushProperty, resources["PyDeckSelectorFocus"]));
        item.Setters.Add(new Setter(Control.FocusVisualSecondaryBrushProperty, resources["PyDeckSelectorFocusContrast"]));
        resources["PyDeckMaterialSelectorItemStyle"] = item;
        resources[typeof(ComboBoxItem)] = item;
        // The base style is BasedOn DefaultComboBoxStyle; preserve every SDK template part.
        var selector = new Style(typeof(ComboBox)) { BasedOn = (Style)resources[typeof(ComboBox)] };
        selector.Setters.Add(new Setter(ItemsControl.ItemContainerStyleProperty, item));
        selector.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 9, 12, 9)));
        selector.Setters.Add(new Setter(Control.ForegroundProperty, Palette.Brush(tokens.Text)));
        selector.Setters.Add(new Setter(Control.BorderBrushProperty, Palette.Brush(tokens.Outline)));
        selector.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        selector.Setters.Add(new Setter(Control.UseSystemFocusVisualsProperty, true));
        selector.Setters.Add(new Setter(Control.FocusVisualPrimaryBrushProperty, Palette.Brush(tokens.Accent)));
        selector.Setters.Add(new Setter(Control.FocusVisualSecondaryBrushProperty, Palette.Brush(surface)));
        selector.Setters.Add(new Setter(ComboBoxHelper.KeepInteriorCornersSquareProperty, false));
        resources[typeof(ComboBox)] = selector;
    }

    // Outgoing templates may briefly outlive a dictionary replacement. Both designs
    // provide these private keys; only Material installs the item template itself.
    public static void InstallPaletteResources(ResourceDictionary resources, DesignTokens tokens)
    {
        void Brush(string key, Color color) => resources[key] = Palette.Brush(color);
        var surface = tokens.HighContrast ? tokens.Card : tokens.SurfaceContainer;
        var disabled = tokens.HighContrast ? tokens.Muted : DesignComponents.Mix(surface, tokens.Text, tokens.DisabledTextOpacity);
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled", "Selected", "SelectedUnfocused", "SelectedPointerOver", "SelectedPressed", "SelectedDisabled" })
        {
            var selected = state.StartsWith("Selected", StringComparison.Ordinal);
            var isDisabled = state.EndsWith("Disabled", StringComparison.Ordinal);
            var fill = selected ? tokens.SecondaryContainer : surface;
            var foreground = selected ? tokens.OnSecondaryContainer : tokens.Text;
            var opacity = state.EndsWith("PointerOver", StringComparison.Ordinal) ? tokens.HoverStateOpacity
                : state.EndsWith("Pressed", StringComparison.Ordinal) ? tokens.PressedStateOpacity : 0;
            if (isDisabled) { fill = selected ? DesignComponents.Mix(surface, tokens.Text, tokens.DisabledContainerOpacity) : surface; foreground = disabled; }
            else if (opacity > 0) fill = DesignComponents.Mix(fill, foreground, opacity);
            Brush("PyDeckSelectorItemBackground" + state, fill);
            Brush("PyDeckSelectorItemForeground" + state, foreground);
        }
        resources["PyDeckSelectorFocus"] = Palette.Brush(tokens.Accent);
        resources["PyDeckSelectorFocusContrast"] = Palette.Brush(surface);
    }

    public static void Configure(ComboBox selector, ResourceDictionary resources, DesignTokens tokens)
    {
        foreach (var resource in resources)
            if (resource.Key is string key && (key.StartsWith("ComboBox", StringComparison.Ordinal) || key.StartsWith("PyDeckSelector", StringComparison.Ordinal) || key == "OverlayCornerRadius"))
                selector.Resources[key] = resource.Value;
        selector.Resources["OverlayCornerRadius"] = new CornerRadius(PopupRadius);
        selector.ItemContainerStyle = (Style)resources["PyDeckMaterialSelectorItemStyle"];
        selector.CornerRadius = new(tokens.InputRadius);
        selector.Foreground = Palette.Brush(tokens.Text);
        selector.BorderBrush = Palette.Brush(tokens.Outline);
        selector.BorderThickness = new(1);
        selector.UseSystemFocusVisuals = true;
        selector.FocusVisualPrimaryBrush = Palette.Brush(tokens.Accent);
        selector.FocusVisualSecondaryBrush = Palette.Brush(tokens.Surface);
        ComboBoxHelper.SetKeepInteriorCornersSquare(selector, false);
        if (configured.TryGetValue(selector, out var state)) state.Tokens = tokens;
        else
        {
            state = new() { Tokens = tokens };
            configured.Add(selector, state);
            selector.Loaded += (_, _) => ApplyNativeParts(selector);
            selector.DropDownOpened += (_, _) => ApplyNativeParts(selector);
        }
        if (selector.IsLoaded) ApplyNativeParts(selector);
    }

    private static void ApplyNativeParts(ComboBox selector)
    {
        if (!configured.TryGetValue(selector, out var state)) return;
        selector.ApplyTemplate();
        var parts = Descendants(selector).OfType<FrameworkElement>().ToArray();
        // The SDK focus halo has a fixed seven-dip corner, independent of CornerRadius.
        // Shape it at component level without replacing the SDK template or input behavior.
        if (parts.OfType<Border>().FirstOrDefault(part => part.Name == "HighlightBackground") is { } focus)
        { focus.CornerRadius = new(state.Tokens.InputRadius); focus.Margin = new(0); }
        var popup = parts.OfType<Popup>().FirstOrDefault(part => part.Name == "Popup");
        if (popup is null && selector.IsDropDownOpen && selector.XamlRoot is not null)
            popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(selector.XamlRoot).FirstOrDefault(candidate => candidate.Child is not null &&
                Descendants(candidate.Child).OfType<ComboBoxItem>().Any(item => ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), selector)));
        if (popup?.Child is Border border)
        {
            border.CornerRadius = new(PopupRadius);
            border.Background = Palette.Brush(state.Tokens.HighContrast ? state.Tokens.Card : state.Tokens.SurfaceContainer);
            border.BorderBrush = Palette.Brush(state.Tokens.HighContrast ? state.Tokens.Outline : state.Tokens.OutlineVariant);
            border.BorderThickness = new(state.Tokens.HighContrast ? 1 : 0);
            border.Padding = new(4);
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static string ItemStyle()
    {
        var states = string.Join("\n", new[] { "Normal", "PointerOver", "Pressed", "Disabled", "Selected", "SelectedUnfocused", "SelectedPointerOver", "SelectedPressed", "SelectedDisabled" }.Select(state =>
        {
            var suffix = state == "Normal" ? "" : state;
            var selected = state.StartsWith("Selected", StringComparison.Ordinal);
            return $$"""
                <VisualState x:Name="{{state}}">
                  <VisualState.Setters>
                    <Setter Target="LayoutRoot.Background" Value="{ThemeResource PyDeckSelectorItemBackground{{suffix}}}" />
                    <Setter Target="ContentPresenter.Foreground" Value="{ThemeResource PyDeckSelectorItemForeground{{suffix}}}" />
                    <Setter Target="SelectionCheck.Foreground" Value="{ThemeResource PyDeckSelectorItemForeground{{suffix}}}" />
                    <Setter Target="SelectionCheck.Opacity" Value="{{(selected ? "1" : "0")}}" />
                  </VisualState.Setters>
                </VisualState>
                """;
        }));
        return $$"""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="ComboBoxItem">
              <Setter Property="MinHeight" Value="40" />
              <Setter Property="Padding" Value="12,8,12,8" />
              <Setter Property="HorizontalContentAlignment" Value="Stretch" />
              <Setter Property="VerticalContentAlignment" Value="Center" />
              <Setter Property="CornerRadius" Value="12" />
              <Setter Property="UseSystemFocusVisuals" Value="True" />
              <Setter Property="FocusVisualMargin" Value="2" />
              <Setter Property="Template">
                <Setter.Value>
                  <ControlTemplate TargetType="ComboBoxItem">
                    <Grid x:Name="LayoutRoot" Background="{TemplateBinding Background}" Margin="4,2,4,2"
                          CornerRadius="{TemplateBinding CornerRadius}" Control.IsTemplateFocusTarget="True">
                      <VisualStateManager.VisualStateGroups>
                        <VisualStateGroup x:Name="CommonStates">{{states}}</VisualStateGroup>
                        <VisualStateGroup x:Name="InputModeStates">
                          <VisualState x:Name="InputModeDefault" />
                          <VisualState x:Name="TouchInputMode"><VisualState.Setters><Setter Target="LayoutRoot.MinHeight" Value="48" /></VisualState.Setters></VisualState>
                          <VisualState x:Name="GameControllerInputMode"><VisualState.Setters><Setter Target="LayoutRoot.MinHeight" Value="48" /></VisualState.Setters></VisualState>
                        </VisualStateGroup>
                        <VisualStateGroup x:Name="FocusStates">
                          <VisualState x:Name="Focused" /><VisualState x:Name="PointerFocused" /><VisualState x:Name="Unfocused" />
                        </VisualStateGroup>
                      </VisualStateManager.VisualStateGroups>
                      <Grid.ColumnDefinitions><ColumnDefinition Width="*" /><ColumnDefinition Width="Auto" /></Grid.ColumnDefinitions>
                      <ContentPresenter x:Name="ContentPresenter" Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}"
                          ContentTransitions="{TemplateBinding ContentTransitions}" Foreground="{TemplateBinding Foreground}"
                          HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}"
                          Margin="{TemplateBinding Padding}" AutomationProperties.AccessibilityView="Raw" />
                      <FontIcon x:Name="SelectionCheck" Grid.Column="1" Glyph="&#xE73E;" FontFamily="{ThemeResource SymbolThemeFontFamily}" FontSize="16"
                          Margin="4,0,12,0" Opacity="0" Foreground="{ThemeResource PyDeckSelectorItemForeground}" VerticalAlignment="Center"
                          IsHitTestVisible="False" IsTextScaleFactorEnabled="False" AutomationProperties.AccessibilityView="Raw" />
                    </Grid>
                  </ControlTemplate>
                </Setter.Value>
              </Setter>
            </Style>
            """;
    }
}
