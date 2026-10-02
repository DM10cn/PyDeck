using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Windows.Graphics;
using Windows.Graphics.Imaging;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private DispatcherQueueTimer? wallpaperTimer;
    private CancellationTokenSource? wallpaperCancellation;
    private uint? wallpaperSeed;
    private uint? wallpaperSecondSeed;
    private string? wallpaperSignature;
    private int wallpaperGeneration;
    private bool wallpaperLoading;
    private bool wallpaperUnavailable;
    private bool materialColorsPending;
    private TextBlock? materialColorDescription;
    private Button? wallpaperRefreshButton;

    private uint MaterialSeedForRender => preferences.MaterialColorSource == "Custom"
        ? preferences.MaterialSeed : wallpaperSeed ?? preferences.MaterialSeed;
    private uint MaterialSecondSeedForRender => preferences.MaterialColorSource == "Custom" || !wallpaperSeed.HasValue
        ? preferences.MaterialSecondSeed ?? preferences.MaterialSeed : wallpaperSecondSeed ?? wallpaperSeed.Value;
    private bool MaterialColorsPending => materialColorsPending;
    private string WallpaperCachePath => Path.Combine(store.DirectoryPath, "material-colors.json");
    private sealed record WallpaperColorCache(int Version, uint Seed, string Signature, uint? SecondSeed = null);

    private void InitializeMaterialColors()
    {
        // Fixtures must never read a real wallpaper or share the user's wallpaper cache.
        if (smokeDirectory is not null) return;
        try
        {
            if (File.Exists(WallpaperCachePath))
            {
                SafeFiles.RequireNoLinks(WallpaperCachePath);
                var cache = JsonSerializer.Deserialize<WallpaperColorCache>(SafeFiles.ReadText(WallpaperCachePath));
                if (cache is { Version: 1 or 2 } && cache.Signature is { Length: 64 } && cache.Signature.All(Uri.IsHexDigit))
                {
                    wallpaperSeed = cache.Seed | 0xFF000000u;
                    wallpaperSecondSeed = (cache.SecondSeed ?? cache.Seed) | 0xFF000000u;
                    // Old single-color caches remain usable while a fresh pair is prepared.
                    wallpaperSignature = cache.Version == 2 ? cache.Signature : null;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        wallpaperTimer = DispatcherQueue.CreateTimer();
        wallpaperTimer.Interval = TimeSpan.FromSeconds(30);
        wallpaperTimer.IsRepeating = true;
        wallpaperTimer.Tick += (_, _) => RefreshMaterialColors();
        Activated += OnMaterialWindowActivated;
        Closed += (_, _) =>
        {
            Activated -= OnMaterialWindowActivated;
            wallpaperTimer.Stop();
            wallpaperGeneration++;
            wallpaperCancellation?.Cancel();
        };
    }

    private void OnMaterialWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated) RefreshMaterialColors();
    }

    private void MarkMaterialColorsApplied()
    {
        materialColorsPending = false;
        UpdateMaterialColorDescription();
    }

    private void RefreshMaterialColors(bool force = false)
    {
        if (smokeDirectory is not null || closed || !initialized || ActiveDesign != "Material" || preferences.MaterialColorSource != "Wallpaper")
        {
            wallpaperTimer?.Stop();
            wallpaperGeneration++;
            wallpaperCancellation?.Cancel();
            wallpaperLoading = false;
            if (preferences.MaterialColorSource != "Wallpaper") materialColorsPending = false;
            UpdateMaterialColorDescription();
            return;
        }
        wallpaperTimer?.Start();
        if (wallpaperLoading && !force) return;
        wallpaperCancellation?.Cancel();
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        wallpaperCancellation = cancellation;
        var generation = ++wallpaperGeneration;
        var location = AppWindow.Position; var size = AppWindow.Size;
        var window = new RectInt32(location.X, location.Y, size.Width, size.Height);
        wallpaperLoading = true;
        UpdateMaterialColorDescription();
        _ = ReadMaterialColorsAsync(window, force, generation, cancellation);
    }

    private async Task ReadMaterialColorsAsync(RectInt32 window, bool force, int generation, CancellationTokenSource cancellation)
    {
        var previousSeed = wallpaperSeed;
        var previousSecondSeed = wallpaperSecondSeed;
        var previousSignature = wallpaperSignature;
        try
        {
            var result = await Task.Run(() => WallpaperColorReader.ReadAsync(window, force ? null : previousSignature, previousSeed, cancellation.Token, previousSecondSeed), cancellation.Token);
            if (closed || generation != wallpaperGeneration || preferences.MaterialColorSource != "Wallpaper" || ActiveDesign != "Material") return;
            wallpaperUnavailable = result is null;
            if (result is not null)
            {
                var secondSeed = result.SecondSeed ?? result.Seed;
                var changed = MaterialSeedForRender != result.Seed || preferences.MaterialColorStyle == "DualSource" && MaterialSecondSeedForRender != secondSeed;
                wallpaperSeed = result.Seed;
                wallpaperSecondSeed = secondSeed;
                wallpaperSignature = result.Signature;
                if (result.Signature != previousSignature || previousSeed != result.Seed || previousSecondSeed != secondSeed)
                {
                    // Cache the ordered pair together. Image bytes and paths stay transient.
                    try
                    {
                        Directory.CreateDirectory(store.DirectoryPath);
                        AtomicJson.Write(WallpaperCachePath, JsonSerializer.Serialize(new WallpaperColorCache(2, result.Seed, result.Signature, secondSeed)));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                }
                if (changed)
                {
                    materialColorsPending = true;
                    ApplyPreparedMaterialColors();
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (generation == wallpaperGeneration && !closed) wallpaperUnavailable = true;
        }
        catch (Exception)
        {
            // A wallpaper/decoder/optional native-color failure must never prevent using the application.
            if (generation == wallpaperGeneration && !closed) wallpaperUnavailable = true;
        }
        finally
        {
            if (ReferenceEquals(wallpaperCancellation, cancellation)) wallpaperCancellation = null;
            cancellation.Dispose();
            if (generation == wallpaperGeneration && !closed)
            {
                wallpaperLoading = false;
                UpdateMaterialColorDescription();
            }
        }
    }

    private void UpdateMaterialColorDescription()
    {
        if (materialColorDescription is not null)
            materialColorDescription.Text = T(preferences.MaterialColorSource == "Custom" ? (preferences.MaterialColorStyle == "DualSource" ? "Colors are generated from your two custom base colors." : "Colors are generated from your custom base color.")
                : materialColorsPending ? "Wallpaper colors are ready and will apply when you change pages."
                : wallpaperLoading ? "Reading desktop wallpaper colors…"
                : wallpaperUnavailable ? (wallpaperSeed.HasValue ? "Wallpaper unavailable. Using the last extracted colors." : "Wallpaper unavailable. Using your custom base color.")
                : wallpaperSeed.HasValue ? "Colors follow the wallpaper on the display containing PyDeck."
                : "Using your custom base color until wallpaper colors are ready.");
        if (wallpaperRefreshButton is not null)
            wallpaperRefreshButton.IsEnabled = preferences.MaterialColorSource == "Wallpaper" && !wallpaperLoading;
    }
}

internal sealed record WallpaperColorResult(uint Seed, string Signature, uint? SecondSeed = null);

internal static class WallpaperColorReader
{
    private const long MaximumImageBytes = 64L * 1024 * 1024;
    private const ulong MaximumImagePixels = 64UL * 1024 * 1024;
    private const uint MaximumImageDimension = 32768;
    private const uint MaximumSamplePixels = 112 * 112;

    public static async Task<WallpaperColorResult?> ReadAsync(RectInt32 window, string? cachedSignature, uint? cachedSeed, CancellationToken cancellation, uint? cachedSecondSeed = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var path = CurrentWallpaper(window);
        if (string.IsNullOrWhiteSpace(path)) return null;
        return await ReadFileAsync(path, cachedSignature, cachedSeed, cancellation, cachedSecondSeed);
    }

    // Fixture entry point: decodes only its supplied local file and never queries the desktop.
    internal static async Task<WallpaperColorResult?> ReadFileAsync(string path, string? cachedSignature, uint? cachedSeed, CancellationToken cancellation, uint? cachedSecondSeed = null)
    {
        cancellation.ThrowIfCancellationRequested();
        // The Windows transcoded wallpaper may have no extension, so let the OS decoder inspect the bytes.
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.Any(char.IsControl) || path.IndexOf(':', 2) >= 0) return null;
        path = Path.GetFullPath(path);
        if (new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network) return null;
        SafeFiles.RequireNoLinks(path);
        var information = new FileInfo(path);
        if (!information.Exists || information.Length is <= 0 or > MaximumImageBytes) return null;
        var length = information.Length; var modified = information.LastWriteTimeUtc.Ticks;
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"monet-area-nearest-pair-v3\n{path.ToUpperInvariant()}\n{length}\n{modified}")));
        if (cachedSeed.HasValue && cachedSignature == signature) return new(cachedSeed.Value, signature, cachedSecondSeed ?? cachedSeed.Value);
        cancellation.ThrowIfCancellationRequested();
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (input.Length != length || input.Length > MaximumImageBytes) return null;
        using var stream = input.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellation);
        var width = decoder.PixelWidth; var height = decoder.PixelHeight;
        if (width == 0 || height == 0 || width > MaximumImageDimension || height > MaximumImageDimension || (ulong)width * height > MaximumImagePixels) return null;
        // Match AOSP WallpaperColors.calculateOptimalSize and createScaledBitmap(filter: false):
        // scale by area, floor each dimension, and retain at least one pixel on either axis.
        // The Windows adapter scales source coordinates before applying EXIF orientation and sRGB conversion.
        var scale = Math.Min(1d, Math.Sqrt(MaximumSamplePixels / ((double)width * height)));
        var scaledWidth = Math.Max(1u, (uint)(width * scale));
        var scaledHeight = Math.Max(1u, (uint)(height * scale));
        // AOSP's minimum of one can exceed the native sample bound for extremely narrow images.
        if ((ulong)scaledWidth * scaledHeight > MaximumSamplePixels)
        {
            if (scaledWidth >= scaledHeight) scaledWidth = MaximumSamplePixels / scaledHeight;
            else scaledHeight = MaximumSamplePixels / scaledWidth;
        }
        var transform = new BitmapTransform { ScaledWidth = scaledWidth, ScaledHeight = scaledHeight, InterpolationMode = BitmapInterpolationMode.NearestNeighbor };
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8, BitmapAlphaMode.Straight, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb).AsTask(cancellation);
        var rgba = pixels.DetachPixelData();
        if (rgba.Length == 0 || rgba.Length > MaximumSamplePixels * 4 || rgba.Length % 4 != 0) return null;
        var argb = new uint[rgba.Length / 4];
        // Preserve alpha: the shared quantizer excludes nonopaque samples instead of blending them into black.
        for (var index = 0; index < argb.Length; index++)
        {
            var offset = index * 4;
            argb[index] = (uint)rgba[offset + 3] << 24 | (uint)rgba[offset] << 16 | (uint)rgba[offset + 1] << 8 | rgba[offset + 2];
        }
        cancellation.ThrowIfCancellationRequested();
        var seeds = MonetColors.SeedsFromPixels(argb);
        information.Refresh();
        if (!information.Exists || information.Length != length || information.LastWriteTimeUtc.Ticks != modified) return null;
        cancellation.ThrowIfCancellationRequested();
        return new(seeds[0], signature, seeds.Length > 1 ? seeds[1] : seeds[0]);
    }

    private static string? CurrentWallpaper(RectInt32 window)
    {
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), throwOnError: true)!);
            var desktop = (IDesktopWallpaper)instance!;
            if (desktop.GetMonitorDevicePathCount(out var count) == 0)
            {
                string? chosen = null; long bestOverlap = -1; double closest = double.MaxValue;
                for (uint index = 0; index < Math.Min(count, 64u); index++)
                {
                    var code = desktop.GetMonitorDevicePathAt(index, out var monitorPointer);
                    var monitor = TakeString(monitorPointer);
                    if (code != 0 || monitor is null || desktop.GetMonitorRECT(monitor, out var bounds) != 0) continue;
                    var overlap = Math.Max(0L, Math.Min((long)window.X + window.Width, bounds.Right) - Math.Max(window.X, bounds.Left))
                        * Math.Max(0L, Math.Min((long)window.Y + window.Height, bounds.Bottom) - Math.Max(window.Y, bounds.Top));
                    var centerX = window.X + window.Width / 2d; var centerY = window.Y + window.Height / 2d;
                    var dx = centerX - Math.Clamp(centerX, bounds.Left, bounds.Right);
                    var dy = centerY - Math.Clamp(centerY, bounds.Top, bounds.Bottom);
                    var distance = dx * dx + dy * dy;
                    if (overlap > bestOverlap || overlap == bestOverlap && distance < closest)
                    { chosen = monitor; bestOverlap = overlap; closest = distance; }
                }
                if (chosen is not null)
                {
                    var code = desktop.GetWallpaper(chosen, out var wallpaperPointer);
                    var wallpaper = TakeString(wallpaperPointer);
                    // A successful empty result means a solid background; do not reuse an old SPI path.
                    if (code == 0) return wallpaper;
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException or ArgumentException) { }
        finally
        {
            if (instance is not null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
        }
        var fallback = new StringBuilder(260);
        return GetDesktopParameter(0x0073, (uint)fallback.Capacity, fallback, 0) ? fallback.ToString() : null;
    }

    private static string? TakeString(nint pointer)
    {
        if (pointer == 0) return null;
        try { return Marshal.PtrToStringUni(pointer); }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDesktopParameter(uint action, uint parameter, StringBuilder value, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorRectangle { public int Left; public int Top; public int Right; public int Bottom; }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        [PreserveSig] int SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitor, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [PreserveSig] int GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitor, out nint wallpaper);
        [PreserveSig] int GetMonitorDevicePathAt(uint index, out nint monitor);
        [PreserveSig] int GetMonitorDevicePathCount(out uint count);
        [PreserveSig] int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitor, out MonitorRectangle rectangle);
    }
}
