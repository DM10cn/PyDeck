using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PimGui.Core;

/// <summary>Release discovery is independent of the PIM binary catalog and its latest-micro filtering.</summary>
public static class BuildSources
{
    public static string Url(string version)
    {
        new BuildOptions(Version: version).Validate();
        var directory = Regex.Match(version, @"^\d+\.\d+\.\d+").Value;
        return $"https://www.python.org/ftp/python/{directory}/Python-{version}.tgz";
    }
    public static string[] ParseVersions(string listing) => Regex.Matches(listing, "href=\"([23]\\.\\d{1,2}\\.\\d{1,3})/\"")
        .Select(m => m.Groups[1].Value).Distinct().OrderByDescending(v => System.Version.Parse(v)).ToArray();

    public static async Task<string[]> ListAsync(NetworkSettings network, string? password, CancellationToken token = default)
    {
        using var client = network.CreateClient(password);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        return ParseVersions(await ReadLimitedAsync(client, "https://www.python.org/ftp/python/", timeout.Token));
    }
    public static async Task<string?> PublishedHashAsync(HttpClient client, string version, CancellationToken token)
    {
        if (version == BuildRecipe.Version) return BuildRecipe.SourceSha256;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45)); token = timeout.Token;
        using var response = await client.GetAsync(Url(version) + ".sigstore", HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var json = await ReadBodyAsync(response, token);
        using var document = JsonDocument.Parse(json);
        var digest = document.RootElement.GetProperty("messageSignature").GetProperty("messageDigest");
        if (digest.GetProperty("algorithm").GetString() != "SHA2_256") throw new IOException("Unsupported source digest algorithm");
        var bytes = Convert.FromBase64String(digest.GetProperty("digest").GetString()!);
        if (bytes.Length != 32) throw new IOException("Invalid source digest");
        return Convert.ToHexString(bytes);
    }
    private static async Task<string> ReadLimitedAsync(HttpClient client, string url, CancellationToken token)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode(); return await ReadBodyAsync(response, token);
    }
    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken token)
    {
        const int limit = 4 * 1024 * 1024;
        if (response.Content.Headers.ContentLength > limit) throw new IOException("Source metadata is too large");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var memory = new MemoryStream(); var buffer = new byte[16384];
        while (await stream.ReadAsync(buffer, token) is var count && count > 0)
        {
            if (memory.Length + count > limit) throw new IOException("Source metadata is too large");
            memory.Write(buffer, 0, count);
        }
        return System.Text.Encoding.UTF8.GetString(memory.ToArray());
    }

    public static void CheckCompatibility(string source, BuildOptions options)
    {
        // Validate before executing any source-provided script, including local imports.
        foreach (var relative in new[] { "PCbuild/build.bat", "PCbuild/pcbuild.proj", "PC/layout/main.py", "Include/patchlevel.h" })
            if (!File.Exists(Path.Combine(source, relative))) throw new IOException("This source needs a different Windows build/layout adapter: missing " + relative);
        var header = File.ReadAllText(Path.Combine(source, "Include", "patchlevel.h"));
        var version = Regex.Match(header, "#define\\s+PY_VERSION\\s+\"([^\"]+)\"").Groups[1].Value;
        if (version != options.Version) throw new IOException("Source version does not match the selected release: " + version);
        var batch = File.ReadAllText(Path.Combine(source, "PCbuild", "build.bat"));
        foreach (var flag in new[] { options.Pgo ? "PGInstrument" : null, !options.IncludeTk ? "--no-tkinter" : null,
            !options.IncludeSsl ? "--no-ssl" : null, !options.IncludeCtypes ? "--no-ctypes" : null })
            if (flag is not null && !batch.Contains(flag, StringComparison.Ordinal)) throw new IOException("This source does not support " + flag);
    }
}
