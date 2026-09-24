using System.Text.Json;
using System.Text.RegularExpressions;

namespace PimGui.Core;

public sealed record AppRelease(Version Version, Uri Page);
public static class AppUpdates
{
    public const string ReleasesPage = "https://github.com/DM10cn/PyDeck/releases";
    public static AppRelease? Parse(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"\Av[0-9]+\.[0-9]+\.[0-9]+\z") || !Version.TryParse(tag[1..], out var version)) return null;
        var normalized = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        // Construct the destination ourselves; never open an untrusted URL from release metadata.
        return version > normalized ? new(version, new Uri(ReleasesPage + "/tag/" + tag)) : null;
    }
    public static async Task<AppRelease?> CheckAsync(HttpClient http, Version current, CancellationToken token = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/DM10cn/PyDeck/releases/latest");
        request.Headers.UserAgent.ParseAdd("PyDeck/" + current.ToString(3));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1024 * 1024, timeout.Token);
        return Parse(await response.Content.ReadAsStringAsync(timeout.Token), current);
    }
}
