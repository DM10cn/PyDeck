using System.Globalization;
using System.Text.Json;

namespace PimGui.Core;

public static class Strings
{
    public static readonly string[] Languages = ["en-US", "zh-CN", "zh-TW", "ja-JP"];
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Tables = Languages.ToDictionary(
        language => language, language =>
        {
            using var stream = typeof(Strings).Assembly.GetManifestResourceStream($"PimGui.Core.Strings.{language}.json")
                ?? throw new InvalidOperationException("Missing language resource: " + language);
            return (IReadOnlyDictionary<string, string>)(JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                ?? throw new InvalidOperationException("Invalid language resource: " + language));
        });
    public static string Language { get; set; } = "en-US";
    public static IReadOnlyDictionary<string, string> Table(string language) => Tables[language];
    public static string T(string key, params object[] args)
    {
        var table = Tables.TryGetValue(Language, out var selected) ? selected : Tables["en-US"];
        var text = table.TryGetValue(key, out var value) ? value : key;
        return args.Length == 0 ? text : string.Format(CultureInfo.GetCultureInfo(Tables.ContainsKey(Language) ? Language : "en-US"), text, args);
    }
}
