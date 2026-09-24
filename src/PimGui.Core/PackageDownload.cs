using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Http.Headers;

namespace PimGui.Core;

public static class TransferUnits
{
    public static string Bytes(double value) => value >= 1024 * 1024 ? $"{value / (1024 * 1024):0.0} MB" : $"{value / 1024:0.0} KB";
}

/// <summary>PIM supplies and validates catalog metadata; PIM still performs all installations.</summary>
public static class PackageDownload
{
    public static async Task<OfflineBundle> FetchAsync(PythonRuntime runtime, string directory, HttpClient client, PimOperation? operation, Func<Uri, Task<bool>>? confirmOrigin = null)
    {
        _ = PimClient.BuildArguments(RuntimeAction.Install, runtime);
        var metadata = JsonNode.Parse(runtime.DownloadMetadata ?? throw new IOException("Download details are unavailable"))!.AsObject();
        var url = new Uri(InstallationSource.Validate(metadata["url"]!.GetValue<string>()));
        var origin = new Uri(runtime.CatalogIndex ?? InstallationSource.Official);
        if (!InstallationSource.SameOrigin(origin, url) && !(runtime.CatalogIndex is null && url.Host is "python.org" or "www.python.org") &&
            (confirmOrigin is null || !await confirmOrigin(url))) throw new InvalidDataException("The download source was not approved");
        var expected = metadata["hash"]?["sha256"]?.GetValue<string>() ?? "";
        if (!Regex.IsMatch(expected, "\\A[0-9a-fA-F]{64}\\z")) throw new IOException("The package has no valid SHA-256 checksum");
        SafeFiles.RequireNoLinks(directory);
        Directory.CreateDirectory(directory);
        var archive = Path.Combine(directory, "package.zip");
        var token = operation?.Token ?? default;
        long? total = null;
        const long maximum = 2L * 1024 * 1024 * 1024;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long received = 0, previousBytes = 0;
        double? speed = null;
        var clock = Stopwatch.StartNew(); var previousTime = 0.0; var transferStart = 0.0;
        operation?.Transfer(0, total, null, null);
        await using (var destination = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
        {
            var buffer = new byte[65536];
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    headersTimeout.CancelAfter(TimeSpan.FromMinutes(2));
                    using var response = await SendAsync(client, url, received, confirmOrigin, headersTimeout.Token);
                    response.EnsureSuccessStatusCode();
                    if (received > 0 && response.StatusCode == HttpStatusCode.PartialContent)
                    {
                        var range = response.Content.Headers.ContentRange;
                        if (range?.From != received || (total is not null && range.Length != total)) throw new InvalidDataException("Invalid download range");
                    }
                    else
                    {
                        if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("Unexpected download response");
                        if (received > 0) { destination.SetLength(0); destination.Position = 0; hash.GetHashAndReset(); received = previousBytes = 0; speed = null; previousTime = clock.Elapsed.TotalSeconds; }
                        total = response.Content.Headers.ContentLength;
                    }
                    if (total > maximum) throw new InvalidDataException("This package is too large");
                    // Connection setup and retry pauses are not a sample of transfer speed.
                    transferStart = previousTime = clock.Elapsed.TotalSeconds;
                    previousBytes = received; speed = null;
                    await using var source = await response.Content.ReadAsStreamAsync(token);
                    while (true)
                    {
                        using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                        idle.CancelAfter(TimeSpan.FromMinutes(2));
                        var count = await source.ReadAsync(buffer, idle.Token);
                        if (count == 0) break;
                        received += count;
                        if (received > maximum) throw new InvalidDataException("This package is too large");
                        hash.AppendData(buffer, 0, count);
                        await destination.WriteAsync(buffer.AsMemory(0, count), token);
                        var elapsed = clock.Elapsed.TotalSeconds;
                        if (elapsed - previousTime >= 0.25)
                        {
                            var sample = (received - previousBytes) / (elapsed - previousTime);
                            speed = speed is null ? sample : speed * 0.7 + sample * 0.3;
                            previousTime = elapsed; previousBytes = received;
                            operation?.Transfer(received, total, speed,
                                elapsed - transferStart >= 2 && total >= received && speed > 0 ? TimeSpan.FromSeconds(Math.Min(86400 * 365, (total.Value - received) / speed.Value)) : null);
                        }
                    }
                    if (total is { } expectedLength && received != expectedLength) throw new IOException("The download is incomplete");
                    break;
                }
                catch (Exception ex) when (attempt < 2 && !token.IsCancellationRequested && ex is not InvalidDataException &&
                    (ex is IOException or OperationCanceledException || ex is HttpRequestException { StatusCode: null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway }))
                { operation?.Transfer(received, total, null, null); await Task.Delay(500 * (attempt + 1), token); }
            }
        }
        token.ThrowIfCancellationRequested();
        operation?.Transfer(received, total, speed, total == received ? TimeSpan.Zero : null);
        if (total is { } length && received != length) throw new IOException("The download is incomplete");
        operation?.Report(OperationPhase.Verifying);
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("The package checksum does not match");
        metadata["url"] = "package.zip";
        AtomicJson.Write(Path.Combine(directory, "index.json"), new JsonObject { ["versions"] = new JsonArray(metadata) }.ToJsonString());
        var bundle = OfflineBundle.Load(directory);
        using var verified = await bundle.PrepareAsync(bundle.Runtimes.Single(), token);
        return bundle;
    }
    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, Uri url, long received, Func<Uri, Task<bool>>? confirm, CancellationToken token)
    {
        for (var hop = 0; hop < 6; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (received > 0) request.Headers.Range = new RangeHeaderValue(received, null);
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location; response.Dispose();
            if (location is null) throw new IOException("Invalid download redirect");
            var next = new Uri(InstallationSource.Validate(new Uri(url, location).AbsoluteUri));
            if (!InstallationSource.SameOrigin(url, next) && (confirm is null || !await confirm(next)))
                throw new InvalidDataException("The download source was not approved");
            token.ThrowIfCancellationRequested(); url = next;
        }
        throw new IOException("Too many download redirects");
    }
}
