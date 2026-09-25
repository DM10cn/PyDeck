using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PimGui.Core;

namespace PimGui.App;
public sealed partial class MainWindow
{
    private UIElement RuntimeIcon(PythonRuntime runtime, bool compact = false)
    {
        var grid = new Grid { Width = compact ? 44 : 52, Height = compact ? 44 : 56, VerticalAlignment = VerticalAlignment.Center, Tag = "PythonRuntimeIcon" };
        grid.Children.Add(new Image { Source = new SvgImageSource(new Uri("ms-appx:///Assets/Python.svg")), Width = compact ? 32 : 40, Height = compact ? 32 : 40, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left });
        var variants = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right, Tag = "RuntimeVariants" };
        foreach (var (visible, file, label) in new[] { (runtime.IsEmbeddable, "Embedded", "Embeddable"), (runtime.IsFreeThreaded, "FreeThreaded", "Free-threaded"), (runtime.IncludesTests, "Tests", "With tests") })
        {
            if (!visible) continue;
            var icon = new Image { Source = new SvgImageSource(new Uri("ms-appx:///Assets/Variant" + file + ".svg")), Width = compact ? 14 : 18, Height = compact ? 14 : 18 };
            ToolTipService.SetToolTip(icon, T(label)); Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(icon, T(label)); variants.Children.Add(icon);
        }
        grid.Children.Add(variants);
        if (runtime.IsPrerelease)
        {
            var badge = new Border { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 207, 64)),
                CornerRadius = new(3), Padding = new(4, 1, 4, 1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Child = new TextBlock { Text = "EAP", FontSize = 9, FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.Black) }, Tag = "EapBadge" };
            ToolTipService.SetToolTip(badge, T("Preview")); grid.Children.Add(badge);
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(grid, runtime.IsPrerelease ? "Python · " + T("Preview") : "Python");
        return grid;
    }
    private async void RefreshDatabase_Click(object sender, RoutedEventArgs e) => await RefreshDatabaseAsync();
    private async Task RefreshDatabaseAsync()
    {
        if (busy || confirmationOpen) return;
        if (!connected) { await ConnectAsync(); if (!connected) return; }
        SetBusy(true, "Refreshing database");
        try
        {
            installed = await ListInstalledAsync();
            if (OfflineSource && offlineBundle is not null) offlineBundle = await Task.Run(() => OfflineBundle.Load(offlineBundle.DirectoryPath));
            else if (!OfflineSource) catalog = await client.ListCatalogAsync();
            MessageBar.IsOpen = false; StatusText.Text = T("Up to date · {0}", DateTime.Now.ToString("t"));
        }
        catch (Exception ex) { catalog = null; ShowError(ex); }
        finally { SetBusy(false); }
    }
}
