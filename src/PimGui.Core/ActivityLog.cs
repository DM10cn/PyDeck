using System.Text.RegularExpressions;

namespace PimGui.Core;

public enum ActivityLevel { Information, Warning, Error }
public enum ActivityOrigin { Application, PythonManager, Command }

public sealed record ActivityEntry(DateTime Timestamp, ActivityLevel Level, string Message, ActivityOrigin Origin = ActivityOrigin.Application)
{
    public string Format() => $"[{Timestamp:HH:mm:ss}] [{Level switch { ActivityLevel.Error => "ERROR", ActivityLevel.Warning => "WARN", _ => "INFO" }}] {Message}";
}

public sealed partial class ActivityLog
{
    private readonly Queue<(ActivityEntry Entry, int Characters)> entries = new();
    private readonly object gate = new();
    private int retainedCharacters;
    private IReadOnlyCollection<ActivityEntry>? snapshot;
    public IReadOnlyCollection<ActivityEntry> Entries
    {
        get { lock (gate) return snapshot ??= Array.AsReadOnly(entries.Select(item => item.Entry).ToArray()); }
    }

    public void Add(string message, ActivityLevel? level = null, ActivityOrigin origin = ActivityOrigin.Application)
    {
        var clean = EscapeSequence().Replace(message, "");
        var severity = level ?? Classify(clean);
        if (clean.Length > 2048) clean = clean[..2048] + " [shortened]";
        var entry = new ActivityEntry(DateTime.Now, severity, clean, clean.StartsWith("> ", StringComparison.Ordinal) ? ActivityOrigin.Command : origin);
        var characters = entry.Format().Length;
        lock (gate)
        {
            entries.Enqueue((entry, characters));
            retainedCharacters += characters;
            while (entries.Count > 2000 || retainedCharacters * sizeof(char) > 1024 * 1024)
                retainedCharacters -= entries.Dequeue().Characters;
            snapshot = null;
        }
    }

    // Infer only explicit output prefixes, never ordinary words in paths or messages.
    public static ActivityLevel Classify(string message)
    {
        var levels = LevelPrefix().Matches(message);
        if (levels.Any(m => (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Equals("ERROR", StringComparison.OrdinalIgnoreCase)))
            return ActivityLevel.Error;
        return levels.Count > 0 ? ActivityLevel.Warning : ActivityLevel.Information;
    }

    public string Text(ActivityLevel? filter = null) => string.Join(Environment.NewLine,
        Entries.Where(entry => filter is null || entry.Level == filter).Select(entry => entry.Format()));
    public void Clear() { lock (gate) { entries.Clear(); retainedCharacters = 0; snapshot = null; } }

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")]
    private static partial Regex EscapeSequence();
    [GeneratedRegex(@"(?im)^\s*(?:\[(ERROR|WARN(?:ING)?)\]|(ERROR|WARN(?:ING)?)(?=\s|:|·|$))")]
    private static partial Regex LevelPrefix();
}
