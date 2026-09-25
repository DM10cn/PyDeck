using System.Text.RegularExpressions;

namespace PimGui.Core;

public enum ActivityLevel { Information, Warning, Error }
public enum ActivityOrigin { Application, PythonManager, Command }

public sealed record ActivityEntry(DateTime Timestamp, ActivityLevel Level, string Message, ActivityOrigin Origin = ActivityOrigin.Application)
{
    public string Format() => $"[{Timestamp:HH:mm:ss}] [{Level switch { ActivityLevel.Error => "ERROR", ActivityLevel.Warning => "WARN", _ => "INFO" }}] {Message}";
}

public sealed class ActivityLog
{
    private readonly Queue<ActivityEntry> entries = new();
    private int retainedCharacters;
    public IReadOnlyCollection<ActivityEntry> Entries => entries.ToArray();

    public void Add(string message, ActivityLevel? level = null, ActivityOrigin origin = ActivityOrigin.Application)
    {
        var clean = Regex.Replace(message, @"\x1B\[[0-?]*[ -/]*[@-~]", "");
        var severity = level ?? Classify(clean);
        if (clean.Length > 2048) clean = clean[..2048] + " [shortened]";
        var entry = new ActivityEntry(DateTime.Now, severity, clean, clean.StartsWith("> ", StringComparison.Ordinal) ? ActivityOrigin.Command : origin);
        entries.Enqueue(entry);
        retainedCharacters += entry.Format().Length;
        while (entries.Count > 2000 || retainedCharacters * sizeof(char) > 1024 * 1024)
            retainedCharacters -= entries.Dequeue().Format().Length;
    }

    // Infer only explicit output prefixes, never ordinary words in paths or messages.
    public static ActivityLevel Classify(string message)
    {
        var levels = Regex.Matches(message, @"(?im)^\s*(?:\[(ERROR|WARN(?:ING)?)\]|(ERROR|WARN(?:ING)?)(?=\s|:|·|$))");
        if (levels.Any(m => (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Equals("ERROR", StringComparison.OrdinalIgnoreCase)))
            return ActivityLevel.Error;
        return levels.Count > 0 ? ActivityLevel.Warning : ActivityLevel.Information;
    }

    public string Text(ActivityLevel? filter = null) => string.Join(Environment.NewLine,
        entries.Where(entry => filter is null || entry.Level == filter).Select(entry => entry.Format()));
    public void Clear() { entries.Clear(); retainedCharacters = 0; }
}
