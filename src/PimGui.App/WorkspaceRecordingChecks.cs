using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PimGui.Core;
using System.Diagnostics;
using System.Text.Json;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task RunWorkspaceRecordingAsync(string directory, List<string> checks)
    {
        if (smokeDirectory is null) throw new IOException("Recording requires an isolated smoke profile");
        // This probe starts from Root.Loaded. Let the initial tree finish loading
        // before replacing its presentation with the recording's Material tree.
        await Task.Delay(250);
        var frameDirectory = Path.Combine(directory, "frames"); Directory.CreateDirectory(frameDirectory);
        var frames = new List<object>(); var events = new List<object>();
        var clock = Stopwatch.StartNew(); var startedUtc = DateTimeOffset.UtcNow;
        void Trace(string step) => File.AppendAllText(Path.Combine(directory, "recording-trace.txt"), $"{clock.Elapsed.TotalMilliseconds:F1} ms {step}\n");
        var completed = false;
        async Task Segment(string name)
        {
            var beginning = clock.Elapsed.TotalMilliseconds;
            events.Add(new { name, elapsedMilliseconds = beginning });
            for (var sample = 0; sample < 8; sample++)
            {
                var remaining = beginning + sample * 100 - clock.Elapsed.TotalMilliseconds;
                if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining));
                Root.UpdateLayout();
                var captured = clock.Elapsed.TotalMilliseconds;
                var file = $"frames/frame-{frames.Count:0000}.png";
                var pixels = await RenderRecordingFrameAsync(Path.Combine(directory, file), Root);
                frames.Add(new { file, segment = name, elapsedMilliseconds = captured,
                    completedMilliseconds = clock.Elapsed.TotalMilliseconds, pixelWidth = pixels.Width, pixelHeight = pixels.Height });
            }
        }
        async Task SelectPage(string id)
        {
            var item = Descendants(ShellHost).OfType<RadioButton>().Single(item => item.GroupName == "MaterialNavigation" && item.Tag as string == id);
            var peer = FrameworkElementAutomationPeer.FromElement(item) ?? FrameworkElementAutomationPeer.CreatePeerForElement(item);
            ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
            Root.UpdateLayout();
            await WaitForSmokeConditionAsync(() => page == id, "Recording navigation did not reach " + id);
        }
        try
        {
            installed = Enumerable.Range(0, 5).Select(index => new PythonRuntime("recording-installed-" + index,
                "PythonCore", $"3.{14 - index}-64", $"3.{14 - index}.7", $"Python 3.{14 - index}.7",
                Path.Combine(store.DirectoryPath, "recording-fixture", $"python-3.{14 - index}", "python.exe"), store.DirectoryPath,
                IsDefault: index == 0, IsManaged: true)).ToArray();
            catalog = Enumerable.Range(0, 3).SelectMany(series => Enumerable.Range(0, 8).Select(micro =>
                new PythonRuntime("recording-installed-" + series, "PythonCore", $"3.{14 - series}-64", $"3.{14 - series}.{micro}",
                    $"Python 3.{14 - series}.{micro}", "", "", IsDefault: false, IsManaged: true))).ToArray();
            expandedSeries.Clear(); activityLog.Clear(); activityFilter = null;
            for (var index = 0; index < 12; index++) activityLog.Add($"交互演示 {index + 1:00} · Python 3.14.7 · x64", ActivityLevel.Information, ActivityOrigin.Application);
            Trace("Applying isolated Material preferences");
            ApplySmokePreferences(preferences with { Design = "Material", Theme = "Dark", Language = "zh-CN", Transparency = "Off",
                MaterialColorSource = "Custom", MaterialColorStyle = "TonalSpot", MaterialSeed = 0xFF6750A4u,
                CatalogSource = "Online", DefaultArchitecture = "x64", CatalogPackageType = "Standard", ShowPreviewReleases = false });
            Trace("Preferences applied");
            var scale = Root.XamlRoot.RasterizationScale;
            AppWindow.Resize(new SizeInt32((int)(1180 * scale), (int)(850 * scale)));
            Trace("Window resized; navigating to runtimes");
            Navigate("runtimes"); Root.UpdateLayout(); Trace("Runtimes first layout complete"); await Task.Delay(600); Root.UpdateLayout();
            await Segment("my-python");
            var install = Descendants(PageHost).OfType<Button>().Single(button => AutomationProperties.GetName(button) == T("Install Python"));
            if (!install.Focus(FocusState.Keyboard)) throw new IOException("Recording primary action rejected keyboard focus");
            await Segment("primary-action-keyboard-focus");

            await SelectPage("catalog"); await Segment("navigate-to-catalog");
            var selector = Descendants(PageHost).OfType<ComboBox>().Single(box => AutomationProperties.GetName(box) == T("Architecture"));
            var selectorPeer = FrameworkElementAutomationPeer.FromElement(selector) ?? FrameworkElementAutomationPeer.CreatePeerForElement(selector);
            var dropdown = (IExpandCollapseProvider)selectorPeer.GetPattern(PatternInterface.ExpandCollapse);
            dropdown.Expand();
            Popup? popup = null;
            await WaitForSmokeConditionAsync(() =>
            {
                popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).FirstOrDefault(candidate => candidate.Child is not null &&
                    Descendants(candidate.Child).OfType<ComboBoxItem>().Any(item => ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), selector)));
                return selector.IsDropDownOpen && popup?.Child is FrameworkElement { ActualHeight: > 0 };
            }, "Recording native dropdown did not open");
            await Task.Delay(120); Root.UpdateLayout();
            var popupTime = clock.Elapsed.TotalMilliseconds;
            await RenderRecordingFrameAsync(Path.Combine(directory, "architecture-popup.png"), popup!.Child);
            events.Add(new { name = "native-architecture-dropdown-open", elapsedMilliseconds = popupTime,
                separateScreenshot = "architecture-popup.png", note = "Detached popup is captured separately; Root frames do not contain it." });
            dropdown.Collapse();
            await WaitForSmokeConditionAsync(() => !selector.IsDropDownOpen, "Recording native dropdown did not close");
            await Segment("architecture-dropdown-closed");

            var series = Descendants(PageHost).OfType<Expander>().Single(header => header.Tag as string == "CatalogSeries:3.14");
            var seriesPeer = FrameworkElementAutomationPeer.FromElement(series) ?? FrameworkElementAutomationPeer.CreatePeerForElement(series);
            var expansion = (IExpandCollapseProvider)seriesPeer.GetPattern(PatternInterface.ExpandCollapse);
            expansion.Expand(); await Segment("native-series-expanded");
            expansion.Collapse(); await Segment("native-series-collapsed");
            palette.UpdateMotion(Root, false);
            expansion.Expand(); await Segment("animations-disabled-series-expanded");
            await SelectPage("activity"); await Segment("navigate-to-activity");
            completed = true;
            checks.Add("Recorded genuine offscreen WinUI Root bitmap samples of native navigation, series expansion/collapse, keyboard focus and disabled animations; native architecture dropdown is a separate popup bitmap. No installation or other runtime mutation was invoked.");
        }
        finally
        {
            palette.UpdateMotion(Root, systemUi.AnimationsEnabled);
            await File.WriteAllTextAsync(Path.Combine(directory, "recording.json"), JsonSerializer.Serialize(new
            {
                completed, startedUtc, requestedWindowWidthDip = 1180, requestedWindowHeightDip = 850,
                requestedFrameIntervalMilliseconds = 100, actualRootWidthDip = Root.ActualWidth, actualRootHeightDip = Root.ActualHeight,
                rasterizationScale = Root.XamlRoot.RasterizationScale, textScaleFactor = systemUi.TextScaleFactor,
                scope = "Synthetic fixture, actual app-internal bitmap samples and native UI Automation; not a desktop screen recording or human keyboard/mouse session. Native window frame and compositor materials are not captured. Detached dropdown is evidenced only by architecture-popup.png.",
                frames, events
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static async Task<(int Width, int Height)> RenderRecordingFrameAsync(string path, UIElement element)
    {
        path = Path.GetFullPath(path);
        var target = new RenderTargetBitmap(); await target.RenderAsync(element);
        if (target.PixelWidth == 0 || target.PixelHeight == 0) throw new IOException("Recording target did not render");
        var pixels = await target.GetPixelsAsync();
        using var reader = DataReader.FromBuffer(pixels);
        var bytes = new byte[pixels.Length]; reader.ReadBytes(bytes);
        var file = await StorageFile.GetFileFromPathAsync(CreateFile(path));
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)target.PixelWidth, (uint)target.PixelHeight, 96, 96, bytes);
        await encoder.FlushAsync();
        return (target.PixelWidth, target.PixelHeight);
    }
}
