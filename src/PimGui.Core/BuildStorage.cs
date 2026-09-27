using System.Security.Cryptography;
using System.Text;

namespace PimGui.Core;

public sealed record StorageEntry(string Kind, string Path, long Bytes, string Fingerprint, string BuildId = "", int Users = 0);

/// <summary>Only explicit build-owned trees can be cleaned. Reviewed trees are rechecked before deletion.</summary>
public sealed class BuildStorage(string dataDirectory)
{
    private readonly BuildStore builds = new(dataDirectory);
    public IReadOnlyList<StorageEntry> Scan()
    {
        using var lease = builds.AcquireLease();
        return ScanUnlocked();
    }
    private IReadOnlyList<StorageEntry> ScanUnlocked()
    {
        var result = new List<StorageEntry>();
        void Add(string kind, string path, string id = "", int users = 0)
        {
            if (!Directory.Exists(path) && !File.Exists(path)) return;
            var snapshot = Snapshot(path);
            result.Add(new(kind, path, snapshot.Bytes, snapshot.Fingerprint, id, users));
        }
        Add("Source downloads", Path.Combine(builds.Root, "Cache"));
        var environments = new VirtualEnvironments(dataDirectory).Read();
        var tools = new BuildDependencies(dataDirectory);
        if (Directory.Exists(tools.DirectoryPath))
        {
            var prefix = Path.GetDirectoryName(tools.Executable)!;
            var runtime = new PythonRuntime("pydeck-bootstrap", "CPython", "3.14-64", BuildDependencies.Version, "Build Python", tools.Executable, prefix, false);
            Add("Build tools", tools.DirectoryPath, users: RuntimeUsage.Find(runtime, environments).Count);
        }
        foreach (var record in builds.Read().Where(r => !r.InProgress))
        {
            foreach (var name in new[] { "source", "source-detached", "temp", "import.tgz" })
                Add("Build temporary files", Path.Combine(builds.JobDirectory(record.Id), name), record.Id);
            var runtime = builds.RuntimeDirectory(record);
            if (Directory.Exists(runtime))
            {
                var marker = Path.Combine(runtime, ".pydeck-build-id"); SafeFiles.RequireNoLinks(marker);
                if (!File.Exists(marker) || SafeFiles.ReadText(marker).Trim() != record.Id) continue;
                Add("Built runtime", runtime, record.Id, RuntimeUsage.Find(builds.Runtime(record), environments).Count);
            }
        }
        return result;
    }
    public void Clean(StorageEntry reviewed)
    {
        using var lease = builds.AcquireLease();
        var lockPath = Path.Combine(dataDirectory, "environments.lock"); SafeFiles.RequireNoLinks(lockPath);
        using var environmentLease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        var current = ScanUnlocked().SingleOrDefault(e => e.Path == reviewed.Path && e.Kind == reviewed.Kind && e.BuildId == reviewed.BuildId)
            ?? throw new IOException("Storage changed; scan again");
        if (current.Fingerprint != reviewed.Fingerprint || current.Bytes != reviewed.Bytes) throw new IOException("Storage changed; scan again");
        if (current.Users != 0) throw new IOException("This runtime is used by registered environments");
        // Invalidate inventory before deletion so interruption cannot leave a partial runtime marked ready.
        if (current.Kind == "Built runtime")
        {
            var record = builds.Read().Single(r => r.Id == current.BuildId);
            builds.Save(record with { State = BuildState.Removed });
        }
        DeleteTree(current.Path);
    }
    internal static (long Bytes, string Fingerprint) Snapshot(string path)
    {
        var files = Entries(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long bytes = 0;
        foreach (var file in files)
        {
            var directory = Directory.Exists(file);
            var info = directory ? (FileSystemInfo)new DirectoryInfo(file) : new FileInfo(file);
            var size = directory ? 0 : ((FileInfo)info).Length; bytes = checked(bytes + size);
            hash.AppendData(Encoding.UTF8.GetBytes(file + "\0" + size + "\0" + info.LastWriteTimeUtc.Ticks + "\0" + (int)info.Attributes + "\n"));
        }
        return (bytes, Convert.ToHexString(hash.GetHashAndReset()));
    }
    private static List<string> Entries(string root)
    {
        root = ExecutionPaths.LocalPath(root); SafeFiles.RequireNoLinks(root);
        var result = new List<string>(); var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
        {
            var path = pending.Pop(); SafeFiles.RequireNoLinks(path);
            if (result.Count >= 300000) throw new IOException("Too many build files to scan");
            result.Add(path);
            if (Directory.Exists(path)) foreach (var child in Directory.EnumerateFileSystemEntries(path))
            {
                if (!Path.GetFullPath(child).StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid cleanup path");
                pending.Push(child);
            }
        }
        result.Sort(StringComparer.Ordinal); return result;
    }
    internal static void DeleteTree(string path)
    {
        // Validate the entire tree first, then delete individual entries without following reparse points.
        foreach (var entry in Entries(path).OrderByDescending(p => p.Length))
        {
            SafeFiles.RequireNoLinks(entry);
            if (Directory.Exists(entry)) Directory.Delete(entry, false);
            else { File.SetAttributes(entry, FileAttributes.Normal); File.Delete(entry); }
        }
    }
}
