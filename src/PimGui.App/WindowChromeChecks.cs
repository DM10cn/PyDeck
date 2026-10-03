using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Graphics;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckWindowChromeAsync(string directory, List<string> checks)
    {
        if (smokeDirectory is null) throw new IOException("Window chrome checks require an isolated smoke profile");
        var original = preferences;
        var originalSize = AppWindow.Size;
        var originalPosition = AppWindow.Position;
        var originalPage = page;
        var originalCategory = settingsCategory;
        var originalDetailSelected = settingsDetailSelected;
        var expectedCaption = original.UseSystemTitleBar;
        var measurements = new List<object>();
        var passed = false;
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var presenter = AppWindow.Presenter as OverlappedPresenter
            ?? throw new IOException("Window chrome requires the ordinary desktop presenter");

        void RequireChrome(string stage)
        {
            Root.UpdateLayout();
            var nativeStyle = unchecked((uint)ChromeProbeNative.GetWindowLong(hwnd, -16));
            var hasNativeCaption = (nativeStyle & 0x00C00000u) == 0x00C00000u;
            if (presenter.HasBorder != expectedCaption || presenter.HasTitleBar != expectedCaption || hasNativeCaption != expectedCaption)
                throw new IOException($"Wrong native chrome at {stage}: expected={expectedCaption}, border={presenter.HasBorder}, caption={presenter.HasTitleBar}, style={nativeStyle:X8}");
            if (ExtendsContentIntoTitleBar || TitleBar.Visibility != Visibility.Collapsed || Root.RowDefinitions[0].Height.Value != 0 || Root.RowDefinitions[0].ActualHeight != 0)
                throw new IOException("Custom title bar or reserved title-row space remains at " + stage);
            if (ChromeProbeNative.GetForegroundWindow() == hwnd)
                throw new IOException("Offscreen chrome probe unexpectedly took foreground focus at " + stage);
            measurements.Add(new { stage, presenter.HasBorder, presenter.HasTitleBar, nativeStyle = nativeStyle.ToString("X8"),
                hasNativeCaption, customTitleBar = TitleBar.Visibility.ToString(), titleRowHeight = Root.RowDefinitions[0].ActualHeight,
                width = AppWindow.Size.Width, height = AppWindow.Size.Height, x = AppWindow.Position.X, y = AppWindow.Position.Y });
        }

        try
        {
            // Wait until the initial native/XAML Loaded sequence has settled.
            await Task.Delay(250);
            RequireChrome("startup");
            // Use the wide settings layout: narrow navigation intentionally focuses
            // Back and would activate this offscreen fixture through keyboard focus.
            var scale = Root.XamlRoot.RasterizationScale;
            AppWindow.Resize(new SizeInt32((int)(1680 * scale), (int)(900 * scale)));
            await Task.Delay(80);
            RequireChrome("resized");
            // Maximize has no nonactivating overload and moves offscreen windows onto
            // a display. Leave it out of unattended probes that must preserve focus.
            presenter.Restore(false);
            AppWindow.Move(originalPosition);
            RequireChrome("restored without activation");

            OpenSettingsCategoryForSmoke("interface");
            var toggle = Descendants(SettingsElement("SettingsDetail")).OfType<ToggleSwitch>()
                .Single(item => AutomationProperties.GetName(item) == T("Use system title bar"));
            await WaitForSmokeConditionAsync(() => toggle.IsLoaded && toggle.IsEnabled, "System-titlebar toggle did not load");
            var togglePeer = FrameworkElementAutomationPeer.FromElement(toggle) ?? FrameworkElementAutomationPeer.CreatePeerForElement(toggle)
                ?? throw new IOException("System-titlebar toggle has no automation peer");
            ((IToggleProvider)togglePeer.GetPattern(PatternInterface.Toggle)).Toggle();
            await store.FlushAsync();
            await WaitForSmokeConditionAsync(() => pendingToggleSaves == 0 && preferences.UseSystemTitleBar == !expectedCaption && store.Load().UseSystemTitleBar == !expectedCaption,
                "System-titlebar choice did not persist for the next startup");
            RequireChrome("opposite preference saved; active frame unchanged");

            SelectSettingsCategory("about");
            Root.UpdateLayout();
            var exit = Descendants(SettingsElement("SettingsDetail")).OfType<Button>()
                .Single(item => AutomationProperties.GetName(item) == T("Exit PyDeck"));
            await WaitForSmokeConditionAsync(() => exit.IsLoaded && exit.IsEnabled && SettingsElementVisible(exit), "About page lacks its usable explicit exit action");
            var exitPeer = FrameworkElementAutomationPeer.FromElement(exit) ?? FrameworkElementAutomationPeer.CreatePeerForElement(exit);
            if (exitPeer?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider)
                throw new IOException("About exit action is not accessible through native Invoke semantics");
            RequireChrome("about exit action available");
            passed = true;
            checks.Add($"{ActiveDesign}: startup system-titlebar={expectedCaption}; native HWND caption and presenter border/titlebar agree; no custom header or empty title row; resize/restore preserve chrome; setting persists for next launch without changing the active frame; About exit remains accessible.");
        }
        finally
        {
            await store.FlushAsync();
            await File.WriteAllTextAsync(Path.Combine(directory, "window-chrome.json"), JsonSerializer.Serialize(new
            {
                passed, design = original.Design, startupUseSystemTitleBar = expectedCaption, measurements,
                scope = "Native presenter and HWND style assertions with real settings automation in an isolated offscreen process. Maximize and exit invocation are intentionally skipped to preserve foreground focus. No native-frame screenshot."
            }, new JsonSerializerOptions { WriteIndented = true }));
            preferences = original;
            await store.SaveAsync(original);
            AppWindow.Resize(originalSize); AppWindow.Move(originalPosition);
            settingsCategory = originalCategory; settingsDetailSelected = originalDetailSelected; Navigate(originalPage);
        }
    }

    private static class ChromeProbeNative
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        internal static extern int GetWindowLong(nint window, int index);

        [DllImport("user32.dll")]
        internal static extern nint GetForegroundWindow();
    }
}
