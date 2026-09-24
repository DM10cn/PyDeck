using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PimGui.Core;

public static class ShebangRules
{
    public static void ValidateMapping(string template, string command)
    {
        if (string.IsNullOrWhiteSpace(template) || template.Length > 512 || template != template.Trim() || template.Any(char.IsControl) || template.StartsWith("#!"))
            throw new ArgumentException("Enter a Shebang template without #!");
        if (!Regex.IsMatch(command, @"\Apyw?(?: -V:[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*)?\z"))
            throw new ArgumentException("Choose a Python interpreter for this rule");
    }
    public static void ValidateChanges(JsonNode? original, JsonNode replacement)
    {
        if (replacement is not JsonObject rules || rules.Count > 200) throw new ArgumentException("Invalid Shebang rules");
        foreach (var pair in rules)
        {
            // Preserve existing advanced entries byte-for-value; validate only edited mappings.
            if (original is JsonObject existing && existing.TryGetPropertyValue(pair.Key, out var old) && JsonNode.DeepEquals(old, pair.Value)) continue;
            ValidateMapping(pair.Key, pair.Value?.GetValue<string>() ?? "");
        }
    }
}
