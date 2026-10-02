using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PimGui.Core;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private async Task CheckDualColorUiAsync(string directory)
    {
        if (smokeDirectory is null) throw new IOException("An isolated dual-color fixture is required");
        var original = preferences;
        var originalPage = page;
        var originalSize = AppWindow.Size;
        try
        {
            const uint first = 0xFF287C60u;
            const uint second = 0xFFAA5239u;
            foreach (var theme in new[] { "Light", "Dark" })
            {
                ApplySmokePreferences(preferences with { Design = "Material", Theme = theme, Language = "zh-CN",
                    MaterialColorSource = "Custom", MaterialColorStyle = "TonalSpot", MaterialSeed = 0xFF5068C5u, MaterialSecondSeed = null });
                Navigate("settings"); Root.UpdateLayout();
                var style = Descendants(PageHost).OfType<ComboBox>().Single(control => AutomationProperties.GetName(control) == T("Color palette"));
                style.SelectedItem = style.Items.OfType<ComboBoxItem>().Single(item => item.Tag as string == "DualSource");
                await WaitForSmokeConditionAsync(() => preferences.MaterialColorStyle == "DualSource", "Dual-color selection did not persist");
                Root.UpdateLayout();
                var pickers = Descendants(PageHost).OfType<ColorPicker>().ToArray();
                if (pickers.Length != 2) throw new IOException("Dual-color settings do not expose both native color pickers");
                pickers.Single(picker => AutomationProperties.GetName(picker) == T("Custom base color")).Color = Color(first);
                pickers.Single(picker => AutomationProperties.GetName(picker) == T("Second base color")).Color = Color(second);
                var apply = Descendants(PageHost).OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == T("Apply custom colors") || AutomationProperties.GetName(button) == T("Apply custom color"));
                InvokeButton(apply);
                await WaitForSmokeConditionAsync(() => preferences.MaterialSeed == first && preferences.MaterialSecondSeed == second,
                    "Applying custom colors did not retain both seeds");
                if (store.Load().MaterialSecondSeed != second || store.Load().MaterialColorStyle != "DualSource")
                    throw new IOException("Dual-color preferences were not saved together");
                Navigate("components"); Root.UpdateLayout();
                var expected = MonetColors.Create(first, theme == "Dark", "DualSource", second);
                if (palette.Accent != Color(expected.Primary) || palette.Tokens.SecondaryContainer != Color(expected.SecondaryContainer) ||
                    palette.Tokens.Tertiary != Color(expected.Tertiary))
                    throw new IOException("The UI did not consume the complete two-seed scheme");
                await CaptureAsync(Path.Combine(directory, $"monet-dual-{theme}.png"));

                // A change to only the second color must invalidate the saved palette and
                // redraw the auxiliary roles without changing the primary family.
                var firstAccent = palette.Accent;
                SavePreferences(preferences with { MaterialSecondSeed = 0xFF5068C5u });
                Root.UpdateLayout();
                var changed = MonetColors.Create(first, theme == "Dark", "DualSource", 0xFF5068C5u);
                if (palette.Accent != firstAccent || palette.Tokens.SecondaryContainer != Color(changed.SecondaryContainer) ||
                    palette.Tokens.SecondaryContainer == Color(expected.SecondaryContainer))
                    throw new IOException("Changing only the second seed did not refresh the auxiliary color family");
            }
            var scale = Root.XamlRoot.RasterizationScale;
            AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(930 * scale), (int)(620 * scale)));
            foreach (var language in Strings.Languages)
            {
                ApplySmokePreferences(preferences with { Language = language });
                Navigate("settings"); Root.UpdateLayout(); await Task.Delay(60);
                var section = Descendants(PageHost).OfType<StackPanel>().Single(panel => panel.Tag as string == "MaterialColorSettings");
                foreach (var picker in Descendants(section).OfType<ColorPicker>())
                    RequireHorizontalBounds(picker, section, "Dual custom picker/" + language);
                var preview = Descendants(section).OfType<StackPanel>().Single(panel => panel.Tag as string == "DualSourcePreview");
                foreach (var item in Descendants(preview).OfType<TextBlock>())
                    RequireHorizontalBounds(item, section, "Dual seed preview/" + language);
                if (language == "zh-CN")
                {
                    section.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
                    await CaptureAsync(Path.Combine(directory, "monet-dual-settings-compact.png"));
                }
            }
            ApplySmokePreferences(preferences with { MaterialColorStyle = "TonalSpot" });
            Navigate("settings"); Root.UpdateLayout();
            if (Descendants(PageHost).OfType<ColorPicker>().Count() != 1)
                throw new IOException("The second color picker remained visible in single-color mode");
        }
        finally { AppWindow.Resize(originalSize); ApplySmokePreferences(original); Navigate(originalPage); }
        static Windows.UI.Color Color(uint value) => Windows.UI.Color.FromArgb(255, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}
