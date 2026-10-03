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
    private SystemAppearanceSnapshot? appliedSystemAppearance;
    private bool SystemAppearancePending { get; set; }
    private readonly record struct ContrastColors(Windows.UI.Color Background, Windows.UI.Color Foreground,
        Windows.UI.Color Highlight, Windows.UI.Color HighlightText);
    private readonly record struct SystemAppearanceSnapshot(ElementTheme Theme, bool Effects, string Backdrop, ContrastColors? Contrast);
    private bool EffectsRequested => !OledBlackActive && BackdropPolicy.RequestsEffects(preferences with { Design = ActiveDesign }, systemUi.AdvancedEffectsEnabled, IsHighContrast);
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
        systemUi.AnimationsEnabledChanged += OnAnimationsChanged;
        Closed += (_, _) =>
        {
            closed = true;
            Root.ActualThemeChanged -= OnActualThemeChanged;
            systemUi.AdvancedEffectsEnabledChanged -= OnEffectsChanged;
            systemUi.ColorValuesChanged -= OnEffectsChanged;
            systemUi.AnimationsEnabledChanged -= OnAnimationsChanged;
            SystemBackdrop = null;
        };
    }
    private void OnAnimationsChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (!initialized || closed) return;
            // This event is available at our Windows 10 2004 minimum. Update live
            // transition objects in place; appearance rebuilding would lose focus.
            palette.UpdateMotion(Root, systemUi.AnimationsEnabled);
        });
    private void OnEffectsChanged(UISettings sender, object args) => QueueAppearance();
    private void QueueAppearance() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!initialized || closed) return;
        var current = CaptureSystemAppearance();
        if (appliedSystemAppearance == current)
        {
            // Wallpaper-driven Windows accent changes do not affect either PyDeck palette.
            SystemAppearancePending = false;
            return;
        }
        // Contrast mode and contrast-scheme changes must remain immediately accessible.
        // Other system changes wait until navigation when a form or dialog is in progress.
        var contrastChanged = appliedSystemAppearance is { } previous && previous.Contrast != current.Contrast;
        if (!contrastChanged && !CanApplyPreparedAppearance())
        {
            SystemAppearancePending = true;
            return;
        }
        ApplyAppearance();
    });

    private SystemAppearanceSnapshot CaptureSystemAppearance()
    {
        var highContrast = IsHighContrast;
        var windowsEffects = systemUi.AdvancedEffectsEnabled;
        var activePreferences = preferences with { Design = ActiveDesign };
        ContrastColors? contrast = highContrast ? new(systemUi.UIElementColor(UIElementType.Window),
            systemUi.UIElementColor(UIElementType.WindowText), systemUi.UIElementColor(UIElementType.Highlight),
            systemUi.UIElementColor(UIElementType.HighlightText)) : null;
        return new(Root.ActualTheme, !OledBlackActive && BackdropPolicy.RequestsEffects(activePreferences, windowsEffects, highContrast),
            OledBlackActive ? "Solid" : BackdropPolicy.Resolve(activePreferences, windowsEffects, highContrast, MicaController.IsSupported(), DesktopAcrylicController.IsSupported()), contrast);
    }

    private void MarkSystemAppearanceApplied()
    {
        appliedSystemAppearance = CaptureSystemAppearance();
        SystemAppearancePending = false;
    }

    private void ApplyBackdrop()
    {
        var requested = OledBlackActive ? "Solid" : BackdropPolicy.Resolve(preferences with { Design = ActiveDesign }, systemUi.AdvancedEffectsEnabled, IsHighContrast,
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
        var highlightText = systemUi.UIElementColor(UIElementType.HighlightText);
        return tokens with { Shell = background, Surface = background, Card = background, Hero = background, ControlFill = background,
            Text = foreground, Muted = foreground, Line = foreground, Green = foreground,
            Accent = highlight, OnAccent = highlightText,
            AccentContainer = background, OnAccentContainer = foreground, NavigationSelected = highlight, NavigationForeground = highlightText, CardBorder = 1, HighContrast = true,
            Secondary = foreground, OnSecondary = background, SecondaryContainer = background, OnSecondaryContainer = foreground,
            Error = foreground, OnError = background, SurfaceHighest = background, Outline = foreground,
            Primary = highlight, OnPrimary = highlightText, PrimaryContainer = background, OnPrimaryContainer = foreground,
            Tertiary = foreground, OnTertiary = background, TertiaryContainer = background, OnTertiaryContainer = foreground,
            OnSurface = foreground, SurfaceVariant = background, OnSurfaceVariant = foreground,
            SurfaceDim = background, SurfaceBright = background, SurfaceContainerLowest = background, SurfaceContainerLow = background,
            SurfaceContainer = background, SurfaceContainerHigh = background, SurfaceContainerHighest = background, OutlineVariant = foreground,
            ErrorContainer = background, OnErrorContainer = foreground, InverseSurface = foreground, InverseOnSurface = background,
            InversePrimary = highlightText, SurfaceTint = background, Background = background, OnBackground = foreground,
            Shadow = background, Scrim = background, PrimaryFixed = highlight, PrimaryFixedDim = highlight,
            OnPrimaryFixed = highlightText, OnPrimaryFixedVariant = highlightText,
            SecondaryFixed = background, SecondaryFixedDim = background, OnSecondaryFixed = foreground, OnSecondaryFixedVariant = foreground,
            TertiaryFixed = background, TertiaryFixedDim = background, OnTertiaryFixed = foreground, OnTertiaryFixedVariant = foreground,
            DisabledTextOpacity = 1, DisabledContainerOpacity = 1 };
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
        if (OledBlackActive) return "Pure black background · OLED optimization is on. Your transparency preference is saved.";
        if (!palette.Tokens.SupportsEffects) return "Transparency is unavailable in Material 3 Expressive. Your Fluent preference is saved.";
        if (IsHighContrast) return "Solid background · Windows contrast theme is active.";
        if (preferences.Transparency == "Off") return "Solid background · transparency is off.";
        if (preferences.Transparency == "System" && !systemUi.AdvancedEffectsEnabled) return "Solid background · following your Windows setting.";
        if (backdropKind == "Solid") return T("{0} is unavailable on this device. A solid background is used.", T(preferences.Backdrop));
        return T("{0} requested. Windows may use a solid background for system preferences, accessibility, or power saving.", T(backdropKind));
    }
}
