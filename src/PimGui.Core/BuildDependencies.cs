using System.IO.Compression;
using System.Security.Cryptography;

namespace PimGui.Core;

/// <summary>Private CPython tools from the official Python NuGet package, without PATH or PIM registration.</summary>
public sealed class BuildDependencies(string dataDirectory)
{
    public const string Version = "3.14.7";
    public const string PackageUrl = "https://api.nuget.org/v3-flatcontainer/python/3.14.7/python.3.14.7.nupkg";
    // SHA-256 of the official python package fetched over HTTPS on 2026-09-27; no signature-verification claim.
    public const string Digest = "46A4DA5529A92D18FF894911F6E6033A8253198D705B8161BF28C9123C87D46B";
    public const string ToolsPage = "https://visualstudio.microsoft.com/visual-cpp-build-tools/";
    private readonly BuildStore builds = new(dataDirectory);
    public string DirectoryPath => Path.Combine(builds.Root, "Tools", "python-" + Version);
    public string Executable => Path.Combine(DirectoryPath, "tools", "python.exe");
    public string? Existing => File.Exists(Executable) ? Executable : null;
    public async Task<string> PrepareAsync(NetworkSettings network, string? password, PimOperation operation)
    {
        using var lease = builds.AcquireLease();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(operation.Token); timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var token = timeout.Token;
        var cache = Path.Combine(builds.Root, "Cache"); SafeFiles.RequireNoLinks(cache); Directory.CreateDirectory(cache);
        var package = Path.Combine(cache, "python-" + Version + ".nupkg"); SafeFiles.RequireNoLinks(package);
        if (!File.Exists(package))
        {
            var partial = package + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                using var client = network.CreateClient(password);
                await DownloadAsync(client, partial, operation, token);
                await BuildArchive.VerifyAsync(partial, Digest, token); File.Move(partial, package);
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }
        operation.Report(OperationPhase.Verifying);
        await BuildArchive.VerifyAsync(package, Digest, token);
        SafeFiles.RequireNoLinks(DirectoryPath);
        if (!Directory.Exists(DirectoryPath))
        {
            operation.Report(OperationPhase.Extracting);
            var stage = DirectoryPath + ".partial-" + Guid.NewGuid().ToString("N");
            SafeFiles.RequireNoLinks(stage); Directory.CreateDirectory(stage);
            try { Extract(package, stage, token); Directory.Move(stage, DirectoryPath); }
            finally { if (Directory.Exists(stage)) BuildStorage.DeleteTree(stage); }
        }
        // Verify reused files against the pinned archive before executing a cached interpreter.
        operation.Report(OperationPhase.Verifying);
        using (var zip = ZipFile.OpenRead(package))
            foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0))
            {
                token.ThrowIfCancellationRequested(); var target = EntryPath(DirectoryPath, entry); SafeFiles.RequireNoLinks(target);
                using var original = entry.Open(); using var actual = File.OpenRead(target);
                if (!SHA256.HashData(original).SequenceEqual(SHA256.HashData(actual))) throw new IOException("Build tools changed; remove the tools folder and prepare again");
            }
        CPythonBuilder.VerifyX64(Executable);
        await BuildProcess.RunAsync(Executable, ["-I", "-S", "-c", "import sys,ssl,zipfile,venv; assert sys.version_info[:3]==(3,14,7)"], null,
            Path.GetTempPath(), new Dictionary<string, string?>(), _ => { }, token);
        return Executable;
    }
    internal static async Task DownloadAsync(HttpClient client, string partial, PimOperation operation, CancellationToken token)
    {
        const long maximum = 100 * 1024 * 1024;
        await using var output = new FileStream(partial, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        long? total = null;
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, PackageUrl);
                var offset = output.Length;
                if (offset > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, null);
                using var headers = CancellationTokenSource.CreateLinkedTokenSource(token); headers.CancelAfter(TimeSpan.FromMinutes(2));
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token);
                response.EnsureSuccessStatusCode();
                if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (offset == 0 || range?.From != offset || range.Length is null || (total is not null && range.Length != total)) throw new InvalidDataException("Invalid build tools download range");
                    total = range.Length;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.OK) { output.SetLength(0); output.Position = 0; offset = 0; total = response.Content.Headers.ContentLength; }
                else throw new InvalidDataException("Invalid build tools response");
                if (total > maximum) throw new InvalidDataException("Build tools download is too large");
                await using var input = await response.Content.ReadAsStreamAsync(token);
                var buffer = new byte[65536]; var clock = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(TimeSpan.FromMinutes(2));
                    var n = await input.ReadAsync(buffer, idle.Token); if (n == 0) break;
                    if (output.Length + n > maximum) throw new InvalidDataException("Build tools download is too large");
                    await output.WriteAsync(buffer.AsMemory(0, n), token);
                    operation.Transfer(output.Length, total, (output.Length - offset) / Math.Max(.001, clock.Elapsed.TotalSeconds), null);
                }
                if (total is not null && output.Length != total) throw new IOException("Incomplete build tools download");
                return;
            }
            catch (Exception ex) when (attempt < 2 && !token.IsCancellationRequested && ex is not InvalidDataException &&
                ex is HttpRequestException or IOException or OperationCanceledException)
            {
                await output.FlushAsync(token); await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token);
            }
        }
    }
    internal static string EntryPath(string root, ZipArchiveEntry entry)
    {
        var name = entry.FullName.Replace('\\', '/').TrimEnd('/');
        if (name.Length == 0 || name.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) ||
            ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            throw new IOException("Unsafe build tools archive");
        var path = Path.GetFullPath(Path.Combine(root, name));
        if (!path.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe build tools archive");
        return path;
    }
    internal static void Extract(string package, string root, CancellationToken token)
    {
        using var zip = ZipFile.OpenRead(package); long size = 0;
        if (zip.Entries.Count > 30000) throw new IOException("Too many build tools files");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested(); var target = EntryPath(root, entry);
            if (!seen.Add(target) || (size = checked(size + entry.Length)) > 600 * 1024 * 1024) throw new IOException("Invalid build tools archive");
            if (entry.Name.Length == 0) Directory.CreateDirectory(target);
            else { Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target, false); }
        }
    }
}
