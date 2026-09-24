namespace PimGui.Core;

public static class RuntimeCatalog
{
    public static string MinorSeries(PythonRuntime runtime)
    {
        var match = System.Text.RegularExpressions.Regex.Match(runtime.Version, @"\A\d+\.\d+");
        return match.Success ? match.Value : runtime.Tag;
    }
    public static bool MatchesDistribution(PythonRuntime runtime, string filter) => filter switch
    {
        "FreeThreaded" => runtime.IsFreeThreaded, "Embedded" => runtime.IsEmbeddable, "Tests" => runtime.IncludesTests,
        "Other" => runtime.IsSpecialized && !runtime.IsFreeThreaded && !runtime.IsEmbeddable && !runtime.IncludesTests, _ => true
    };
    public static int CompareVersions(string left, string right)
    {
        var result = SortVersion(left).CompareTo(SortVersion(right));
        if (result != 0) return result;
        (int Stage, int Number) Suffix(string value)
        {
            var match = System.Text.RegularExpressions.Regex.Match(value, @"(?:\d)(a|b|rc)(\d+)");
            return match.Success ? (match.Groups[1].Value switch { "a" => 0, "b" => 1, _ => 2 }, int.Parse(match.Groups[2].Value)) : (3, 0);
        }
        return Suffix(left).CompareTo(Suffix(right));
    }
    public static IReadOnlyList<PythonRuntime> Filter(IEnumerable<PythonRuntime> source, string architecture, bool previews, string search) =>
        source.Where(r => (architecture == "All architectures" || r.Architecture == architecture) &&
            (previews || !r.IsPrerelease) && (search.Length == 0 ||
            $"{r.DisplayName} {r.Tag} {r.Version} {r.Company} {r.Distribution}".Contains(search, StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(r => SortVersion(r.Version)).ThenBy(r => r.IsPrerelease).ThenBy(r => r.IsSpecialized).ToArray();

    public static PythonRuntime? Recommended(IEnumerable<PythonRuntime> source, string preferredArchitecture) =>
        source.Where(r => !r.IsPrerelease && !r.IsSpecialized)
            .OrderByDescending(r => SortVersion(r.Version)).ThenByDescending(r => r.Architecture == preferredArchitecture).FirstOrDefault();

    private static Version SortVersion(string value)
    {
        var core = System.Text.RegularExpressions.Regex.Match(value, @"^\d+(?:\.\d+){1,3}").Value;
        return Version.TryParse(core, out var version) ? version : new Version(0, 0);
    }
}
