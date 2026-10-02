using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;

namespace PimGui.App;

internal static class DesignProgress
{
    private sealed class ProgressState
    {
        public required DesignTokens Tokens;
        public Border? Root;
        public Brush? OriginalRootBackground;
    }
    private static readonly ConditionalWeakTable<ProgressBar, ProgressState> configured = new();

    public static void Configure(ProgressBar progress, DesignTokens tokens)
    {
        if (configured.TryGetValue(progress, out var state)) state.Tokens = tokens;
        else
        {
            state = new() { Tokens = tokens }; configured.Add(progress, state);
            progress.Loaded += (_, _) => ApplyNativeTrack(progress);
        }
        if (tokens.Design == "Material")
        {
            // Linear indicator roles match Material's Primary / SecondaryContainer.
            // AccessibleTokens maps Accent and Surface to system Highlight / Window.
            progress.Foreground = Palette.Brush(tokens.Accent);
            progress.Background = Palette.Brush(tokens.HighContrast ? tokens.Surface : tokens.SecondaryContainer);
            progress.MinHeight = 4;
            progress.CornerRadius = new(2);
        }
        else
        {
            // Restore SDK theme resources, including Windows accent/contrast behavior.
            progress.ClearValue(Control.ForegroundProperty);
            progress.ClearValue(Control.BackgroundProperty);
            progress.ClearValue(FrameworkElement.MinHeightProperty);
            progress.ClearValue(Control.CornerRadiusProperty);
        }
        ApplyNativeTrack(progress);
    }

    private static void ApplyNativeTrack(ProgressBar progress)
    {
        if (!configured.TryGetValue(progress, out var state)) return;
        progress.ApplyTemplate();
        var root = Descendants(progress).OfType<Border>().FirstOrDefault(part => part.Name == "ProgressBarRoot");
        if (root is null) return;
        if (!ReferenceEquals(state.Root, root)) { state.Root = root; state.OriginalRootBackground = root.Background; }
        // The real WinUI template binds all three indicator Rectangle.Fill values
        // to Foreground. Its indeterminate state hides ProgressBarTrack entirely.
        // Paint the existing rounded root behind it so both modes retain the tonal
        // track, while leaving SDK animation, Value sizing and UIA semantics intact.
        root.Background = state.Tokens.Design == "Material" ? progress.Background : state.OriginalRootBackground;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
