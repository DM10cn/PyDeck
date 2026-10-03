using Windows.UI;
using PimGui.Core;

namespace PimGui.App;

internal enum TextRole { Display, Headline, Title, Section, BodyLarge, Body, Caption, Label }
internal readonly record struct TypographyToken(double Size, double LineHeight, bool Emphasized = false);

// Style decisions live here. Pages consume semantic roles, never a design-language flag.
internal sealed record DesignTokens
{
    public required string Name { get; init; }
    public string Design { get; init; } = "Material";
    public bool HighContrast { get; init; }
    public bool SupportsEffects { get; init; }
    public bool SupportsDynamicColor { get; init; }
    public MonetScheme? MaterialScheme { get; init; }
    public required Color Shell { get; init; }
    public required Color Surface { get; init; }
    public required Color Card { get; init; }
    public required Color ControlFill { get; init; }
    public required Color Hero { get; init; }
    public required Color Text { get; init; }
    public required Color Muted { get; init; }
    public required Color Accent { get; init; }
    public required Color OnAccent { get; init; }
    public required Color AccentContainer { get; init; }
    public required Color NavigationSelected { get; init; }
    public required Color Line { get; init; }
    public required Color Green { get; init; }
    public Color Secondary { get; init; }
    public Color OnSecondary { get; init; }
    public Color SecondaryContainer { get; init; }
    public Color OnSecondaryContainer { get; init; }
    public Color Error { get; init; }
    public Color OnError { get; init; }
    public Color SurfaceHighest { get; init; }
    public Color Outline { get; init; }
    public Color Primary { get; init; }
    public Color OnPrimary { get; init; }
    public Color PrimaryContainer { get; init; }
    public Color OnPrimaryContainer { get; init; }
    public Color OnAccentContainer { get; init; }
    public Color NavigationForeground { get; init; }
    public Color Tertiary { get; init; }
    public Color OnTertiary { get; init; }
    public Color TertiaryContainer { get; init; }
    public Color OnTertiaryContainer { get; init; }
    public Color OnSurface { get; init; }
    public Color SurfaceVariant { get; init; }
    public Color OnSurfaceVariant { get; init; }
    public Color SurfaceDim { get; init; }
    public Color SurfaceBright { get; init; }
    public Color SurfaceContainerLowest { get; init; }
    public Color SurfaceContainerLow { get; init; }
    public Color SurfaceContainer { get; init; }
    public Color SurfaceContainerHigh { get; init; }
    public Color SurfaceContainerHighest { get; init; }
    public Color OutlineVariant { get; init; }
    public Color ErrorContainer { get; init; }
    public Color OnErrorContainer { get; init; }
    public Color InverseSurface { get; init; }
    public Color InverseOnSurface { get; init; }
    public Color InversePrimary { get; init; }
    public Color SurfaceTint { get; init; }
    public Color Background { get; init; }
    public Color OnBackground { get; init; }
    public Color Shadow { get; init; }
    public Color Scrim { get; init; }
    public Color PrimaryFixed { get; init; }
    public Color PrimaryFixedDim { get; init; }
    public Color OnPrimaryFixed { get; init; }
    public Color OnPrimaryFixedVariant { get; init; }
    public Color SecondaryFixed { get; init; }
    public Color SecondaryFixedDim { get; init; }
    public Color OnSecondaryFixed { get; init; }
    public Color OnSecondaryFixedVariant { get; init; }
    public Color TertiaryFixed { get; init; }
    public Color TertiaryFixedDim { get; init; }
    public Color OnTertiaryFixed { get; init; }
    public Color OnTertiaryFixedVariant { get; init; }
    public double HoverStateOpacity { get; init; } = .08;
    public double PressedStateOpacity { get; init; } = .12;
    public double DisabledContainerOpacity { get; init; } = .12;
    public double DisabledTextOpacity { get; init; } = .38;
    public double StateDurationMs { get; init; } = 100;
    public double PressedActionRadius { get; init; } = 4;
    public double CompactControlHeight { get; init; } = 28;
    public double CompactHorizontalPadding { get; init; } = 10;
    public double CompactVerticalPadding { get; init; } = 3;
    public double CompactIconSize { get; init; } = 14;
    public string FontFamily { get; init; } = "Segoe UI Variable, Segoe UI, Microsoft YaHei UI, Yu Gothic UI, Microsoft JhengHei UI";
    public double CardRadius { get; init; }
    public double SurfaceRadius { get; init; }
    public double ActionRadius { get; init; }
    public double InputRadius { get; init; }
    public double ChipRadius { get; init; }
    public double IconRadius { get; init; }
    public double NavigationRadius { get; init; }
    public double CardBorder { get; init; }
    public double NavigationIndicator { get; init; }
    public double PageTitleSize { get; init; }
    public double HeroTitleSize { get; init; }
    public double SectionSpacing { get; init; }
    public double ContentWidth { get; init; } = 1160;
    public double ControlHeight { get; init; } = 32;
    public double ControlFontSize { get; init; } = 14;
    public double ControlIconSize { get; init; } = 16;
    public double ControlHorizontalPadding { get; init; } = 12;
    public double ControlVerticalPadding { get; init; } = 4;
    public double ControlSpacing { get; init; } = 8;
    public double ToolbarSpacing { get; init; } = 12;
    public double RowPadding { get; init; } = 12;
    public double BodyFontSize { get; init; } = 14;
    public double CaptionFontSize { get; init; } = 12;
    public double SectionTitleSize { get; init; } = 18;

    public TypographyToken Typography(TextRole role) => role switch
    {
        TextRole.Display => new(HeroTitleSize, HeroTitleSize == 32 ? 40 : 36, true),
        TextRole.Headline or TextRole.Title => new(PageTitleSize, Design == "Fluent" ? 36 : 38, true),
        TextRole.Section => new(SectionTitleSize, Design == "Fluent" ? 28 : 24, true),
        TextRole.BodyLarge => new(16, 24),
        TextRole.Body => new(BodyFontSize, 20),
        TextRole.Caption => new(CaptionFontSize, 16),
        _ => new(ControlFontSize, 20, Design != "Fluent")
    };

    public double LineHeightFor(double size) => size switch
    {
        <= 12 => 16, <= 14 => 20, <= 16 => 24, <= 20 => 28, <= 24 => 32, <= 28 => 36,
        <= 32 => 40, _ => Math.Ceiling(size * 1.25 / 4) * 4
    };

    public DesignTokens WithBackdrop(bool active)
    {
        if (!SupportsEffects || !active) return this;
        // Translucent paint above one native backdrop. Do not fade text or stack blur controllers.
        static Color Layer(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
        return this with { Surface = Layer(Surface, 110), Card = Layer(Card, 82), Hero = Layer(Hero, 90),
            ControlFill = Layer(ControlFill, 130), NavigationSelected = Layer(NavigationSelected, 120) };
    }

    public static DesignTokens For(string design, bool light, uint seed = 0xFF1B6EF3u, string variant = "TonalSpot", uint? secondSeed = null)
    {
        Color Pick(string day, string night) => Palette.C(light ? day : night);
        if (design == "Fluent") return new()
        {
            Name = "Windows Fluent", Design = "Fluent", SupportsEffects = true, SupportsDynamicColor = false,
            Shell = Pick("F3F3F3", "202020"), Surface = Pick("F9F9F9", "272727"), Card = Pick("FFFFFF", "303030"), Hero = Pick("FFFFFF", "303030"), ControlFill = Pick("FFFFFF", "3A3A3A"),
            Text = Pick("1A1A1A", "FFFFFF"), Muted = Pick("5D5D5D", "C5C5C5"), Accent = Pick("0067C0", "60CDFF"), OnAccent = Pick("FFFFFF", "003E5A"),
            AccentContainer = Pick("E4F0FA", "243B47"), NavigationSelected = Pick("E5E5E5", "383838"), Line = Pick("E5E5E5", "414141"), Green = Pick("0F6B42", "6CCB9F"),
            Secondary = Pick("5D5D5D", "C5C5C5"), SecondaryContainer = Pick("E9E9E9", "383838"), OnSecondaryContainer = Pick("1A1A1A", "FFFFFF"),
            Error = Pick("C42B1C", "FF99A4"), OnError = Pick("FFFFFF", "4A0711"), SurfaceHighest = Pick("FFFFFF", "3A3A3A"), Outline = Pick("858585", "9A9A9A"),
            OnAccentContainer = Pick("0067C0", "60CDFF"), NavigationForeground = Pick("0067C0", "60CDFF"),
            CardRadius = 8, SurfaceRadius = 8, ActionRadius = 4, InputRadius = 4, ChipRadius = 4, IconRadius = 6, NavigationRadius = 4,
            CardBorder = 1, NavigationIndicator = 3, PageTitleSize = 28, HeroTitleSize = 28, SectionSpacing = 20,
            SectionTitleSize = 20, PressedActionRadius = 4
        };

        // HCT palettes, tone deltas, contrast and all 49 Material roles come from official MCU.
        // MD3 Expressive is the component language; "Expressive" here is a separate opt-in
        // color-scheme variant. Tonal Spot / spec 2021 is the wallpaper-color default.
        var scheme = MonetColors.Create(seed, !light, variant, secondSeed);
        static Color Argb(uint value) => Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return new()
        {
            Name = "Material 3 Expressive", Design = "Material", SupportsEffects = false, SupportsDynamicColor = true, MaterialScheme = scheme,
            Shell = Argb(scheme.SurfaceContainerLowest), Surface = Argb(scheme.Surface), Card = Argb(scheme.SurfaceContainerLow),
            Hero = Argb(scheme.PrimaryContainer), ControlFill = Argb(scheme.SurfaceContainerHigh),
            Text = Argb(scheme.OnSurface), Muted = Argb(scheme.OnSurfaceVariant), Accent = Argb(scheme.Primary), OnAccent = Argb(scheme.OnPrimary),
            AccentContainer = Argb(scheme.PrimaryContainer), OnAccentContainer = Argb(scheme.OnPrimaryContainer),
            NavigationSelected = Argb(scheme.SecondaryContainer), NavigationForeground = Argb(scheme.OnSecondaryContainer), Line = Argb(scheme.OutlineVariant),
            // The legacy Green name is a supporting-status role; text/icons also carry meaning.
            Green = Argb(scheme.Tertiary),
            Primary = Argb(scheme.Primary), OnPrimary = Argb(scheme.OnPrimary), PrimaryContainer = Argb(scheme.PrimaryContainer), OnPrimaryContainer = Argb(scheme.OnPrimaryContainer),
            Secondary = Argb(scheme.Secondary), OnSecondary = Argb(scheme.OnSecondary), SecondaryContainer = Argb(scheme.SecondaryContainer), OnSecondaryContainer = Argb(scheme.OnSecondaryContainer),
            Tertiary = Argb(scheme.Tertiary), OnTertiary = Argb(scheme.OnTertiary), TertiaryContainer = Argb(scheme.TertiaryContainer), OnTertiaryContainer = Argb(scheme.OnTertiaryContainer),
            OnSurface = Argb(scheme.OnSurface), SurfaceVariant = Argb(scheme.SurfaceVariant), OnSurfaceVariant = Argb(scheme.OnSurfaceVariant),
            SurfaceDim = Argb(scheme.SurfaceDim), SurfaceBright = Argb(scheme.SurfaceBright), SurfaceContainerLowest = Argb(scheme.SurfaceContainerLowest),
            SurfaceContainerLow = Argb(scheme.SurfaceContainerLow), SurfaceContainer = Argb(scheme.SurfaceContainer), SurfaceContainerHigh = Argb(scheme.SurfaceContainerHigh),
            SurfaceContainerHighest = Argb(scheme.SurfaceContainerHighest), SurfaceHighest = Argb(scheme.SurfaceContainerHighest), Outline = Argb(scheme.Outline), OutlineVariant = Argb(scheme.OutlineVariant),
            Error = Argb(scheme.Error), OnError = Argb(scheme.OnError), ErrorContainer = Argb(scheme.ErrorContainer), OnErrorContainer = Argb(scheme.OnErrorContainer),
            InverseSurface = Argb(scheme.InverseSurface), InverseOnSurface = Argb(scheme.InverseOnSurface), InversePrimary = Argb(scheme.InversePrimary),
            SurfaceTint = Argb(scheme.SurfaceTint), Background = Argb(scheme.Background), OnBackground = Argb(scheme.OnBackground), Shadow = Argb(scheme.Shadow), Scrim = Argb(scheme.Scrim),
            PrimaryFixed = Argb(scheme.PrimaryFixed), PrimaryFixedDim = Argb(scheme.PrimaryFixedDim), OnPrimaryFixed = Argb(scheme.OnPrimaryFixed), OnPrimaryFixedVariant = Argb(scheme.OnPrimaryFixedVariant),
            SecondaryFixed = Argb(scheme.SecondaryFixed), SecondaryFixedDim = Argb(scheme.SecondaryFixedDim), OnSecondaryFixed = Argb(scheme.OnSecondaryFixed), OnSecondaryFixedVariant = Argb(scheme.OnSecondaryFixedVariant),
            TertiaryFixed = Argb(scheme.TertiaryFixed), TertiaryFixedDim = Argb(scheme.TertiaryFixedDim), OnTertiaryFixed = Argb(scheme.OnTertiaryFixed), OnTertiaryFixedVariant = Argb(scheme.OnTertiaryFixedVariant),
            CardRadius = 16, SurfaceRadius = 24, ActionRadius = 20, InputRadius = 12, ChipRadius = 8, IconRadius = 16, NavigationRadius = 22,
            CardBorder = 0, NavigationIndicator = 0, PageTitleSize = 30, HeroTitleSize = 32, SectionSpacing = 20,
            ControlHeight = 40, ControlFontSize = 14, ControlIconSize = 20, ControlHorizontalPadding = 16, ControlVerticalPadding = 8,
            CompactControlHeight = 32, CompactHorizontalPadding = 12, CompactVerticalPadding = 4, CompactIconSize = 18,
            ControlSpacing = 8, RowPadding = 12, ToolbarSpacing = 12, SectionTitleSize = 18, PressedActionRadius = 12
        };
    }
}
