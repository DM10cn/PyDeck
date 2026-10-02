using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PimGui.Core;
using Windows.UI;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckProgressColorsAsync(string directory)
    {
        if (smokeDirectory is null || busy || runningOperations.Count != 0) throw new IOException("Progress checks require an idle isolated profile");
        var original = preferences;
        var originalPage = page;
        IDisposable? work = null;
        PimOperation? operation = null;
        try
        {
            foreach (var design in new[] { "Material", "Fluent" })
            foreach (var theme in new[] { "Light", "Dark" })
            foreach (var seed in design == "Material" ? new[] { 0xff287c60u, 0xffb344aau } : new[] { 0xff287c60u })
            {
                var context = $"Progress/{design}/{theme}/{seed:X8}";
                ApplySmokePreferences(preferences with { Design = design, Theme = theme, Transparency = "Off", MaterialColorSource = "Custom", MaterialSeed = seed });
                Navigate("components"); Root.UpdateLayout();
                work = StartWork(WorkKind.Catalog);
                if (work is null) throw new IOException(context + ": Could not reserve isolated refresh lease");
                Root.UpdateLayout(); await Task.Delay(80);
                if (BusyProgress.Visibility != Visibility.Visible || !BusyProgress.IsIndeterminate)
                    throw new IOException(context + ": Refresh did not use the real indeterminate BusyProgress");
                CheckProgressParts(BusyProgress, palette.Tokens, context + "/refresh");
                await CaptureAsync(System.IO.Path.Combine(directory, $"progress-{design}-{theme}-{seed:X8}-refresh.png"), BusyProgress);

                operation = BeginOperation("Progress fixture", download: true);
                operation.Report(OperationPhase.Downloading, 40);
                UpdateOperationPanel(); Root.UpdateLayout(); await Task.Delay(80);
                if (BusyProgress.Visibility != Visibility.Collapsed || OperationProgressBar.IsIndeterminate || OperationProgressBar.Value != 40)
                    throw new IOException(context + ": Determinate operation progress semantics changed");
                CheckProgressParts(OperationProgressBar, palette.Tokens, context + "/determinate");
                var provider = new ProgressBarAutomationPeer(OperationProgressBar).GetPattern(PatternInterface.RangeValue) as IRangeValueProvider;
                if (provider is null || provider.Value != 40 || provider.Minimum != 0 || provider.Maximum != 100 || !provider.IsReadOnly)
                    throw new IOException(context + ": Native progress UIA range was lost");
                await CaptureAsync(System.IO.Path.Combine(directory, $"progress-{design}-{theme}-{seed:X8}-determinate.png"), OperationPanel);
                operation.Report(OperationPhase.Verifying);
                UpdateOperationPanel(); Root.UpdateLayout(); await Task.Delay(80);
                if (!OperationProgressBar.IsIndeterminate) throw new IOException(context + ": Unknown work acquired a fabricated percentage");
                CheckProgressParts(OperationProgressBar, palette.Tokens, context + "/indeterminate");
                if (design == "Material" && theme == "Dark" && seed == 0xff287c60u)
                {
                    // Synthetic accessible tokens verify system-role precedence without
                    // changing the user's Windows contrast setting.
                    var accessible = palette.Tokens with { HighContrast = true, Accent = Color.FromArgb(255, 255, 255, 0),
                        Surface = Color.FromArgb(255, 0, 0, 0), SecondaryContainer = Color.FromArgb(255, 255, 0, 255) };
                    new Palette(accessible).ConfigureProgress(OperationProgressBar);
                    CheckProgressParts(OperationProgressBar, accessible, context + "/contrast-roles");
                    palette.ConfigureProgress(OperationProgressBar);
                }
                FinishOperation(operation); operation = null;
                work.Dispose(); work = null; Root.UpdateLayout();
                if (BusyProgress.Visibility != Visibility.Collapsed || OperationPanel.Visibility != Visibility.Collapsed)
                    throw new IOException(context + ": Completed fixture left progress visible");
            }
        }
        finally
        {
            if (operation is not null) FinishOperation(operation);
            work?.Dispose();
            ApplySmokePreferences(original); Navigate(originalPage);
        }
    }

    private static void CheckProgressParts(ProgressBar progress, DesignTokens tokens, string context)
    {
        progress.ApplyTemplate(); progress.UpdateLayout();
        var parts = Descendants(progress).OfType<FrameworkElement>().ToArray();
        var root = parts.OfType<Border>().Single(part => part.Name == "ProgressBarRoot");
        var track = parts.OfType<Rectangle>().Single(part => part.Name == "ProgressBarTrack");
        var indicators = new[] { "DeterminateProgressBarIndicator", "IndeterminateProgressBarIndicator", "IndeterminateProgressBarIndicator2" }
            .Select(name => parts.OfType<Rectangle>().Single(part => part.Name == name)).ToArray();
        if (progress.IsTabStop) throw new IOException(context + ": Progress acquired an interactive tab stop");
        if (tokens.Design == "Fluent")
        {
            if (!ReferenceEquals(progress.ReadLocalValue(Control.ForegroundProperty), DependencyProperty.UnsetValue) ||
                !ReferenceEquals(progress.ReadLocalValue(Control.BackgroundProperty), DependencyProperty.UnsetValue) || root.Background is not null)
                throw new IOException(context + ": Material progress overrides leaked into native Fluent");
            return;
        }
        void ColorIs(Brush brush, Color expected, string role)
        {
            if (brush is not SolidColorBrush actual || actual.Color != expected)
                throw new IOException(context + ": Incorrect rendered " + role + " brush");
        }
        ColorIs(progress.Foreground, tokens.Accent, "primary");
        foreach (var indicator in indicators) ColorIs(indicator.Fill, tokens.Accent, indicator.Name);
        var expectedTrack = tokens.HighContrast ? tokens.Surface : tokens.SecondaryContainer;
        ColorIs(track.Fill, expectedTrack, "native track");
        ColorIs(root.Background, expectedTrack, "persistent indeterminate track");
        if (progress.MinHeight != 4 || progress.CornerRadius != new CornerRadius(2) || root.CornerRadius != new CornerRadius(2))
            throw new IOException(context + ": Material progress lost its four-dip round track");
    }
}
