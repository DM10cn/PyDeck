using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System.Runtime.CompilerServices;

namespace PimGui.App;

// Keep the SDK ScrollBar template and its native range, paging, drag and UIA logic.
// Material changes paint and the existing named parts; it never handles scroll values.
internal sealed class DesignScrollStyles : DependencyObject
{
    private static readonly ConditionalWeakTable<Thumb, ThumbPaint> thumbs = new();
    private sealed class ThumbPaint
    {
        public required Rectangle Visual;
        public required Brush Idle;
        public required Brush Hover;
        public required Brush Drag;
        public bool Hovered;
        public bool Dragging;
        public void Apply() => Visual.Fill = Dragging ? Drag : Hovered ? Hover : Idle;
    }

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(DesignScrollStyles), new PropertyMetadata(false, EnabledChanged));
    public static bool GetEnabled(DependencyObject control) => (bool)control.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject control, bool value) => control.SetValue(EnabledProperty, value);

    public static void Install(ResourceDictionary resources, DesignTokens tokens)
    {
        // Keep these keys in both dictionaries while outgoing native controls unload.
        resources["PyDeckScrollThumbIdle"] = Palette.Brush(tokens.Outline);
        resources["PyDeckScrollThumbHover"] = Palette.Brush(tokens.Secondary);
        resources["PyDeckScrollThumbDrag"] = Palette.Brush(tokens.Accent);
        if (tokens.Design != "Material") return;
        var transparent = Palette.Brush(Microsoft.UI.Colors.Transparent);
        foreach (var key in new[]
        {
            "ScrollBarBackground", "ScrollBarBackgroundPointerOver", "ScrollBarBackgroundDisabled",
            "ScrollBarBorderBrush", "ScrollBarBorderBrushPointerOver", "ScrollBarBorderBrushDisabled",
            "ScrollBarTrackFill", "ScrollBarTrackFillPointerOver", "ScrollBarTrackFillDisabled",
            "ScrollBarTrackStroke", "ScrollBarTrackStrokePointerOver", "ScrollBarTrackStrokeDisabled",
            "ScrollBarThumbBorderBrush", "ScrollViewerScrollBarSeparatorBackground"
        }) resources[key] = transparent;
        foreach (var key in new[] { "ScrollBarThumbBackground", "ScrollBarPanningThumbBackground", "ScrollBarThumbFill" })
            resources[key] = resources["PyDeckScrollThumbIdle"];
        resources["ScrollBarThumbFillPointerOver"] = resources["PyDeckScrollThumbHover"];
        resources["ScrollBarThumbFillPressed"] = resources["PyDeckScrollThumbDrag"];
        resources["ScrollBarThumbFillDisabled"] = Palette.Brush(tokens.Muted);
        resources["ScrollBarPanningThumbBackgroundDisabled"] = Palette.Brush(tokens.Muted);
        resources["ScrollBarThumbStrokeThickness"] = 1.0;
        resources["ScrollViewerScrollBarMargin"] = new Thickness(0);
        var style = new Style(typeof(ScrollBar))
        {
            BasedOn = (Style)Application.Current.Resources["DefaultScrollBarStyle"]
        };
        style.Setters.Add(new Setter(EnabledProperty, true));
        resources[typeof(ScrollBar)] = style;
    }

    private static void EnabledChanged(DependencyObject control, DependencyPropertyChangedEventArgs args)
    {
        if (control is not ScrollBar bar || args.NewValue is not true) return;
        bar.Loaded += ScrollBarLoaded;
        if (bar.IsLoaded) Configure(bar);
    }

    private static void ScrollBarLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is ScrollBar bar) Configure(bar);
    }

    private static void Configure(ScrollBar bar)
    {
        bar.ApplyTemplate();
        // An inset rail stays in the page's reserved gutter in both indicator modes.
        bar.Margin = bar.Orientation == Orientation.Vertical ? new Thickness(0, 8, 4, 8) : new Thickness(8, 0, 8, 4);
        foreach (var part in Descendants(bar).OfType<FrameworkElement>())
        {
            if (part is RepeatButton button && part.Name is "VerticalSmallDecrease" or "VerticalSmallIncrease" or "HorizontalSmallDecrease" or "HorizontalSmallIncrease")
            {
                button.Visibility = Visibility.Collapsed;
                button.MinWidth = button.MinHeight = button.Width = button.Height = 0;
            }
            else if (part is Thumb thumb && part.Name is "VerticalThumb" or "HorizontalThumb")
            {
                // SDK expands 8 -> 12 dip and translates 2 -> 0. Leading alignment keeps
                // their center fixed instead of shifting the handle on hover/drag.
                if (part.Name == "VerticalThumb") thumb.HorizontalAlignment = HorizontalAlignment.Left;
                else thumb.VerticalAlignment = VerticalAlignment.Top;
                ConfigureThumb(thumb);
            }
            else if (part is Border indicator && part.Name is "VerticalPanningThumb" or "HorizontalPanningThumb")
            {
                indicator.CornerRadius = new(3);
                indicator.Margin = new(0);
                if (part.Name == "VerticalPanningThumb") { indicator.Width = 6; indicator.HorizontalAlignment = HorizontalAlignment.Center; }
                else { indicator.Height = 6; indicator.VerticalAlignment = VerticalAlignment.Center; }
            }
        }
    }

    private static void ConfigureThumb(Thumb thumb)
    {
        thumb.ApplyTemplate();
        var visual = Descendants(thumb).OfType<Rectangle>().FirstOrDefault(part => part.Name == "ThumbVisual");
        if (visual is null) return;
        visual.RadiusX = visual.RadiusY = 6;
        visual.StrokeThickness = 1;
        var resources = Application.Current.Resources;
        if (thumbs.TryGetValue(thumb, out var existing))
        {
            existing.Visual = visual;
            existing.Idle = (Brush)resources["PyDeckScrollThumbIdle"];
            existing.Hover = (Brush)resources["PyDeckScrollThumbHover"];
            existing.Drag = (Brush)resources["PyDeckScrollThumbDrag"];
            existing.Apply();
            return;
        }
        var paint = new ThumbPaint
        {
            Visual = visual,
            Idle = (Brush)resources["PyDeckScrollThumbIdle"],
            Hover = (Brush)resources["PyDeckScrollThumbHover"],
            Drag = (Brush)resources["PyDeckScrollThumbDrag"]
        };
        thumbs.Add(thumb, paint);
        thumb.PointerEntered += (_, _) => { paint.Hovered = true; paint.Apply(); };
        thumb.PointerExited += (_, _) => { paint.Hovered = false; paint.Apply(); };
        thumb.DragStarted += (_, _) => { paint.Dragging = true; paint.Apply(); };
        thumb.DragCompleted += (_, _) => { paint.Dragging = false; paint.Apply(); };
        paint.Apply();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
