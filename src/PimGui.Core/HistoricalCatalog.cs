using System.Text;
using System.Text.Json.Nodes;

namespace PimGui.Core;

/// <summary>
/// Browse all versions from the source, including history pages. This metadata is not an
/// installation authority: PimClient re-resolves every selection through PIM before use.
/// </summary>
public static class HistoricalCatalog
{
    private const int MaxPageBytes = 8 * 1024 * 1024, MaxTotalBytes = 32 * 1024 * 1024;
    private const int MaxPages = 64, MaxEntries = 20_000;

    public static async Task<IReadOnlyList<PythonRuntime>> LoadAsync(HttpClient client, string source,
        Func<Uri, Task<bool>>? confirmOrigin = null, CancellationToken cancellationToken = default)
    {
        var index = InstallationSource.Validate(source);
        var next = new Uri(index);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var entries = new Dictionary<string, PythonRuntime>(StringComparer.OrdinalIgnoreCase);
        var totalBytes = 0;
        for (var page = 0; next is not null; page++)
        {
            if (page >= MaxPages || !visited.Add(next.AbsoluteUri)) throw new IOException("The Python catalog history is too large or contains a pagination loop");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            var (json, effectiveUrl, bytes) = await ReadAsync(client, next, confirmOrigin, timeout.Token);
            totalBytes += bytes;
            if (totalBytes > MaxTotalBytes) throw new IOException("The Python catalog history is too large");
            var root = JsonNode.Parse(json)?.AsObject() ?? throw new FormatException("Invalid Python catalog");
            var versions = root["versions"]?.AsArray() ?? throw new FormatException("Invalid Python catalog");
            foreach (var entry in versions)
            {
                if (entry is not JsonObject item) throw new FormatException("Invalid Python catalog entry");
                // Match PIM's relative URL resolution without granting the page permission to
                // download from another origin. PackageDownload asks before that connection.
                if (item["url"] is JsonValue value && value.TryGetValue<string>(out var url))
                    item["url"] = InstallationSource.Validate(new Uri(effectiveUrl, url).AbsoluteUri);
            }
            foreach (var runtime in RuntimeParser.ParseCatalog(root.ToJsonString()))
            {
                _ = PimClient.BuildArguments(RuntimeAction.Install, runtime);
                var item = runtime with { CatalogIndex = index };
                if (entries.TryGetValue(item.CatalogIdentity, out var previous))
                {
                    // Overlap between adjacent history pages is normal; conflicting identities
                    // are not. Never silently choose one of two different payloads.
                    if (previous.Company != item.Company || previous.Tag != item.Tag ||
                        !JsonNode.DeepEquals(JsonNode.Parse(previous.DownloadMetadata ?? "null"), JsonNode.Parse(item.DownloadMetadata ?? "null")))
                        throw new IOException("The Python catalog contains conflicting entries for the same version");
                }
                else entries.Add(item.CatalogIdentity, item);
                if (entries.Count > MaxEntries) throw new IOException("The Python catalog contains too many versions");
            }
            if (!root.TryGetPropertyValue("next", out var nextNode) || nextNode is null) break;
            if (nextNode is not JsonValue nextValue || !nextValue.TryGetValue<string>(out var nextText) || string.IsNullOrWhiteSpace(nextText))
                throw new FormatException("Invalid Python catalog history link");
            next = new Uri(InstallationSource.Validate(new Uri(effectiveUrl, nextText).AbsoluteUri));
            await RequireOriginAsync(effectiveUrl, next, confirmOrigin);
        }
        return entries.Values.OrderByDescending(r => r.Version, Comparer<string>.Create(RuntimeCatalog.CompareVersions)).ThenBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<(string Json, Uri Url, int Bytes)> ReadAsync(HttpClient client, Uri url,
        Func<Uri, Task<bool>>? confirmOrigin, CancellationToken token)
    {
        for (var hop = 0; hop < 6; hop++)
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            // Production clients disable automatic redirects. Also reject injected clients which
            // bypass that policy, instead of accidentally trusting an unseen final origin.
            if (response.RequestMessage?.RequestUri is { } actual && actual != url)
                throw new IOException("Automatic catalog redirects are not supported");
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location ?? throw new IOException("Invalid catalog redirect");
                var next = new Uri(InstallationSource.Validate(new Uri(url, location).AbsoluteUri));
                await RequireOriginAsync(url, next, confirmOrigin); url = next; continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxPageBytes) throw new IOException("The Python catalog page is too large");
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var destination = new MemoryStream();
            var buffer = new byte[65536];
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) != 0)
            {
                if (destination.Length + read > MaxPageBytes) throw new IOException("The Python catalog page is too large");
                destination.Write(buffer, 0, read);
            }
            return (new UTF8Encoding(false, true).GetString(destination.ToArray()), url, checked((int)destination.Length));
        }
        throw new IOException("Too many catalog redirects");
    }

    private static async Task RequireOriginAsync(Uri before, Uri after, Func<Uri, Task<bool>>? confirm)
    {
        if (!InstallationSource.SameOrigin(before, after) && (confirm is null || !await confirm(after)))
            throw new InvalidDataException("The catalog source was not approved");
    }
}
