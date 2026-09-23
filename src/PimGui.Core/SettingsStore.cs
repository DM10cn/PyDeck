using System.Text.Json;
using System.Text.Json.Nodes;

namespace PimGui.Core;

public sealed record AppSettings
{
    public string Design { get; init; } = "Material";
    public string Theme { get; init; } = "Dark";
    public string ManagerPath { get; init; } = "";
    public string Language { get; init; } = "en-US";
    public string Transparency { get; init; } = "System";
    public string Backdrop { get; init; } = "Mica";
    public string DefaultArchitecture { get; init; } = "x64";
    public string CatalogSource { get; init; } = "Online";
    public bool ShowPreviewReleases { get; init; }
    public bool ShowSpecializedPackages { get; init; }
    public bool ConfirmBeforeUninstall { get; init; } = true;

    public AppSettings Normalize() => this with
    {
        Design = Design is "Fluent" or "Material" ? Design : "Material",
        Theme = Theme is "System" or "Light" or "Dark" ? Theme : "Dark",
        Language = Language is "en-US" or "zh-CN" or "zh-TW" or "ja-JP" ? Language : "en-US",
        Transparency = Transparency is "System" or "On" or "Off" ? Transparency : "System",
        Backdrop = Backdrop is "Mica" or "Acrylic" ? Backdrop : "Mica",
        DefaultArchitecture = DefaultArchitecture is "x64" or "ARM64" or "x86" ? DefaultArchitecture : "x64",
        CatalogSource = CatalogSource is "Online" or "Offline" ? CatalogSource : "Online",
        ManagerPath = ManagerPath?.Trim() ?? ""
    };
}

public sealed class SettingsStore(string directory)
{
    public string DirectoryPath => directory;
    public string FilePath => Path.Combine(directory, "settings.json");
    public string? LoadWarning { get; private set; }
    public AppSettings Load()
    {
        LoadWarning = null;
        try
        {
            if (!File.Exists(FilePath)) return new();
            return (JsonSerializer.Deserialize<AppSettings>(SafeFiles.ReadText(FilePath)) ?? throw new JsonException("Empty settings.")).Normalize();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { LoadWarning = "Your saved preferences could not be read. Defaults are in use. " + ex.Message; return new(); }
    }
    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(directory);
        AtomicJson.Write(FilePath, JsonSerializer.Serialize(settings.Normalize(), new JsonSerializerOptions { WriteIndented = true }));
    }
}

public static class AtomicJson
{
    public static void Write(string path, string text)
    {
        SafeFiles.RequireNoLinks(path);
        var originalHash = File.Exists(path) ? Fingerprint(path) : null;
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(System.Text.Encoding.UTF8.GetBytes(text));
                stream.Flush(flushToDisk: true);
            }
            // Antivirus/indexing readers can briefly deny ReplaceFile's delete access.
            // Keep the atomic replacement and ACL preservation; never delete the old file as a fallback.
            for (var attempt = 0; ; attempt++)
            {
                SafeFiles.RequireNoLinks(path);
                if (originalHash is null) { File.Move(temporary, path); break; }
                if (!originalHash.AsSpan().SequenceEqual(Fingerprint(path)))
                    throw new IOException("The configuration changed while saving. Try again.");
                try { File.Replace(temporary, path, null); break; }
                catch (IOException ex) when (attempt < 4 && (ex.HResult & 0xffff) is 32 or 33 or 1175)
                {
                    Thread.Sleep(25 << attempt); // At most 375 ms, then surface a persistent failure.
                }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static byte[] Fingerprint(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 2 * 1024 * 1024) throw new IOException("The configuration file is too large to replace safely.");
        return System.Security.Cryptography.SHA256.HashData(file);
    }
}

public static class DefaultVersionConfig
{
    public static string SetDefault(string filePath, string selector)
    {
        if (string.IsNullOrWhiteSpace(selector) || selector.Length > 256 ||
            !System.Text.RegularExpressions.Regex.IsMatch(selector, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*(?:/[A-Za-z0-9][A-Za-z0-9_.-]*)?\z"))
            throw new ArgumentException("Invalid default version.");
        SafeFiles.RequireNoLinks(filePath);
        var original = File.Exists(filePath) ? SafeFiles.ReadText(filePath) : null;
        var root = original is null ? new JsonObject() : JsonNode.Parse(original) as JsonObject
            ?? throw new FormatException("The PIM configuration is not a JSON object. It was not changed.");
        root["default_tag"] = selector;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var backup = "";
        if (original is not null)
        {
            backup = filePath + "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "." + Guid.NewGuid().ToString("N")[..8] + ".bak";
            File.Copy(filePath, backup, overwrite: false);
        }
        if ((File.Exists(filePath) ? SafeFiles.ReadText(filePath) : null) != original)
            throw new IOException("The PIM configuration changed while it was being read. Please try again.");
        AtomicJson.Write(filePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return backup;
    }
}
