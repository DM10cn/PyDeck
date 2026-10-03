using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace PimGui.App;

// An observation-only visual attached to our own templates. ButtonBase continues to
// own capture, cancellation, keyboard activation, commands and its automation peer.
internal static class MaterialRipple
{
    internal sealed record WaveSnapshot(long Id, Point Origin, double Radius, string Phase,
        double GrowthMilliseconds, double? FadeMilliseconds, double? FadeDelayMilliseconds);
    internal sealed record RippleSnapshot(bool Attached, bool Enabled, bool HasHost, int ActiveCount,
        long CreatedCount, int HandlerSets, double Width, double Height, CornerRadius Clip,
        IReadOnlyList<WaveSnapshot> Waves);

    private static readonly ConditionalWeakTable<Control, Registration> registrations = new();

    public static void Configure(Control control, bool enabled)
    {
        if (!registrations.TryGetValue(control, out var registration))
        {
            registration = new(control);
            registrations.Add(control, registration);
        }
        registration.SetEnabled(enabled);
    }

    public static void Detach(Control control)
    {
        if (!registrations.TryGetValue(control, out var registration)) return;
        registrations.Remove(control);
        registration.Dispose();
    }

    // The probe uses exactly the same begin/end paths as routed input. Neither path
    // invokes Click, changes IsPressed/IsChecked or executes the button's command.
    internal static long BeginForTesting(Control control, Point? origin = null)
        => registrations.TryGetValue(control, out var registration) ? registration.Begin(origin) : 0;
    internal static void EndForTesting(Control control, long id, bool canceled = false)
    {
        if (registrations.TryGetValue(control, out var registration)) registration.End(id, canceled);
    }
    internal static RippleSnapshot GetSnapshot(Control control)
        => registrations.TryGetValue(control, out var registration) ? registration.Snapshot()
            : new(false, false, false, 0, 0, 0, 0, 0, new(), []);

    private sealed class Wave
    {
        public required long Id;
        public required Point Origin;
        public required double Radius;
        public required ShapeVisual Visual;
        public required CompositionEllipseGeometry Geometry;
        public required CompositionSpriteShape Shape;
        public required CompositionColorBrush Brush;
        public required Vector2KeyFrameAnimation Growth;
        public required CompositionEasingFunction Easing;
        public required long Started;
        public ScalarKeyFrameAnimation? Fade;
        public CompositionScopedBatch? Batch;
        public TypedEventHandler<object, CompositionBatchCompletedEventArgs>? Completed;
        public string Phase = "held";
    }

    private sealed class Registration : IDisposable
    {
        private const int MaximumWaves = 3;
        private const double GrowthMilliseconds = 520;
        private const double FadeMilliseconds = 280;
        private readonly Control control;
        private readonly List<Wave> waves = [];
        private readonly Dictionary<uint, long> pointers = [];
        private readonly Dictionary<VirtualKey, long> keys = [];
        private readonly PointerEventHandler pressed;
        private readonly PointerEventHandler released;
        private readonly PointerEventHandler canceled;
        private readonly PointerEventHandler captureLost;
        private readonly KeyEventHandler keyDown;
        private readonly KeyEventHandler keyUp;
        private readonly long templateToken;
        private readonly long styleToken;
        private readonly long foregroundToken;
        private readonly long enabledToken;
        private Grid? host;
        private Border? surface;
        private long cornerToken;
        private ContainerVisual? layer;
        private RectangleClip? clip;
        private bool enabled;
        private bool disposed;
        private bool resolveQueued;
        private long created;
        private CornerRadius clipCorners;

        public Registration(Control control)
        {
            this.control = control;
            pressed = OnPressed; released = OnReleased; canceled = OnCanceled; captureLost = OnCaptureLost;
            keyDown = OnKeyDown; keyUp = OnKeyUp;
            control.AddHandler(UIElement.PointerPressedEvent, pressed, true);
            control.AddHandler(UIElement.PointerReleasedEvent, released, true);
            control.AddHandler(UIElement.PointerCanceledEvent, canceled, true);
            control.AddHandler(UIElement.PointerCaptureLostEvent, captureLost, true);
            control.AddHandler(UIElement.KeyDownEvent, keyDown, true);
            control.AddHandler(UIElement.KeyUpEvent, keyUp, true);
            control.Loaded += OnLoaded;
            control.Unloaded += OnUnloaded;
            control.LostFocus += OnLostFocus;
            templateToken = control.RegisterPropertyChangedCallback(Control.TemplateProperty, OnTemplateChanged);
            styleToken = control.RegisterPropertyChangedCallback(FrameworkElement.StyleProperty, OnTemplateChanged);
            foregroundToken = control.RegisterPropertyChangedCallback(Control.ForegroundProperty, OnForegroundChanged);
            enabledToken = control.RegisterPropertyChangedCallback(Control.IsEnabledProperty, OnEnabledChanged);
        }

        public void SetEnabled(bool value)
        {
            if (disposed) return;
            enabled = value;
            if (!enabled) ClearWaves();
            QueueResolve();
        }

        private void QueueResolve()
        {
            if (resolveQueued || disposed || !control.IsLoaded) return;
            resolveQueued = true;
            // Style changes can be in native template invalidation. Do not force an
            // ApplyTemplate or layout pass from that stack (or from TrackMotion).
            if (!control.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                resolveQueued = false;
                if (!disposed && control.IsLoaded) ResolveHost();
            })) resolveQueued = false;
        }

        private void OnLoaded(object sender, RoutedEventArgs args) => QueueResolve();
        private void OnUnloaded(object sender, RoutedEventArgs args) => DisconnectHost();
        private void OnEnabledChanged(DependencyObject sender, DependencyProperty property)
        {
            // Follow the effective dependency property immediately. The public
            // IsEnabledChanged notification can arrive after the disabled value.
            if (!control.IsEnabled) ClearWaves();
        }
        private void OnLostFocus(object sender, RoutedEventArgs args)
        {
            foreach (var id in keys.Values.ToArray()) End(id, true);
            keys.Clear();
        }
        private void OnTemplateChanged(DependencyObject sender, DependencyProperty property)
        {
            DisconnectHost();
            QueueResolve();
        }
        private void OnForegroundChanged(DependencyObject sender, DependencyProperty property)
        {
            // A shared navigation control can change its role or palette in place.
            // Read the current brush; never retain a Palette or DesignComponents.
            foreach (var wave in waves) wave.Brush.Color = CurrentColor();
        }

        private static Grid? FindHost(DependencyObject parent)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is Grid { Name: "RippleHost" } found) return found;
                // A button's content may itself contain controls. Their templates own
                // their own effects and must never become this control's host.
                if (child is Control) continue;
                if (FindHost(child) is { } nested) return nested;
            }
            return null;
        }

        private void ResolveHost()
        {
            if (disposed || !control.IsLoaded) return;
            var next = FindHost(control);
            if (ReferenceEquals(host, next)) { UpdateClip(); return; }
            DisconnectHost();
            if (next is null) return;
            host = next;
            if (VisualTreeHelper.GetParent(host) is Grid root)
                surface = root.Children.OfType<Border>().FirstOrDefault(border => border.Name == "RippleSurface" || border.Name == "ButtonBorder");
            // Only a template with an explicit surface and reserved host participates.
            if (surface is null) { host = null; return; }
            // Realization only discovers our XAML slot. Most virtualized action
            // buttons are never pressed, so they do not need composition objects.
            host.SizeChanged += OnHostSizeChanged;
            host.Unloaded += OnHostUnloaded;
            cornerToken = surface.RegisterPropertyChangedCallback(Border.CornerRadiusProperty, OnCornerChanged);
            UpdateClip();
        }

        private void EnsureLayer()
        {
            if (host is null || layer is not null) return;
            var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
            layer = compositor.CreateContainerVisual();
            clip = compositor.CreateRectangleClip();
            layer.Clip = clip;
            ElementCompositionPreview.SetElementChildVisual(host, layer);
            UpdateClip();
        }

        private void OnHostSizeChanged(object sender, SizeChangedEventArgs args)
        {
            ClearWaves();
            UpdateClip();
        }
        private void OnHostUnloaded(object sender, RoutedEventArgs args) => DisconnectHost();
        private void OnCornerChanged(DependencyObject sender, DependencyProperty property) => UpdateClip();

        private void UpdateClip()
        {
            if (host is null || surface is null) return;
            var width = (float)Math.Max(0, host.ActualWidth);
            var height = (float)Math.Max(0, host.ActualHeight);
            var corners = surface.CornerRadius;
            var scale = 1d;
            void Fit(double available, double sum) { if (sum > 0) scale = Math.Min(scale, available / sum); }
            Fit(width, corners.TopLeft + corners.TopRight); Fit(width, corners.BottomLeft + corners.BottomRight);
            Fit(height, corners.TopLeft + corners.BottomLeft); Fit(height, corners.TopRight + corners.BottomRight);
            clipCorners = new(corners.TopLeft * scale, corners.TopRight * scale, corners.BottomRight * scale, corners.BottomLeft * scale);
            if (layer is null || clip is null) return;
            layer.Size = new(width, height);
            clip.Left = 0; clip.Top = 0; clip.Right = width; clip.Bottom = height;
            clip.TopLeftRadius = new((float)clipCorners.TopLeft);
            clip.TopRightRadius = new((float)clipCorners.TopRight);
            clip.BottomRightRadius = new((float)clipCorners.BottomRight);
            clip.BottomLeftRadius = new((float)clipCorners.BottomLeft);
        }

        private Color CurrentColor() => control.Foreground is SolidColorBrush brush ? brush.Color : Microsoft.UI.Colors.Transparent;

        public long Begin(Point? requestedOrigin)
        {
            if (disposed || !enabled || !control.IsEnabled || !control.IsLoaded) return 0;
            ResolveHost();
            if (host is null || host.ActualWidth <= 0 || host.ActualHeight <= 0) return 0;
            EnsureLayer();
            if (layer is null) return 0;
            while (waves.Count >= MaximumWaves) Remove(waves[0]);
            var width = host.ActualWidth; var height = host.ActualHeight;
            var origin = requestedOrigin ?? new Point(width / 2, height / 2);
            origin = new(Math.Clamp(origin.X, 0, width), Math.Clamp(origin.Y, 0, height));
            // Both radii share one value: a circle, even for wide action buttons.
            var dx = Math.Max(origin.X, width - origin.X); var dy = Math.Max(origin.Y, height - origin.Y);
            var radius = Math.Sqrt(dx * dx + dy * dy);
            var compositor = layer.Compositor;
            var geometry = compositor.CreateEllipseGeometry();
            geometry.Center = new((float)origin.X, (float)origin.Y);
            geometry.Radius = new(0);
            var brush = compositor.CreateColorBrush(CurrentColor());
            var shape = compositor.CreateSpriteShape(geometry); shape.FillBrush = brush;
            var visual = compositor.CreateShapeVisual();
            visual.Size = new((float)width, (float)height); visual.Opacity = .12f;
            visual.Shapes.Add(shape);
            layer.Children.InsertAtTop(visual);
            var growth = compositor.CreateVector2KeyFrameAnimation();
            // Leave the circular front visible for more of the gesture. A strongly
            // front-loaded deceleration looked like an immediate whole-button flash.
            var easing = compositor.CreateCubicBezierEasingFunction(new(.2f, 0), new(.4f, 1));
            growth.Duration = TimeSpan.FromMilliseconds(GrowthMilliseconds);
            growth.InsertKeyFrame(0, new(0));
            growth.InsertKeyFrame(1, new((float)radius), easing);
            geometry.StartAnimation(nameof(CompositionEllipseGeometry.Radius), growth);
            var wave = new Wave { Id = ++created, Origin = origin, Radius = radius, Visual = visual, Geometry = geometry,
                Shape = shape, Brush = brush, Growth = growth, Easing = easing, Started = Stopwatch.GetTimestamp() };
            waves.Add(wave);
            return wave.Id;
        }

        public void End(long id, bool canceled)
        {
            var wave = waves.FirstOrDefault(item => item.Id == id);
            if (wave is null || wave.Phase != "held") return;
            wave.Phase = canceled ? "canceled" : "released";
            var compositor = wave.Visual.Compositor;
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.Duration = TimeSpan.FromMilliseconds(canceled ? 80 : FadeMilliseconds);
            var elapsed = Stopwatch.GetElapsedTime(wave.Started).TotalMilliseconds;
            // A short tap may release before its circle has grown. Let composition
            // finish that expansion before fading; this never delays native Click.
            fade.DelayTime = TimeSpan.FromMilliseconds(canceled ? 0 : Math.Max(0, GrowthMilliseconds - elapsed));
            fade.InsertKeyFrame(0, .12f); fade.InsertKeyFrame(1, 0);
            wave.Fade = fade;
            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation); wave.Batch = batch;
            wave.Completed = (_, _) => control.DispatcherQueue.TryEnqueue(() => Remove(wave));
            batch.Completed += wave.Completed;
            wave.Visual.StartAnimation(nameof(Visual.Opacity), fade);
            batch.End();
        }

        private void OnPressed(object sender, PointerRoutedEventArgs args)
        {
            if (disposed || !enabled || !control.IsEnabled || !control.IsLoaded) return;
            var point = args.GetCurrentPoint(control);
            // Mouse activation is defined by the left-button bit. Contact is used
            // only for touch/pen, so a platform's mouse contact reporting cannot
            // suppress a valid click's visual feedback.
            if (point.PointerDeviceType == PointerDeviceType.Mouse ? !point.Properties.IsLeftButtonPressed : !point.IsInContact) return;
            if (pointers.ContainsKey(point.PointerId)) return;
            // Host and control occupy the same template slot; obtain the point in the
            // host itself so padding/content alignment cannot shift the click origin.
            ResolveHost();
            var id = Begin(host is null ? point.Position : args.GetCurrentPoint(host).Position);
            if (id != 0) pointers[point.PointerId] = id;
        }
        private void OnReleased(object sender, PointerRoutedEventArgs args)
        {
            if (pointers.Remove(args.Pointer.PointerId, out var id)) End(id, false);
        }
        private void OnCanceled(object sender, PointerRoutedEventArgs args)
        {
            if (pointers.Remove(args.Pointer.PointerId, out var id)) End(id, true);
        }
        private void OnCaptureLost(object sender, PointerRoutedEventArgs args)
        {
            if (pointers.Remove(args.Pointer.PointerId, out var id)) End(id, args.GetCurrentPoint(control).IsInContact);
        }
        private void OnKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (args.Key is not (VirtualKey.Space or VirtualKey.Enter) || args.KeyStatus.WasKeyDown || keys.ContainsKey(args.Key)) return;
            var id = Begin(null);
            if (id != 0) keys[args.Key] = id;
        }
        private void OnKeyUp(object sender, KeyRoutedEventArgs args)
        {
            if (keys.Remove(args.Key, out var id)) End(id, false);
        }

        private void Remove(Wave wave)
        {
            if (!waves.Remove(wave)) return;
            foreach (var key in pointers.Where(pair => pair.Value == wave.Id).Select(pair => pair.Key).ToArray()) pointers.Remove(key);
            foreach (var key in keys.Where(pair => pair.Value == wave.Id).Select(pair => pair.Key).ToArray()) keys.Remove(key);
            layer?.Children.Remove(wave.Visual);
            wave.Geometry.StopAnimation(nameof(CompositionEllipseGeometry.Radius));
            wave.Visual.StopAnimation(nameof(Visual.Opacity));
            if (wave.Batch is { } batch)
            {
                if (wave.Completed is not null) batch.Completed -= wave.Completed;
                batch.Dispose();
            }
            wave.Completed = null;
            wave.Fade?.Dispose(); wave.Growth.Dispose(); wave.Easing.Dispose();
            wave.Visual.Dispose(); wave.Shape.Dispose(); wave.Geometry.Dispose(); wave.Brush.Dispose();
        }
        private void ClearWaves()
        {
            foreach (var wave in waves.ToArray()) Remove(wave);
            pointers.Clear(); keys.Clear();
        }
        private void DisconnectHost()
        {
            ClearWaves();
            if (surface is not null) surface.UnregisterPropertyChangedCallback(Border.CornerRadiusProperty, cornerToken);
            if (host is not null)
            {
                host.SizeChanged -= OnHostSizeChanged; host.Unloaded -= OnHostUnloaded;
                if (layer is not null) ElementCompositionPreview.SetElementChildVisual(host, null);
            }
            layer?.Dispose(); clip?.Dispose(); layer = null; clip = null; host = null; surface = null;
        }
        public RippleSnapshot Snapshot() => new(true, enabled && control.IsEnabled, host is not null, waves.Count, created,
            1, host?.ActualWidth ?? 0, host?.ActualHeight ?? 0, clipCorners,
            waves.Select(wave => new WaveSnapshot(wave.Id, wave.Origin, wave.Radius, wave.Phase,
                wave.Growth.Duration.TotalMilliseconds, wave.Fade?.Duration.TotalMilliseconds,
                wave.Fade?.DelayTime.TotalMilliseconds)).ToArray());

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            DisconnectHost();
            control.RemoveHandler(UIElement.PointerPressedEvent, pressed);
            control.RemoveHandler(UIElement.PointerReleasedEvent, released);
            control.RemoveHandler(UIElement.PointerCanceledEvent, canceled);
            control.RemoveHandler(UIElement.PointerCaptureLostEvent, captureLost);
            control.RemoveHandler(UIElement.KeyDownEvent, keyDown);
            control.RemoveHandler(UIElement.KeyUpEvent, keyUp);
            control.Loaded -= OnLoaded; control.Unloaded -= OnUnloaded;
            control.LostFocus -= OnLostFocus;
            control.UnregisterPropertyChangedCallback(Control.TemplateProperty, templateToken);
            control.UnregisterPropertyChangedCallback(FrameworkElement.StyleProperty, styleToken);
            control.UnregisterPropertyChangedCallback(Control.ForegroundProperty, foregroundToken);
            control.UnregisterPropertyChangedCallback(Control.IsEnabledProperty, enabledToken);
        }
    }
}
