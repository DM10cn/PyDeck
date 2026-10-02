using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;
using Windows.Graphics.Imaging;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckMaterialColorsAsync(string directory)
    {
        var original = preferences;
        var originalSeed = wallpaperSeed;
        var originalSecondSeed = wallpaperSecondSeed;
        var originalSignature = wallpaperSignature;
        var networkWasExpanded = expandedSettings.Contains("Network");
        try
        {
            var fixture = Path.Combine(directory, "wallpaper-fixture.png");
            using (var output = new FileStream(fixture, FileMode.Create, FileAccess.ReadWrite))
            using (var stream = output.AsRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                // Each pair has an opaque red pixel and transparent blue pixel. Only red may seed a palette.
                byte[] pixels = [255, 0, 0, 255, 0, 0, 255, 0, 255, 0, 0, 255, 0, 0, 255, 0];
                encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, 2, 2, 96, 96, pixels);
                await encoder.FlushAsync();
            }
            var extracted = await WallpaperColorReader.ReadFileAsync(fixture, null, null, CancellationToken.None);
            if (extracted is null || extracted.Seed != 0xFFFF0000u || extracted.SecondSeed != extracted.Seed || extracted.Signature.Length != 64)
                throw new IOException("WIC image extraction did not preserve opaque sRGB seed colors");
            var cached = await WallpaperColorReader.ReadFileAsync(fixture, extracted.Signature, extracted.Seed, CancellationToken.None);
            if (cached != extracted) throw new IOException("Unchanged wallpaper did not preserve its cached result");

            var dualFixture = Path.Combine(directory, "wallpaper-dual-fixture.png");
            using (var output = new FileStream(dualFixture, FileMode.Create, FileAccess.ReadWrite))
            using (var stream = output.AsRandomAccessStream())
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
                byte[] pixels = [255, 0, 0, 255, 0, 255, 0, 255, 255, 0, 0, 255, 0, 255, 0, 255];
                encoder.SetPixelData(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, 2, 2, 96, 96, pixels);
                await encoder.FlushAsync();
            }
            var expectedSeeds = MonetColors.SeedsFromPixels([0xFFFF0000u, 0xFF00FF00u, 0xFFFF0000u, 0xFF00FF00u]);
            var dualExtracted = await WallpaperColorReader.ReadFileAsync(dualFixture, null, null, CancellationToken.None);
            if (expectedSeeds.Length < 2 || dualExtracted is null || dualExtracted.Seed != expectedSeeds[0] || dualExtracted.SecondSeed != expectedSeeds[1])
                throw new IOException("Wallpaper decoding did not preserve the ordered AOSP seed pair");
            var dualCached = await WallpaperColorReader.ReadFileAsync(dualFixture, dualExtracted.Signature, dualExtracted.Seed, CancellationToken.None, dualExtracted.SecondSeed);
            if (dualCached != dualExtracted) throw new IOException("Wallpaper cache mixed or discarded the second color");

            foreach (var theme in new[] { "Light", "Dark" })
            foreach (var seed in new uint[] { 0xFFA85028u, 0xFF287C60u })
            {
                ApplySmokePreferences(preferences with { Design = "Material", Theme = theme, MaterialColorSource = "Custom", MaterialColorStyle = "TonalSpot", MaterialSeed = seed, Language = "zh-CN", Transparency = "Off" });
                Navigate("components"); Root.UpdateLayout();
                var expected = MonetColors.Create(seed, theme == "Dark", preferences.MaterialColorStyle);
                if (palette.Accent != Argb(expected.Primary) || palette.Text != Argb(expected.OnSurface) || !palette.Tokens.SupportsDynamicColor)
                    throw new IOException("Material presentation did not use the native dynamic scheme");
                await CaptureAsync(Path.Combine(directory, $"monet-{theme}-{seed:X8}.png"));
            }

            Navigate("settings"); Root.UpdateLayout();
            var picker = Descendants(PageHost).OfType<ColorPicker>().Single();
            picker.Color = Argb(0xFF795548u);
            var apply = Descendants(PageHost).OfType<Button>().Single(button => AutomationProperties.GetName(button) == T("Apply custom color"));
            InvokeButton(apply);
            await WaitForSmokeConditionAsync(() => preferences.MaterialSeed == 0xFF795548u, "Custom color action did not save");
            if (store.Load().MaterialSeed != 0xFF795548u || store.Load().MaterialColorSource != "Custom") throw new IOException("Custom color was not persisted");
            var colorSection = Descendants(PageHost).OfType<StackPanel>().Single(panel => panel.Tag as string == "MaterialColorSettings");
            colorSection.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
            await CaptureAsync(Path.Combine(directory, "monet-custom-settings.png"));

            ApplySmokePreferences(preferences with { MaterialColorSource = "Wallpaper", MaterialColorStyle = "DualSource" });
            expandedSettings.Add("Network"); Navigate("settings"); Root.UpdateLayout();
            var draft = Descendants(settingsSections["Network"].Section).OfType<TextBox>().First();
            draft.Text = "http://unsaved-wallpaper-fixture.invalid:8123";
            var pageBefore = PageHost.Children.Single();
            wallpaperSeed = 0xFF22AA55u;
            wallpaperSecondSeed = 0xFFAA5239u;
            materialColorsPending = true;
            ApplyPreparedMaterialColors();
            QueueAppearance();
            await Task.Delay(80);
            if (!MaterialColorsPending || !ReferenceEquals(pageBefore, PageHost.Children.Single()) || !draft.IsLoaded || draft.Text != "http://unsaved-wallpaper-fixture.invalid:8123")
                throw new IOException("Wallpaper completion replaced an in-progress settings editor");
            cancelDialogOpen = true;
            page = "runtimes";
            ApplyPreparedMaterialColors();
            if (!MaterialColorsPending || !ReferenceEquals(pageBefore, PageHost.Children.Single()))
                throw new IOException("Wallpaper completion refreshed beneath an open cancellation dialog");
            cancelDialogOpen = false;
            Navigate("runtimes"); Root.UpdateLayout();
            var wallpaperScheme = MonetColors.Create(wallpaperSeed.Value, true, "DualSource", wallpaperSecondSeed);
            if (MaterialColorsPending || palette.Accent != Argb(wallpaperScheme.Primary) || palette.Tokens.SecondaryContainer != Argb(wallpaperScheme.SecondaryContainer))
                throw new IOException("Explicit navigation failed to consume prepared wallpaper colors");
            ApplySmokeAppearance("Fluent", "Light"); Navigate("settings"); Root.UpdateLayout();
            if (Descendants(PageHost).OfType<FrameworkElement>().Any(element => element.Tag as string == "MaterialColorSettings"))
                throw new IOException("Material-only settings leaked into Fluent");
        }
        finally
        {
            cancelDialogOpen = false;
            wallpaperSeed = originalSeed; wallpaperSecondSeed = originalSecondSeed; wallpaperSignature = originalSignature; materialColorsPending = false;
            if (!networkWasExpanded) expandedSettings.Remove("Network");
            ApplySmokePreferences(original);
        }
        static Windows.UI.Color Argb(uint color) => Windows.UI.Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }
}
