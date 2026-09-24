namespace PimGui.Core;

public static class InstallationSource
{
    public const string Official = "https://www.python.org/ftp/python/index-windows.json";
    public static string Validate(string value)
    {
        if (value.Length == 0) return Official;
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || value.Any(char.IsControl))
            throw new ArgumentException("Use an HTTPS index URL without credentials, query parameters, or fragments");
        return uri.AbsoluteUri;
    }
    public static string Installed(PythonRuntime runtime)
    {
        var path = Path.Combine(ExecutionPaths.LocalPath(runtime.Prefix), "__install__.json");
        SafeFiles.RequireNoLinks(path);
        using var json = System.Text.Json.JsonDocument.Parse(SafeFiles.ReadText(path));
        // PIM persists the catalog it used. Never substitute today's selected source.
        if (!json.RootElement.TryGetProperty("source", out var source) || source.ValueKind != System.Text.Json.JsonValueKind.String)
            throw new IOException("The original installation source is unavailable");
        var value = source.GetString();
        if (string.IsNullOrWhiteSpace(value)) throw new IOException("The original installation source is unavailable");
        return Validate(value);
    }
    public static bool SameOrigin(Uri a, Uri b) => a.Scheme == b.Scheme && a.IdnHost == b.IdnHost && a.Port == b.Port;
}
