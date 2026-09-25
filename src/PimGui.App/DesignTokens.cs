using Windows.UI;

namespace PimGui.App;

// Style decisions live here. Pages consume semantic roles, never a design-language flag.
internal sealed record DesignTokens
{
    public required string Name { get; init; }
    public bool SupportsEffects { get; init; }
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

    public DesignTokens WithBackdrop(bool active)
    {
        if (!SupportsEffects || !active) return this;
        // Translucent paint above one native backdrop. Do not fade text or stack blur controllers.
        static Color Layer(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
        return this with { Surface = Layer(Surface, 110), Card = Layer(Card, 82), Hero = Layer(Hero, 90),
            ControlFill = Layer(ControlFill, 130), NavigationSelected = Layer(NavigationSelected, 120) };
    }

    public static DesignTokens For(string design, bool light)
    {
        Color Pick(string day, string night) => Palette.C(light ? day : night);
        return design == "Fluent" ? new()
        {
            Name = "Windows Fluent", SupportsEffects = true,
            Shell = Pick("F3F3F3", "202020"), Surface = Pick("F9F9F9", "272727"), Card = Pick("FFFFFF", "303030"), Hero = Pick("FFFFFF", "303030"), ControlFill = Pick("FFFFFF", "3A3A3A"),
            Text = Pick("1A1A1A", "FFFFFF"), Muted = Pick("5D5D5D", "C5C5C5"), Accent = Pick("0067C0", "60CDFF"), OnAccent = Pick("FFFFFF", "003E5A"),
            AccentContainer = Pick("E4F0FA", "243B47"), NavigationSelected = Pick("E5E5E5", "383838"), Line = Pick("E5E5E5", "414141"), Green = Pick("0F6B42", "6CCB9F"),
            CardRadius = 8, SurfaceRadius = 8, ActionRadius = 4, InputRadius = 4, ChipRadius = 4, IconRadius = 6, NavigationRadius = 4,
            CardBorder = 1, NavigationIndicator = 3, PageTitleSize = 28, HeroTitleSize = 28, SectionSpacing = 20
        } : new()
        {
            Name = "Material 3 Expressive", SupportsEffects = false,
            Shell = Pick("EDF3FA", "10151E"), Surface = Pick("F8FAFE", "161D28"), Card = Pick("FFFFFF", "1D2735"), Hero = Pick("DCEBFF", "243E5F"), ControlFill = Pick("F0F4FA", "293441"),
            Text = Pick("182636", "E9F0FC"), Muted = Pick("54677D", "A2B1C8"), Accent = Pick("086BCC", "87BCFF"), OnAccent = Pick("FFFFFF", "052F59"),
            AccentContainer = Pick("DCEBFF", "243E5F"), NavigationSelected = Pick("DCEBFF", "243E5F"), Line = Pick("DCE4EF", "2E3A4D"), Green = Pick("146A4C", "8CDABB"),
            CardRadius = 22, SurfaceRadius = 28, ActionRadius = 8, InputRadius = 18, ChipRadius = 12, IconRadius = 16, NavigationRadius = 24,
            CardBorder = 0, NavigationIndicator = 0, PageTitleSize = 28, HeroTitleSize = 32, SectionSpacing = 20
        };
    }
}
