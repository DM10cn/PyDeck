using System.Text.RegularExpressions;

namespace PimGui.Core;

public static partial class RuntimeCatalog
{
    public static string MinorSeries(PythonRuntime runtime)
    {
        var match = MinorPattern().Match(runtime.Version);
        return match.Success ? match.Value : runtime.Tag;
    }
    public static bool MatchesDistribution(PythonRuntime runtime, string filter) => filter switch
    {
        "FreeThreaded" => runtime.IsFreeThreaded, "Embedded" => runtime.IsEmbeddable, "Tests" => runtime.IncludesTests,
        "Other" => runtime.IsSpecialized && !runtime.IsFreeThreaded && !runtime.IsEmbeddable && !runtime.IncludesTests, _ => true
    };
    public static int CompareVersions(string left, string right) => VersionKey(left).CompareTo(VersionKey(right));

    // Use this as the sort key, rather than reparsing both strings on every comparison.
    // No global cache: catalogs may come from arbitrary user-selected sources.
    public static (Version Core, int Stage, int Number) VersionKey(string value)
    {
        var core = CorePattern().Match(value).Value;
        var version = Version.TryParse(core, out var parsed) ? parsed : new Version(0, 0);
        var suffix = PreviewPattern().Match(value);
        return suffix.Success ? (version, suffix.Groups[1].Value.ToLowerInvariant() switch { "a" => 0, "b" => 1, _ => 2 },
            int.TryParse(suffix.Groups[2].Value, out var number) ? number : int.MaxValue) : (version, 3, 0);
    }
    public static IReadOnlyList<PythonRuntime> Filter(IEnumerable<PythonRuntime> source, string architecture, bool previews, string search) =>
        source.Where(r => (architecture == "All architectures" || r.Architecture == architecture) &&
            (previews || !r.IsPrerelease) && (search.Length == 0 ||
            $"{r.DisplayName} {r.Tag} {r.Version} {r.Company} {r.Distribution}".Contains(search, StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(r => VersionKey(r.Version)).ThenBy(r => r.IsSpecialized).ToArray();

    public static PythonRuntime? Recommended(IEnumerable<PythonRuntime> source, string preferredArchitecture)
    {
        PythonRuntime? best = null;
        (Version Core, int Stage, int Number) bestKey = default;
        foreach (var candidate in source)
        {
            if (candidate.IsPrerelease || candidate.IsSpecialized) continue;
            var key = VersionKey(candidate.Version);
            var comparison = best is null ? 1 : key.CompareTo(bestKey);
            if (comparison > 0 || comparison == 0 && candidate.Architecture == preferredArchitecture && best!.Architecture != preferredArchitecture)
            { best = candidate; bestKey = key; }
        }
        return best;
    }

    [GeneratedRegex(@"\A\d+\.\d+")]
    private static partial Regex MinorPattern();
    [GeneratedRegex(@"^\d+(?:\.\d+){1,3}")]
    private static partial Regex CorePattern();
    [GeneratedRegex(@"(?:\d)(a|b|rc)(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PreviewPattern();
}
