using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PimGui.Core;

// PIM's portable download format: index.json alongside local ZIP archives.
public sealed class OfflineBundle
{
    private sealed record Package(PythonRuntime Runtime, JsonObject Metadata, string Path, string Hash);
    private readonly IReadOnlyList<Package> packages;
    public string DirectoryPath { get; }
    public IReadOnlyList<PythonRuntime> Runtimes => packages.Select(p => p.Runtime).ToArray();
    private OfflineBundle(string directory, IReadOnlyList<Package> items) { DirectoryPath = directory; packages = items; }

    public static OfflineBundle Load(string directory)
    {
        directory = ExecutionPaths.LocalPath(directory);
        var index = Path.Combine(directory, "index.json");
        SafeFiles.RequireNoLinks(index);
        if (!File.Exists(index)) throw new IOException("Choose a folder containing index.json and the Python packages.");
        var json = SafeFiles.ReadText(index);
        var runtimes = RuntimeParser.Parse(json);
        var root = JsonNode.Parse(json)!.AsObject();
        // A signed/chained feed cannot be rewritten into a standalone unsigned snapshot.
        if (root.ContainsKey("next") || root.ContainsKey("requires_signature") || root.ContainsKey("source_settings"))
            throw new IOException("This index is not a standalone offline bundle. Download it with Python Install Manager first.");
        var versions = root["versions"]!.AsArray();
        var items = new List<Package>();
        for (var i = 0; i < runtimes.Count; i++)
        {
            var metadata = versions[i]!.AsObject();
            _ = PimClient.BuildArguments(RuntimeAction.Install, runtimes[i]);
            var relative = metadata["url"]?.GetValue<string>() ?? "";
            if (!IsRelativeFile(relative) || !relative.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Offline packages must be ZIP files inside the selected folder.");
            var path = Path.GetFullPath(Path.Combine(directory, relative));
            SafeFiles.RequireNoLinks(path);
            if (!File.Exists(path)) throw new IOException("An offline package is missing. Copy the complete bundle and try again.");
            var hash = metadata["hash"]?["sha256"]?.GetValue<string>() ?? "";
            if (!Regex.IsMatch(hash, @"\A[0-9a-fA-F]{64}\z"))
                throw new IOException("This offline package has no valid SHA-256 checksum.");
            items.Add(new(runtimes[i], (JsonObject)metadata.DeepClone(), path, hash));
        }
        if (items.Count == 0) throw new IOException("This offline bundle contains no Python versions.");
        return new(directory, items);
    }

    private static bool IsRelativeFile(string value) => value.Length > 0 && !Path.IsPathRooted(value) &&
        !value.Any(char.IsControl) && value.IndexOfAny([':', '%', '?', '#']) < 0 &&
        value.Split(['/', '\\']).All(part => part.Length > 0 && part is not "." and not ".." &&
            !part.EndsWith(' ') && !part.EndsWith('.') &&
            !Regex.IsMatch(part.Split('.')[0], @"\A(?:CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])\z", RegexOptions.IgnoreCase));

    public async Task<PreparedOfflinePackage> PrepareAsync(PythonRuntime runtime, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var package = packages.SingleOrDefault(p => p.Runtime == runtime)
            ?? throw new InvalidOperationException("This Python entry has changed. Refresh the list before trying again.");
        var prepared = new PreparedOfflinePackage();
        try
        {
            SafeFiles.RequireNoLinks(package.Path);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = new FileStream(package.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
            await using (var target = new FileStream(prepared.ArchivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                if (source.Length > 2L * 1024 * 1024 * 1024) throw new IOException("This offline package is too large.");
                var buffer = new byte[81920];
                int count;
                while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                { hash.AppendData(buffer, 0, count); await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken); }
            }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(package.Hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The offline package checksum does not match. Copy or download the bundle again.");
            using (var archive = ZipFile.OpenRead(prepared.ArchivePath))
            {
                if (archive.Entries.Count > 100_000) throw new IOException("This offline package is too large.");
                long expanded = 0;
                foreach (var entry in archive.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = entry.FullName.TrimEnd('/', '\\');
                    if (!IsRelativeFile(name) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                        throw new IOException("This offline package contains an unsafe file path.");
                    expanded = checked(expanded + entry.Length);
                    if (expanded > 8L * 1024 * 1024 * 1024) throw new IOException("This offline package is too large.");
                }
            }
            var metadata = (JsonObject)package.Metadata.DeepClone();
            cancellationToken.ThrowIfCancellationRequested();
            metadata["url"] = Path.GetFileName(prepared.ArchivePath);
            AtomicJson.Write(prepared.IndexPath, new JsonObject { ["versions"] = new JsonArray(metadata) }.ToJsonString());
            // Keep both primary and fallback sources local; do not modify the user's PIM configuration.
            AtomicJson.Write(prepared.ConfigPath, new JsonObject { ["install"] = new JsonObject
            { ["source"] = prepared.IndexPath, ["fallback_source"] = prepared.IndexPath } }.ToJsonString());
            return prepared;
        }
        catch { prepared.Dispose(); throw; }
    }
}

public sealed class PreparedOfflinePackage : IDisposable
{
    private readonly string directory;
    public string ArchivePath => Path.Combine(directory, "package.zip");
    public string IndexPath => Path.Combine(directory, "index.json");
    public string ConfigPath => Path.Combine(directory, "config.json");
    internal PreparedOfflinePackage()
    {
        directory = Path.Combine(Path.GetTempPath(), "PyDeck-offline-" + Guid.NewGuid().ToString("N"));
        SafeFiles.RequireNoLinks(directory);
        Directory.CreateDirectory(directory);
    }
    public void Dispose()
    {
        // Remove only our known files, never recursively delete a user-selected folder.
        try
        {
            SafeFiles.RequireNoLinks(directory);
            foreach (var path in new[] { ArchivePath, IndexPath, ConfigPath })
            { SafeFiles.RequireNoLinks(path); if (File.Exists(path)) File.Delete(path); }
            Directory.Delete(directory, recursive: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
