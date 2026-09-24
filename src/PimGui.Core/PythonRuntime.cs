using System.Text.Json;
using System.Text.RegularExpressions;

namespace PimGui.Core;

public sealed record PythonRuntime(string Id, string Company, string Tag, string Version,
    string DisplayName, string Executable, string Prefix, bool IsDefault, bool IsManaged = false)
{
    public string? DownloadMetadata { get; init; }
    public string? CatalogIndex { get; init; }
    public string Architecture => Tag.Contains("arm64", StringComparison.OrdinalIgnoreCase) ? "ARM64"
        : Tag.EndsWith("-32", StringComparison.OrdinalIgnoreCase) ? "x86" : "x64";
    public bool IsPrerelease => Regex.IsMatch(Version, @"\d(?:a|b|rc)\d", RegexOptions.IgnoreCase)
        || Tag.Contains("dev", StringComparison.OrdinalIgnoreCase);
    public bool IsFreeThreaded => Regex.IsMatch(Tag, @"^\d+\.\d+t(?:-|$)");
    public string Selector => $"{Company}/{Tag}";
    public bool IsEmbeddable => Company.Equals("PythonEmbed", StringComparison.OrdinalIgnoreCase);
    public bool IncludesTests => Company.Equals("PythonTest", StringComparison.OrdinalIgnoreCase);
    public bool IsSpecialized => IsFreeThreaded || IsEmbeddable || IncludesTests || !Company.Equals("PythonCore", StringComparison.OrdinalIgnoreCase);
    public string Distribution => IsEmbeddable ? "Embeddable" : IncludesTests ? "With tests" : IsFreeThreaded ? "Free-threaded" : Company;
    public string Channel => IsPrerelease ? "Preview" : "Stable";
}

public static class RuntimeParser
{
    public static IReadOnlyList<PythonRuntime> Parse(string json)
    {
        try
        {
            if (json.Length > 8 * 1024 * 1024) throw new FormatException("The Python list is too large to read safely.");
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("versions", out var versions) ||
                versions.ValueKind != JsonValueKind.Array)
                throw new FormatException("Python Install Manager returned an unrecognized list format.");
            var result = new List<PythonRuntime>();
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in versions.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) throw new FormatException("A Python entry is not an object.");
                string Get(string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? "" : "";
                var tag = Get("tag");
                var id = Get("id");
                var company = Get("company");
                if (string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(company))
                    throw new FormatException("A Python entry is missing its ID, company, or version tag.");
                var version = Get("sort-version");
                if (!identities.Add(id)) throw new FormatException("Python Install Manager returned duplicate runtime IDs.");
                var title = Get("display-name");
                result.Add(new(id, company, tag, version.Length > 0 ? version : tag,
                    title.Length > 0 ? title : $"{company} {tag}", Get("executable"), Get("prefix"),
                    item.TryGetProperty("default", out var isDefault) && isDefault.ValueKind == JsonValueKind.True)
                    { DownloadMetadata = item.TryGetProperty("url", out _) && item.TryGetProperty("hash", out _) ? item.GetRawText() : null });
            }
            return result;
        }
        catch (JsonException ex) { throw new FormatException("Python Install Manager returned invalid JSON.", ex); }
    }
}
