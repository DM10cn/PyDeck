using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System.Text.Json;
using Windows.Foundation;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckRippleInteractionAsync(string directory, List<string> checks)
    {
        if (smokeDirectory is null) throw new IOException("Ripple checks require an isolated smoke profile");
        await WaitForSmokeConditionAsync(() => Root.IsLoaded, "Ripple host window did not load");
        // Do not replace the presentation while the initial Loaded stack is active.
        await Task.Delay(250);
        Directory.CreateDirectory(directory);
        var original = preferences; var originalPage = page;
        var samples = new List<object>();
        Button? probe = null; StackPanel? fixture = null;
        var completed = false; string? error = null; var clicks = 0;
        MaterialRipple.RippleSnapshot Sample(string step)
        {
            var snapshot = MaterialRipple.GetSnapshot(probe!);
            samples.Add(new { step, snapshot, clicks });
            return snapshot;
        }
        async Task Ready(string context)
        {
            Root.UpdateLayout();
            await WaitForSmokeConditionAsync(() => probe!.IsLoaded && MaterialRipple.GetSnapshot(probe) is
                { Attached: true, HasHost: true, Width: > 0, Height: > 0 }, context);
        }
        static void Require(bool condition, string message)
        {
            if (!condition) throw new IOException("Ripple: " + message);
        }
        try
        {
            ApplySmokePreferences(preferences with { Design = "Material", Theme = "Dark", Language = "en-US", Transparency = "Off" });
            Navigate("components"); Root.UpdateLayout(); await Task.Delay(250);
            Require(!palette.Tokens.HighContrast, "geometry fixture requires ordinary Material colors; high contrast intentionally suppresses motion");
            probe = palette.Action("Ripple automation fixture", role: ActionRole.Primary);
            probe.Width = 280; probe.Height = 56;
            probe.HorizontalAlignment = HorizontalAlignment.Left;
            probe.Click += (_, _) => clicks++;
            fixture = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top, Margin = new(24) };
            fixture.Children.Add(probe); PageHost.Children.Add(fixture);
            await Ready("Ripple fixture did not acquire its real loaded template host");
            // Exercise four distinct corners that fit without radius normalization.
            probe.CornerRadius = new(20, 12, 8, 4); Root.UpdateLayout();
            palette.UpdateMotion(Root, true);
            var loaded = Sample("loaded");
            Require(loaded.Enabled && loaded.HandlerSets == 1, "loaded button lacks one enabled registration");

            foreach (var location in new[] { "center", "edge", "corner" })
            {
                var before = MaterialRipple.GetSnapshot(probe);
                Point? requested = location switch
                {
                    "edge" => new Point(0, before.Height / 2),
                    "corner" => new Point(0, 0),
                    _ => null
                };
                var id = MaterialRipple.BeginForTesting(probe, requested);
                var snapshot = Sample(location + "-held");
                Require(id > 0 && snapshot.ActiveCount == 1 && snapshot.CreatedCount == before.CreatedCount + 1,
                    location + " did not create exactly one wave");
                var wave = snapshot.Waves.Single();
                Require(wave.GrowthMilliseconds >= 480 && wave.GrowthMilliseconds <= 650,
                    "circular expansion is too abrupt or unreasonably slow");
                var expectedOrigin = requested ?? new Point(snapshot.Width / 2, snapshot.Height / 2);
                Require(Math.Abs(wave.Origin.X - expectedOrigin.X) < .01 && Math.Abs(wave.Origin.Y - expectedOrigin.Y) < .01,
                    location + " origin shifted from the requested host position");
                // Independently verify coverage and a tight boundary against all four
                // corners, rather than repeating the implementation's farthest-axis formula.
                var cornerDistances = new[] { new Point(), new Point(snapshot.Width, 0),
                    new Point(0, snapshot.Height), new Point(snapshot.Width, snapshot.Height) }
                    .Select(corner => Math.Pow(corner.X - wave.Origin.X, 2) + Math.Pow(corner.Y - wave.Origin.Y, 2)).ToArray();
                var radiusSquared = wave.Radius * wave.Radius;
                var tolerance = Math.Max(.001, radiusSquared * .00001);
                Require(double.IsFinite(wave.Radius) && wave.Radius > 0 &&
                    cornerDistances.All(distance => distance <= radiusSquared + tolerance) &&
                    cornerDistances.Any(distance => Math.Abs(distance - radiusSquared) <= tolerance),
                    location + " radius does not tightly cover the farthest corner");

                var host = Descendants(probe).OfType<Grid>().Single(grid => grid.Name == "RippleHost");
                var layer = ElementCompositionPreview.GetElementChildVisual(host) as ContainerVisual;
                Require(layer is not null && layer.Children.Count == 1, "wave is missing its actual composition layer");
                var visual = layer!.Children.OfType<ShapeVisual>().Single();
                var shape = visual.Shapes.OfType<CompositionSpriteShape>().Single();
                var ellipse = shape.Geometry as CompositionEllipseGeometry;
                Require(ellipse is not null && Math.Abs(ellipse.Radius.X - ellipse.Radius.Y) < .001 &&
                    Math.Abs(shape.Scale.X - shape.Scale.Y) < .001 && Math.Abs(visual.Scale.X - visual.Scale.Y) < .001,
                    "wide button applies a nonuniform scale or noncircular ellipse geometry");
                Require(Math.Abs(ellipse!.Center.X - wave.Origin.X) < .01 && Math.Abs(ellipse.Center.Y - wave.Origin.Y) < .01,
                    "composition ellipse center disagrees with the input origin");
                var clip = layer.Clip as RectangleClip;
                Require(clip is not null && Math.Abs(clip.Left) < .01 && Math.Abs(clip.Top) < .01 &&
                    Math.Abs(clip.Right - snapshot.Width) < .01 && Math.Abs(clip.Bottom - snapshot.Height) < .01 &&
                    new[] { (clip.TopLeftRadius, 20d), (clip.TopRightRadius, 12d),
                        (clip.BottomRightRadius, 8d), (clip.BottomLeftRadius, 4d) }
                        .All(corner => Math.Abs(corner.Item1.X - corner.Item2) < .01 && Math.Abs(corner.Item1.Y - corner.Item2) < .01),
                    "composition clip is missing or does not match the rounded host");
                samples.Add(new { step = location + "-composition", ellipseRadiusBaseValue = ellipse.Radius,
                    shapeScale = shape.Scale, visualScale = visual.Scale, hostWidth = snapshot.Width, hostHeight = snapshot.Height,
                    note = "Composition base values and scale structure; not a sampled animated pixel radius." });
                MaterialRipple.EndForTesting(probe, id, canceled: location == "corner");
                var ended = Sample(location + "-ended").Waves.Single();
                Require(ended.Phase == (location == "corner" ? "canceled" : "released"),
                    location + " did not enter the matching release/cancel phase");
                if (location != "corner")
                    Require(ended.FadeMilliseconds >= 250 && ended.FadeMilliseconds <= 350 && ended.FadeDelayMilliseconds >= 0,
                        "release did not keep the slower visual fade separate from button input");
                await WaitForSmokeConditionAsync(() => MaterialRipple.GetSnapshot(probe).ActiveCount == 0,
                    location + " composition completion did not release its wave");
            }
            Require(clicks == 0, "visual begin/end injection dispatched a command");
            var tapStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var tap = MaterialRipple.BeginForTesting(probe);
            MaterialRipple.EndForTesting(probe, tap);
            var tapWave = Sample("short-tap-released").Waves.Single();
            var tapElapsed = System.Diagnostics.Stopwatch.GetElapsedTime(tapStarted).TotalMilliseconds;
            Require(tapWave.FadeDelayMilliseconds + tapElapsed >= tapWave.GrowthMilliseconds - 2,
                "short tap fades before its circular expansion completes");
            await WaitForSmokeConditionAsync(() => MaterialRipple.GetSnapshot(probe).ActiveCount == 0,
                "short-tap visual did not finish and clean up");
            checks.Add("Material ripple begin/end injection preserves center, edge and corner origins; its target radius tightly covers all host corners. A loaded wide button uses ellipse geometry, equal-axis scales and a rounded composition clip; these are structural observations, not real pointer input or per-frame pixel measurements.");

            Require(MaterialRipple.BeginForTesting(probe) > 0, "motion-off fixture did not begin");
            palette.UpdateMotion(Root, false);
            var motionOff = Sample("motion-off");
            Require(!motionOff.Enabled && motionOff.ActiveCount == 0 && MaterialRipple.BeginForTesting(probe) == 0,
                "motion-off retained or started a wave");
            palette.UpdateMotion(Root, true);
            Require(MaterialRipple.BeginForTesting(probe) > 0, "disabled-control fixture did not begin");
            probe.IsEnabled = false;
            var disabled = Sample("disabled");
            Require(!disabled.Enabled && disabled.ActiveCount == 0 && MaterialRipple.BeginForTesting(probe) == 0,
                "disabled button retained or started a wave");
            probe.IsEnabled = true;

            var burstIds = new List<long>(); var burstBefore = MaterialRipple.GetSnapshot(probe).CreatedCount;
            for (var index = 0; index < 32; index++)
            {
                burstIds.Add(MaterialRipple.BeginForTesting(probe, new Point(index, 12)));
                var burst = MaterialRipple.GetSnapshot(probe);
                Require(burstIds[^1] > 0 && burst.ActiveCount == Math.Min(index + 1, 3), "burst did not retain a bounded set of three waves");
            }
            var burstSnapshot = Sample("burst-32");
            Require(burstSnapshot.CreatedCount == burstBefore + 32 && burstSnapshot.Waves.Select(wave => wave.Id).SequenceEqual(burstIds.TakeLast(3)),
                "burst did not evict its oldest waves");
            foreach (var id in burstIds) MaterialRipple.EndForTesting(probe, id, canceled: true);
            await WaitForSmokeConditionAsync(() => MaterialRipple.GetSnapshot(probe).ActiveCount == 0, "burst cancellation leaked waves");

            Require(MaterialRipple.BeginForTesting(probe) > 0, "unload fixture did not begin");
            fixture.Children.Remove(probe); Root.UpdateLayout();
            await WaitForSmokeConditionAsync(() => !probe.IsLoaded && !MaterialRipple.GetSnapshot(probe).HasHost,
                "unloaded ripple retained its composition host");
            var unloaded = Sample("unloaded");
            Require(unloaded.ActiveCount == 0 && MaterialRipple.BeginForTesting(probe) == 0, "unloaded button retained or started a wave");
            fixture.Children.Add(probe);
            await Ready("reloaded ripple did not reconnect its template host");
            Require(Sample("reloaded").HandlerSets == 1, "reload duplicated registration bookkeeping");
            checks.Add("Material ripple clears waves when animation is disabled, the button is disabled or unloaded; reload reconnects the host. A 32-wave injected burst retains only the newest three and cancellation releases all retained waves.");

            // Reconfigure the same loaded control with new real component owners/styles.
            // This covers registration lifetime without replacing the button or invoking
            // application commands, and is separate from a system theme-change event test.
            for (var index = 0; index < 8; index++)
            {
                var color = index % 2 == 0 ? Palette.C("6750A4") : Palette.C("006A6A");
                var components = DesignComponents.Create(palette.Tokens with { Accent = color, Primary = color });
                Require(MaterialRipple.BeginForTesting(probe) > 0, "retheme fixture did not begin");
                components.ConfigureAction(probe, ActionRole.Primary, compact: false);
                await Ready("rethemed ripple did not resolve its replacement template");
                components.UpdateMotion(probe, true);
                var themed = Sample("retheme-" + index);
                Require(themed.ActiveCount == 0 && themed.HandlerSets == 1 && themed.Enabled &&
                    Descendants(probe).OfType<Grid>().Count(grid => grid.Name == "RippleHost") == 1,
                    "retheme retained old waves or duplicated its host/registration bookkeeping");
                var created = themed.CreatedCount;
                var id = MaterialRipple.BeginForTesting(probe);
                Require(MaterialRipple.GetSnapshot(probe).CreatedCount == created + 1, "retheme begin duplicated a wave");
                MaterialRipple.EndForTesting(probe, id, canceled: true);
                await WaitForSmokeConditionAsync(() => MaterialRipple.GetSnapshot(probe).ActiveCount == 0, "retheme wave did not release");
                var beforeClick = clicks;
                var peer = FrameworkElementAutomationPeer.FromElement(probe) ?? FrameworkElementAutomationPeer.CreatePeerForElement(probe);
                Require(peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider, "fixture lost its native UI Automation Invoke pattern");
                ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
                await WaitForSmokeConditionAsync(() => clicks > beforeClick, "native Invoke did not raise the fixture Click");
                await Task.Delay(80);
                Require(clicks == beforeClick + 1, "one native Invoke raised more than one Click after retheme");
            }
            Require(clicks == 8, "visual lifecycle operations changed the expected native Click count");
            Sample("completed");
            checks.Add("Eight real Material component/style reconfigurations retain the same loaded button, one ripple host and one registration set. Each native UI Automation Invoke raises exactly one harmless fixture Click; test begin/end paths do not invoke Click. Routed mouse/touch/keyboard handler delivery remains a separate live-input check.");
            completed = true;
        }
        catch (Exception exception) { error = exception.ToString(); throw; }
        finally
        {
            if (probe is not null) MaterialRipple.Detach(probe);
            if (fixture is not null) PageHost.Children.Remove(fixture);
            await File.WriteAllTextAsync(Path.Combine(directory, "ripple-interaction.json"), JsonSerializer.Serialize(new
            {
                passed = completed, error, samples, nativeClickCount = clicks, rethemeCycles = 8, burstBegins = 32,
                scope = "Isolated loaded WinUI controls, BeginForTesting/EndForTesting injection, composition structure and native UI Automation Invoke. Not a real pointer/touch/keyboard session, desktop recording or per-frame animated radius measurement. HandlerSets reports registration bookkeeping; routed input duplication requires live input verification."
            }, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
            palette.UpdateMotion(Root, systemUi.AnimationsEnabled);
            ApplySmokePreferences(original); Navigate(originalPage);
        }
    }
}
