using Microsoft.Win32;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PimGui.Core;

public sealed record PimConfigurationSnapshot(string Path, string? Original, JsonObject Values, IReadOnlyList<string> Overrides);

public static class PimConfiguration
{
    public static readonly string[] Editable = ["default_tag", "default_platform", "automatic_install", "include_unmanaged", "shebang_can_run_anything", "shebang_templates"];
    public static string UserPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Python", "pymanager.json");
    public static PimConfigurationSnapshot Read(string? path = null, bool inspectOverrides = true)
    {
        path ??= UserPath;
        SafeFiles.RequireNoLinks(path);
        var text = File.Exists(path) ? SafeFiles.ReadText(path) : null;
        var values = text is null ? new JsonObject() : JsonNode.Parse(text) as JsonObject ?? throw new FormatException("The PIM configuration is not a JSON object. It was not changed.");
        return new(path, text, values, inspectOverrides ? Overrides() : []);
    }
    public static IReadOnlyList<string> Overrides()
    {
        var result = new List<string>();
        foreach (var name in new[] { "PYTHON_MANAGER_CONFIG", "PYTHON_MANAGER_DEFAULT", "PYTHON_MANAGER_DEFAULT_PLATFORM", "PYTHON_MANAGER_AUTOMATIC_INSTALL", "PYTHON_MANAGER_INCLUDE_UNMANAGED", "PYTHON_MANAGER_SHEBANG_CAN_RUN_ANYTHING" })
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))) result.Add(name);
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"Software\Policies\Python\PyManager");
            if (key is not null) result.AddRange(key.GetValueNames().Select(name => "Policy: " + name));
        }
        return result;
    }
    public static string Save(PimConfigurationSnapshot snapshot, JsonObject edits, bool inspectOverrides = true)
    {
        if (inspectOverrides && Overrides().Count > 0) throw new InvalidOperationException("Environment variables or administrator policy override PIM configuration. Review them before saving");
        var root = (JsonObject)snapshot.Values.DeepClone();
        foreach (var item in edits)
        {
            if (!Editable.Contains(item.Key)) throw new ArgumentException("This setting is not editable here");
            if (item.Value is null) { root.Remove(item.Key); continue; }
            if (item.Key == "shebang_templates") ShebangRules.ValidateChanges(snapshot.Values[item.Key], item.Value);
            else if (item.Key is "automatic_install" or "include_unmanaged" or "shebang_can_run_anything") _ = item.Value.GetValue<bool>();
            else
            {
                var value = item.Value.GetValue<string>();
                if (item.Key == "default_platform" && value is not ("-64" or "-32" or "-arm64")) throw new ArgumentException("Invalid default platform");
                if (item.Key == "default_tag" && !Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*(?:/[A-Za-z0-9][A-Za-z0-9_.-]*)?\z")) throw new ArgumentException("Invalid default version.");
            }
            root[item.Key] = item.Value.DeepClone();
        }
        return Replace(snapshot, root.ToJsonString(new() { WriteIndented = true }));
    }
    private static string Replace(PimConfigurationSnapshot snapshot, string replacement)
    {
        SafeFiles.RequireNoLinks(snapshot.Path);
        if ((File.Exists(snapshot.Path) ? SafeFiles.ReadText(snapshot.Path) : null) != snapshot.Original)
            throw new IOException("The PIM configuration changed. Reload it before saving");
        Directory.CreateDirectory(Path.GetDirectoryName(snapshot.Path)!);
        var backup = snapshot.Path + ".pydeck-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak";
        // Write the exact reviewed snapshot, not a second read of a possibly changed file.
        AtomicJson.Write(backup, snapshot.Original ?? "{}");
        AtomicJson.Write(snapshot.Path, replacement, snapshot.Original, checkOriginal: true);
        return backup;
    }
    public static string Restore(PimConfigurationSnapshot snapshot, string backup)
    {
        if (Overrides().Count > 0) throw new IOException("Environment variables or administrator policy override PIM configuration. Review them before saving");
        if (!Path.GetDirectoryName(backup)!.Equals(Path.GetDirectoryName(snapshot.Path), StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(backup).StartsWith(Path.GetFileName(snapshot.Path) + ".pydeck-", StringComparison.OrdinalIgnoreCase) || !backup.EndsWith(".bak"))
            throw new IOException("Choose a backup created by PyDeck");
        SafeFiles.RequireNoLinks(backup);
        var contents = SafeFiles.ReadText(backup);
        if (JsonNode.Parse(contents) is not JsonObject) throw new IOException("Invalid configuration backup");
        return Replace(snapshot, contents);
    }
}
