using System.Text.Json;

namespace PimGui.Core;

public enum BuildState { Preparing, Downloading, Extracting, Compiling, Assembling, Validating, Ready, Failed, Cancelled, Interrupted, Removed, Training }
public sealed record BuildRecord(string Id, BuildOptions Options, BuildState State, DateTimeOffset Started,
    DateTimeOffset? Finished = null, BuildToolchain? Toolchain = null, string Error = "")
{
    public string SourceUrl => Options.SourceArchive.Length > 0 ? "Local archive" : BuildSources.Url(Options.Version);
    public string SourceSha256 { get; init; } = "";
    public string SourceVerification { get; init; } = "";
    public bool InProgress => State is BuildState.Preparing or BuildState.Downloading or BuildState.Extracting or BuildState.Compiling or BuildState.Assembling or BuildState.Validating or BuildState.Training;
}

/// <summary>Paths are derived from validated IDs, never accepted from the persisted manifest.</summary>
public sealed class BuildStore
{
    public string Root { get; }
    public BuildStore(string dataDirectory)
    {
        Root = ExecutionPaths.LocalPath(Path.Combine(dataDirectory, "Builds"));
        SafeFiles.RequireNoLinks(Root);
    }
    private static string ValidateId(string id) => Guid.TryParseExact(id, "N", out _) ? id : throw new IOException("Invalid build ID");
    public string JobDirectory(string id) => Path.Combine(Root, "Jobs", ValidateId(id));
    public string RuntimeDirectory(string id) => Path.Combine(Root, "Runtimes", ValidateId(id));
    public string RuntimeDirectory(BuildRecord record) => record.Options.OutputParent.Length == 0 ? RuntimeDirectory(record.Id)
        : Path.Combine(BuildRecipe.BatchPath(record.Options.OutputParent), "CPython-" + record.Options.Version + "-" + ValidateId(record.Id));
    public string LogPath(string id) => Path.Combine(JobDirectory(id), "build.log");
    public FileStream AcquireLease()
    {
        SafeFiles.RequireNoLinks(Root); Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, "build.lock"); SafeFiles.RequireNoLinks(path);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new IOException("Another Python build is running", ex); }
    }
    public void Save(BuildRecord record)
    {
        record.Options.Validate();
        var directory = JobDirectory(record.Id); SafeFiles.RequireNoLinks(directory); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "build.json"); SafeFiles.RequireNoLinks(path);
        AtomicJson.Write(path, JsonSerializer.Serialize(record), File.Exists(path) ? SafeFiles.ReadText(path) : null, checkOriginal: true);
    }
    public IReadOnlyList<BuildRecord> Read()
    {
        var root = Path.Combine(Root, "Jobs"); SafeFiles.RequireNoLinks(root);
        if (!Directory.Exists(root)) return [];
        var result = new List<BuildRecord>();
        foreach (var directory in Directory.EnumerateDirectories(root).Take(1001))
        {
            if (result.Count == 1000) throw new IOException("Build history is too large");
            var id = Path.GetFileName(directory); ValidateId(id);
            var path = Path.Combine(directory, "build.json"); SafeFiles.RequireNoLinks(path);
            if (!File.Exists(path)) continue;
            var record = JsonSerializer.Deserialize<BuildRecord>(SafeFiles.ReadText(path)) ?? throw new IOException("Invalid build record");
            if (record.Id != id || !Enum.IsDefined(record.State)) throw new IOException("Invalid build record");
            record.Options.Validate(); result.Add(record);
        }
        return result.OrderByDescending(r => r.Started).ToArray();
    }
    public void RecoverInterrupted()
    {
        FileStream lease;
        try { lease = AcquireLease(); } catch (IOException) { return; }
        using (lease)
            foreach (var record in Read().Where(r => r.InProgress))
                Save(record with { State = BuildState.Interrupted, Finished = DateTimeOffset.UtcNow });
    }
    public IReadOnlyList<PythonRuntime> Runtimes() => Read().Where(r => r.State == BuildState.Ready).Select(Runtime).ToArray();
    public PythonRuntime Runtime(BuildRecord record)
    {
        var prefix = RuntimeDirectory(record); SafeFiles.RequireNoLinks(prefix);
        return new("local-build-" + record.Id, "CPython", record.Options.Version + "-64", record.Options.Version,
            "Python " + record.Options.Version + " · " + record.Options.Configuration + " · " + record.Id[..8], Path.Combine(prefix, record.Options.ExecutableName), prefix, false)
            { LocalBuildId = record.Id, BuildConfiguration = record.Options };
    }
    public void Remove(string id)
    {
        using var lease = AcquireLease();
        var record = Read().Single(r => r.Id == id);
        if (record.State != BuildState.Ready) throw new IOException("Only a ready runtime can be removed from the list");
        Save(record with { State = BuildState.Removed });
    }
    public IReadOnlyList<PythonRuntime> Merge(IEnumerable<PythonRuntime> managed)
    {
        var local = Runtimes();
        return managed.Where(r => !local.Any(l => string.Equals(l.Executable, r.Executable, StringComparison.OrdinalIgnoreCase)))
            .Concat(local).ToArray();
    }
}
