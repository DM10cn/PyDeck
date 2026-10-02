namespace PimGui.Core;

// One immutable projection per loaded catalog. A new inventory replaces the projection;
// filtering never retains results or interprets a cached entry as an installation preflight.
public sealed class RuntimeCatalogSnapshot
{
    private sealed record Entry(PythonRuntime Runtime, string Architecture, bool Preview, bool Specialized,
        string SearchText, (Version Core, int Stage, int Number) Version);
    private readonly Entry[] entries;

    public RuntimeCatalogSnapshot(IEnumerable<PythonRuntime> source)
    {
        entries = source.Select(runtime => new Entry(runtime, runtime.Architecture, runtime.IsPrerelease,
            runtime.IsSpecialized, $"{runtime.DisplayName} {runtime.Tag} {runtime.Version} {runtime.Company} {runtime.Distribution}",
            RuntimeCatalog.VersionKey(runtime.Version)))
            .OrderByDescending(entry => entry.Version).ThenBy(entry => entry.Specialized).ToArray();
    }

    public IReadOnlyList<PythonRuntime> Filter(string architecture, bool previews, string search, string distribution = "All")
    {
        var matches = new List<PythonRuntime>();
        foreach (var entry in entries)
        {
            if (architecture != "All architectures" && entry.Architecture != architecture || !previews && entry.Preview ||
                search.Length > 0 && !entry.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
            if (distribution == "Standard" ? entry.Specialized : !RuntimeCatalog.MatchesDistribution(entry.Runtime, distribution)) continue;
            matches.Add(entry.Runtime);
        }
        return matches.ToArray();
    }
}
