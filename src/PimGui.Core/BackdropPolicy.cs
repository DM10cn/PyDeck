namespace PimGui.Core;

public static class BackdropPolicy
{
    public static bool RequestsEffects(AppSettings settings, bool windowsEffects, bool highContrast) =>
        settings.Design == "Fluent" && !highContrast &&
        (settings.Transparency == "On" || (settings.Transparency == "System" && windowsEffects));

    public static string Resolve(AppSettings settings, bool windowsEffects, bool highContrast, bool micaSupported, bool acrylicSupported)
    {
        if (!RequestsEffects(settings, windowsEffects, highContrast)) return "Solid";
        return settings.Backdrop switch { "Mica" when micaSupported => "Mica", "Acrylic" when acrylicSupported => "Acrylic", _ => "Solid" };
    }
}
