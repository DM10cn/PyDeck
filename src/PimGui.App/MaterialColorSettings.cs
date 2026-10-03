using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private StackPanel MaterialColorSettings()
    {
        var section = palette.Section("Dynamic colors");
        var dual = preferences.MaterialColorStyle == "DualSource";
        section.Tag = "MaterialColorSettings";
        section.Children.Add(SettingRow("Color source", "Desktop wallpaper is read locally. No image is copied or uploaded.",
            Choice([("Wallpaper", "Desktop wallpaper"), ("Custom", "Custom base color")], preferences.MaterialColorSource,
                source => { SavePreferences(preferences with { MaterialColorSource = source }); RefreshMaterialColors(); }, "Color source")));
        section.Children.Add(SettingRow("Color palette", dual ? "The first color shapes primary actions and backgrounds; the second shapes navigation and supporting accents." : null,
            Choice([("TonalSpot", "Balanced"), ("Expressive", "Expressive"), ("DualSource", "Two colors")], preferences.MaterialColorStyle,
                style => SavePreferences(preferences with { MaterialColorStyle = style }), "Color palette")));
        if (dual)
        {
            section.Children.Add(palette.Label("Wallpaper mode chooses two distinct colors when available; a single color is used when no second candidate is found.", palette.Tokens.CaptionFontSize, muted: true));
            var preview = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Tag = "DualSourcePreview" };
            foreach (var (name, color) in new[] { ("First base color", MaterialSeedForRender), ("Second base color", MaterialSecondSeedForRender) })
            {
                var swatch = new Border { Width = 28, Height = 28, CornerRadius = new(14), Background = Palette.Brush(SeedColor(color)),
                    BorderBrush = Palette.Brush(palette.Line), BorderThickness = new(palette.Tokens.HighContrast ? 1 : 0) };
                AutomationProperties.SetName(swatch, T(name) + $" #{color & 0xFFFFFFu:X6}");
                var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                item.Children.Add(swatch); item.Children.Add(palette.Label(T(name) + $" · #{color & 0xFFFFFFu:X6}", palette.Tokens.CaptionFontSize, muted: true));
                preview.Children.Add(item);
            }
            section.Children.Add(preview);
        }
        materialColorDescription = palette.Label("", palette.Tokens.CaptionFontSize, muted: true);
        AutomationProperties.SetLiveSetting(materialColorDescription, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        section.Children.Add(materialColorDescription);
        wallpaperRefreshButton = palette.Action("Refresh wallpaper colors", "\uE72C", compact: true, role: ActionRole.Quiet);
        wallpaperRefreshButton.HorizontalAlignment = HorizontalAlignment.Left;
        wallpaperRefreshButton.Click += (_, _) => RefreshMaterialColors(force: true);
        section.Children.Add(wallpaperRefreshButton);

        var picker = SeedPicker(preferences.MaterialSeed, dual ? 240 : 280);
        AutomationProperties.SetName(picker, T("Custom base color"));
        ColorPicker? secondPicker = null;
        var custom = new StackPanel { Spacing = 8 };
        custom.Children.Add(palette.Label(dual ? "These colors are also used when wallpaper colors are unavailable." : "This color is also used when wallpaper colors are unavailable.", palette.Tokens.CaptionFontSize, muted: true));
        if (dual)
        {
            secondPicker = SeedPicker(preferences.MaterialSecondSeed ?? preferences.MaterialSeed, 240);
            AutomationProperties.SetName(secondPicker, T("Second base color"));
            var pickers = new Grid { ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
            pickers.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            pickers.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
            var first = new StackPanel { Spacing = 6 }; first.Children.Add(palette.Label("First base color", palette.Tokens.CaptionFontSize)); first.Children.Add(picker);
            var second = new StackPanel { Spacing = 6 }; second.Children.Add(palette.Label("Second base color", palette.Tokens.CaptionFontSize)); second.Children.Add(secondPicker);
            Grid.SetColumn(second, 1); pickers.Children.Add(first); pickers.Children.Add(second); custom.Children.Add(pickers);
        }
        else custom.Children.Add(picker);
        var apply = palette.Action(dual ? "Apply custom colors" : "Apply custom color", primary: true, compact: true);
        apply.HorizontalAlignment = HorizontalAlignment.Left;
        apply.Click += (_, _) =>
        {
            SavePreferences(preferences with { MaterialSeed = SeedArgb(picker.Color),
                MaterialSecondSeed = dual ? SeedArgb(secondPicker!.Color) : preferences.MaterialSecondSeed, MaterialColorSource = "Custom" });
            RefreshMaterialColors();
        };
        custom.Children.Add(apply);
        var expand = palette.Expander("Custom base color", custom, preferences.MaterialColorSource == "Custom");
        section.Children.Add(expand);
        UpdateMaterialColorDescription();
        return section;
    }

    private static Windows.UI.Color SeedColor(uint seed) => Windows.UI.Color.FromArgb(255, (byte)(seed >> 16), (byte)(seed >> 8), (byte)seed);
    private static uint SeedArgb(Windows.UI.Color color) => 0xFF000000u | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
    private static ColorPicker SeedPicker(uint seed, double width) => new()
    {
        Color = SeedColor(seed), IsAlphaEnabled = false, IsAlphaSliderVisible = false, IsAlphaTextInputVisible = false,
        IsColorChannelTextInputVisible = false, IsHexInputVisible = true, MaxWidth = width, HorizontalAlignment = HorizontalAlignment.Left
    };
}
