namespace PimGui.Core;

public static class RuntimeCatalog
{
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
