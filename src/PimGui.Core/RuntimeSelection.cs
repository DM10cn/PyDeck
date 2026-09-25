using System.Text.Json;
using System.Text.RegularExpressions;

namespace PimGui.Core;

public static class RuntimeSelection
{
    public static string ExactSelector(PythonRuntime runtime)
    {
        if (!Regex.IsMatch(runtime.Version, @"\A\d+\.\d+(?:\.\d+){0,2}(?:(?:a|b|rc)\d+)?\z", RegexOptions.IgnoreCase))
            throw new ArgumentException("Invalid Python package version");
        var platform = Regex.Match(runtime.Tag, @"-(?:32|64|arm64)\z", RegexOptions.IgnoreCase).Value;
        var exact = runtime.Version + (runtime.IsFreeThreaded ? "t" : "") + platform;
        if (runtime.DownloadMetadata is not null)
        {
            using var json = JsonDocument.Parse(runtime.DownloadMetadata);
            if (json.RootElement.TryGetProperty("install-for", out var tags) && tags.ValueKind == JsonValueKind.Array)
            {
                // Only a version-specific tag can request a historical micro safely. Do not use
                // a latest/major/minor alias supplied by a custom feed as an exact selector.
                foreach (var tag in tags.EnumerateArray())
                    if (tag.ValueKind == JsonValueKind.String && string.Equals(tag.GetString(), exact, StringComparison.OrdinalIgnoreCase))
                        return runtime.Company + "/" + tag.GetString();
            }
        }
        // Installed/legacy metadata may omit install-for. PIM must still resolve this selector
        // back to the requested ID AND version before any download or installation is allowed.
        return runtime.Company + "/" + exact;
    }
}
