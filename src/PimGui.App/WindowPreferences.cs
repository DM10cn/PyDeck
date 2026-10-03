using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Color = Windows.UI.Color;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private NativeTrayIcon? trayIcon;
    private NativeBorderlessFrame? borderlessFrame;
    private bool explicitWindowExit;
    private bool savingBeforeExit;
    private bool exitAfterSettingsFlush;
    private bool OledBlackActive => preferences.OledBlack && Root.ActualTheme == ElementTheme.Dark && !IsHighContrast;

    private void InitializeWindowPreferences()
    {
        // The preference selects native chrome or a completely borderless window.
        // Apply it once at startup so changing the setting cannot move a live editor.
        SetTitleBar(null);
        ExtendsContentIntoTitleBar = false;
        TitleBar.Visibility = Visibility.Collapsed;
        Root.RowDefinitions[0].Height = new GridLength(0);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.SetBorderAndTitleBar(preferences.UseSystemTitleBar, preferences.UseSystemTitleBar);
        if (!preferences.UseSystemTitleBar)
            borderlessFrame = new NativeBorderlessFrame(WinRT.Interop.WindowNative.GetWindowHandle(this));
        Closed += (_, _) =>
        {
            trayIcon?.Dispose(); trayIcon = null;
            borderlessFrame?.Dispose(); borderlessFrame = null;
        };
    }

    private void ApplyWindowCaptionColors()
    {
        // Use the current presenter: the saved title-bar preference takes effect
        // only after restart and may already differ from this window's chrome.
        if (AppWindow.Presenter is not OverlappedPresenter { HasTitleBar: true } ||
            !AppWindowTitleBar.IsCustomizationSupported()) return;

        // With ExtendsContentIntoTitleBar=false Windows ignores alpha. In
        // particular, Colors.Transparent becomes white, not the shell behind it.
        // Set every state together; null hands contrast themes back to Windows.
        static Color Opaque(Color color) => Color.FromArgb(255, color.R, color.G, color.B);
        var titleBar = AppWindow.TitleBar;
        var systemColors = palette.Tokens.HighContrast;
        Color? background = systemColors ? null : Opaque(palette.Shell);
        Color? foreground = systemColors ? null : Opaque(palette.Text);
        Color? inactiveForeground = systemColors ? null : Opaque(palette.Muted);
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = inactiveForeground;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = inactiveForeground;
        titleBar.ButtonHoverBackgroundColor = systemColors ? null
            : DesignComponents.Mix(palette.Shell, palette.Text, palette.Tokens.HoverStateOpacity);
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = systemColors ? null
            : DesignComponents.Mix(palette.Shell, palette.Text, palette.Tokens.PressedStateOpacity);
        titleBar.ButtonPressedForegroundColor = foreground;
    }

    private DesignTokens ApplyWindowPreferenceTokens(DesignTokens tokens)
    {
        if (preferences.UseSystemFont) tokens = tokens with { FontFamily = FontFamily.XamlAutoFontFamily.Source };
        // Keep container colors and text contrast intact; only the large canvas is black.
        return OledBlackActive ? tokens with { Shell = Colors.Black, Surface = Colors.Black, Background = Colors.Black } : tokens;
    }

    private void ApplyWindowPreferenceFonts()
    {
        var font = new FontFamily(palette.Tokens.FontFamily);
        Root.Resources["ContentControlThemeFontFamily"] = font;
        foreach (var label in Descendants(Root).OfType<TextBlock>().Where(label => label.Tag is "ShellLabel" or "NavigationLabel"))
            label.FontFamily = font;
        foreach (var label in new[] { ConnectionText, StatusText, StyleStatus, OperationTitleText, OperationPhaseText })
            label.FontFamily = font;
    }

    private bool TryApplyClosePreference()
    {
        // Probe windows must terminate, even when testing a saved "close to tray" choice.
        if (smokeDirectory is not null || explicitWindowExit || preferences.CloseBehavior == "Exit") return false;
        if (preferences.CloseBehavior == "Tray")
        {
            try
            {
                trayIcon ??= new NativeTrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this),
                    Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"),
                    RestoreFromTray, ExitFromTray, () => T("Open PyDeck"), () => T("Exit PyDeck"));
                if (trayIcon.Show()) { AppWindow.Hide(); return true; }
            }
            catch (Exception ex) { ShowError(ex); }
            // A missing Explorer notification area must never leave an unreachable app.
            Notify("The notification area is unavailable. PyDeck was minimized instead.", InfoBarSeverity.Warning);
        }
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Minimize();
        return true;
    }

    private void RestoreFromTray()
    {
        if (closed) return;
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        Activate();
        trayIcon?.Hide();
    }

    private void ExitFromTray()
    {
        // Reveal a possible in-progress-work warning; explicit exit still uses OnClosing.
        RestoreFromTray();
        explicitWindowExit = true;
        Close();
    }

    private async Task CloseAfterSettingsSavedAsync()
    {
        if (savingBeforeExit) return;
        savingBeforeExit = true;
        try
        {
            var previousSave = store.FlushAsync();
            if (previousSave.IsFaulted || previousSave.IsCanceled)
                await store.SaveAsync(preferences);
            // A new choice may be queued while a slow disk is flushing the previous one.
            // Drain through the current queue tail before asking WinUI to close again.
            do { await store.FlushAsync(); } while (!store.FlushAsync().IsCompletedSuccessfully);
            if (closed) return;
            exitAfterSettingsFlush = true;
            Close();
        }
        catch (Exception ex) { explicitWindowExit = false; exitAfterSettingsFlush = false; ShowError(ex); }
        finally { savingBeforeExit = false; }
    }
}
