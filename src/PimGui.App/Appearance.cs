using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PimGui.Core;
using Windows.UI.ViewManagement;
using System.Runtime.InteropServices;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private readonly UISettings systemUi = new();
    private bool closed;
    private string backdropKind = "Solid";
    private bool EffectsRequested => BackdropPolicy.RequestsEffects(preferences, systemUi.AdvancedEffectsEnabled, IsHighContrast);
    // AccessibilitySettings.HighContrastChanged requires a UWP CoreWindow. This unpackaged desktop window
    // reads the desktop flag and observes UISettings.ColorValuesChanged instead.
    [StructLayout(LayoutKind.Sequential)]
    private struct HighContrastInfo { public uint Size; public uint Flags; public nint DefaultScheme; }
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadSystemParameters(uint action, uint parameter, ref HighContrastInfo value, uint flags);
    private static bool IsHighContrast
    {
        get
        {
            var info = new HighContrastInfo { Size = (uint)Marshal.SizeOf<HighContrastInfo>() };
            return !ReadSystemParameters(0x0042, info.Size, ref info, 0) || (info.Flags & 1) != 0;
        }
    }

    private void InitializeSystemAppearance()
    {
        systemUi.AdvancedEffectsEnabledChanged += OnEffectsChanged;
        systemUi.ColorValuesChanged += OnEffectsChanged;
        Closed += (_, _) =>
        {
            closed = true;
            systemUi.AdvancedEffectsEnabledChanged -= OnEffectsChanged;
            systemUi.ColorValuesChanged -= OnEffectsChanged;
            SystemBackdrop = null;
        };
    }
    private void OnEffectsChanged(UISettings sender, object args) => QueueAppearance();
    private void QueueAppearance() => DispatcherQueue.TryEnqueue(() => { if (initialized && !closed) ApplyAppearance(); });

    private void ApplyBackdrop()
    {
        var requested = BackdropPolicy.Resolve(preferences, systemUi.AdvancedEffectsEnabled, IsHighContrast,
            MicaController.IsSupported(), DesktopAcrylicController.IsSupported());
        // Keep the native controller attached across language, theme and preference changes.
        // Replacing it on every save tears down the composition target while the new one connects.
        if (requested == backdropKind && (requested switch
            { "Mica" => SystemBackdrop is MicaBackdrop, "Acrylic" => SystemBackdrop is DesktopAcrylicBackdrop, _ => SystemBackdrop is null })) return;
        SystemBackdrop = null;
        backdropKind = requested;
        // Native backdrops retain Windows accessibility, power, activation and hardware fallback policies.
        SystemBackdrop = requested switch { "Mica" => new MicaBackdrop(), "Acrylic" => new DesktopAcrylicBackdrop(), _ => null };
    }

    private DesignTokens AccessibleTokens(DesignTokens tokens)
    {
        if (!IsHighContrast) return tokens;
        var background = systemUi.UIElementColor(UIElementType.Window);
        var foreground = systemUi.UIElementColor(UIElementType.WindowText);
        var highlight = systemUi.UIElementColor(UIElementType.Highlight);
        return tokens with { Shell = background, Surface = background, Card = background, Hero = background, ControlFill = background,
            Text = foreground, Muted = foreground, Line = foreground, Green = foreground,
            Accent = highlight, OnAccent = systemUi.UIElementColor(UIElementType.HighlightText),
            AccentContainer = background, NavigationSelected = background, CardBorder = 1 };
    }

    private Brush PopupBrush() => EffectsRequested ? new AcrylicBrush
    {
        TintColor = SolidCardColor, FallbackColor = SolidCardColor, TintOpacity = 0.8, TintLuminosityOpacity = 0.96
    } : Palette.Brush(palette.Card);

    private Windows.UI.Color SolidCardColor => Windows.UI.Color.FromArgb(255, palette.Card.R, palette.Card.G, palette.Card.B);

    private void ApplyPopupResources()
    {
        // Native control states consume the same accent roles as our custom controls.
        foreach (var key in new[] { "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
            "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush", "SystemControlHighlightAccentBrush" })
            Root.Resources[key] = Palette.Brush(palette.Accent);
        foreach (var key in new[] { "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush" })
            Root.Resources[key] = Palette.Brush(palette.OnAccent);
        // Cover the native popup controls too: Material and Off must not retain an implicit Acrylic brush.
        foreach (var key in new[] { "ComboBoxDropDownBackground", "FlyoutPresenterBackground", "MenuFlyoutPresenterBackground",
            "ToolTipBackground", "ToolTipBackgroundBrush", "AcrylicBackgroundFillColorDefaultBrush", "AcrylicInAppFillColorDefaultBrush", "DesktopAcrylicTransparentBrush" })
            Root.Resources[key] = PopupBrush();
    }

    private MenuFlyout RuntimeMenu()
    {
        var style = new Style(typeof(MenuFlyoutPresenter));
        style.Setters.Add(new Setter(Control.BackgroundProperty, PopupBrush()));
        style.Setters.Add(new Setter { Property = MenuFlyoutPresenter.SystemBackdropProperty,
            Value = EffectsRequested && DesktopAcrylicController.IsSupported() ? new DesktopAcrylicBackdrop() : null });
        // Setting an opaque presenter prevents a native flyout backdrop from showing through in Material / Off.
        return new MenuFlyout { MenuFlyoutPresenterStyle = style, SystemBackdrop = EffectsRequested && DesktopAcrylicController.IsSupported() ? new DesktopAcrylicBackdrop() : null };
    }

    private string BackdropDescription()
    {
        if (!palette.Tokens.SupportsEffects) return "Transparency is unavailable in Material 3 Expressive. Your Fluent preference is saved.";
        if (IsHighContrast) return "Solid background · Windows contrast theme is active.";
        if (preferences.Transparency == "Off") return "Solid background · transparency is off.";
        if (preferences.Transparency == "System" && !systemUi.AdvancedEffectsEnabled) return "Solid background · following your Windows setting.";
        if (backdropKind == "Solid") return T("{0} is unavailable on this device. A solid background is used.", T(preferences.Backdrop));
        return T("{0} requested. Windows may use a solid background for system preferences, accessibility, or power saving.", T(backdropKind));
    }
}
