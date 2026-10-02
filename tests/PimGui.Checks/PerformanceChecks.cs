using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using PimGui.Core;

internal static class PerformanceChecks
{
    public static void Run(Action<string, Action> check)
    {
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        check("Concurrent notifications share one pending dispatcher callback", () =>
        {
            var queue = new ConcurrentQueue<Action>(); var updates = 0; var value = 0; var observed = 0;
            var signal = new CoalescedAction(action => { queue.Enqueue(action); return true; }, () => { updates++; observed = value; });
            Parallel.For(0, 10_000, _ => { Interlocked.Increment(ref value); signal.Request(); });
            Require(queue.Count == 1 && updates == 0, "Producer flood queued redundant UI work");
            queue.TryDequeue(out var dispatch); dispatch!();
            Require(updates == 1 && observed == 10_000, "Latest state was lost");
            signal.Request(); Require(queue.Count == 1, "Completed signal stayed locked");
        });
        check("Notification during dispatch schedules a follow-up; rejected dispatch can retry", () =>
        {
            var queue = new Queue<Action>(); var accept = false; var updates = 0;
            CoalescedAction? signal = null;
            signal = new(action => { if (!accept) return false; queue.Enqueue(action); return true; },
                () => { if (++updates == 1) signal!.Request(); });
            signal.Request(); accept = true; signal.Request();
            Require(queue.Count == 1, "Rejected dispatch stayed locked");
            queue.Dequeue()(); Require(queue.Count == 1, "Reentrant notification lost");
            queue.Dequeue()(); Require(updates == 2 && queue.Count == 0, "Unexpected extra dispatch");
        });
        check("Failed UI update does not poison subsequent notifications", () =>
        {
            var queue = new Queue<Action>(); var fail = true;
            var signal = new CoalescedAction(action => { queue.Enqueue(action); return true; }, () => { if (fail) throw new IOException("fixture"); });
            signal.Request(); try { queue.Dequeue()(); } catch (IOException) { }
            fail = false; signal.Request(); Require(queue.Count == 1, "Update failure leaked ownership"); queue.Dequeue()();
        });
        check("Activity snapshots are stable, bounded, and safe for concurrent producers", () =>
        {
            var log = new ActivityLog(); log.Add("before"); var previous = log.Entries;
            Require(ReferenceEquals(previous, log.Entries), "Unchanged log recopied");
            Parallel.For(0, 4000, i => { log.Add("WARN worker " + i); if (i % 100 == 0) _ = log.Text(ActivityLevel.Warning); });
            Require(previous.Single().Message == "before", "Published snapshot mutated");
            Require(log.Entries.Count == 2000 && log.Entries.All(e => e.Level == ActivityLevel.Warning), "Retention or severity drift");
            var retained = log.Entries; log.Clear(); Require(log.Entries.Count == 0 && retained.Count == 2000, "Clear corrupted a snapshot");
            log.Add(new string('x', 10_000)); Require(log.Entries.Single().Message.Length < 2100, "Line limit lost");
        });
        check("Activity byte retention remains accurate during rolling eviction", () =>
        {
            var log = new ActivityLog();
            for (var i = 0; i < 1500; i++) log.Add(new string('x', 2048), i % 2 == 0 ? ActivityLevel.Error : ActivityLevel.Information);
            var retained = log.Entries.Sum(entry => entry.Format().Length * sizeof(char));
            Require(retained <= 1024 * 1024 && retained > 1024 * 1024 - 4200, "Byte retention no longer fills or respects its limit");
            for (var i = 0; i < 2500; i++) log.Add("short");
            Require(log.Entries.Count == 2000 && log.Entries.All(entry => entry.Message == "short"), "Rolling size bookkeeping drifted");
        });
        check("Catalog snapshot matches filters and ordering across repeated queries", () =>
        {
            var source = Enumerable.Range(0, 2000).Select(i => new PythonRuntime("id-" + i,
                i % 4 == 0 ? "PythonEmbed" : i % 4 == 1 ? "PythonTest" : "PythonCore",
                $"3.{i % 8 + 8}{(i % 7 == 0 ? "t" : "")}-{(i % 3 == 0 ? "arm64" : i % 3 == 1 ? "32" : "64")}",
                $"3.{i % 8 + 8}.{i % 40}{(i % 5 == 0 ? "rc1" : "")}", "Python item " + i, "", "", false)).ToList();
            source.Add(source[2] with { Id = "tied-order" });
            var snapshot = new RuntimeCatalogSnapshot(source);
            foreach (var architecture in new[] { "All architectures", "x64", "ARM64", "x86" })
            foreach (var previews in new[] { false, true })
            foreach (var filter in new[] { "All", "Standard", "FreeThreaded", "Embedded", "Tests", "Other" })
            foreach (var search in new[] { "", "3.14", "PYTHONCORE", "item 10", "no match", "With tests" })
            {
                var expected = RuntimeCatalog.Filter(source, architecture, previews, search)
                    .Where(r => filter == "Standard" ? !r.IsSpecialized : RuntimeCatalog.MatchesDistribution(r, filter));
                Require(snapshot.Filter(architecture, previews, search, filter).SequenceEqual(expected), "Projection changed catalog semantics");
            }
            var oldCount = source.Count;
            source.Clear();
            Require(snapshot.Filter("All architectures", true, "").Count == oldCount &&
                new RuntimeCatalogSnapshot(source).Filter("All architectures", true, "").Count == 0, "Catalog snapshots were not independent");
        });
        check("Repeated catalog query allocation and timing are measured independently", () =>
        {
            var source = Enumerable.Range(0, 20_000).Select(i => new PythonRuntime("id-" + i, "PythonCore", "3.14-64",
                "3.14." + i, "Python 3.14." + i, "", "", false)).ToArray();
            var projectionClock = Stopwatch.StartNew(); var before = GC.GetAllocatedBytesForCurrentThread();
            var snapshot = new RuntimeCatalogSnapshot(source); projectionClock.Stop();
            var projectionBytes = GC.GetAllocatedBytesForCurrentThread() - before;
            var queries = new[] { "", "3", "3.", "3.1", "3.14", "3.14.", "3.14.1", "3.14.12" };
            var legacy = Measure(() => queries.Select(query => RuntimeCatalog.Filter(source, "x64", false, query).Count.ToString()).ToArray());
            var current = Measure(() => queries.Select(query => snapshot.Filter("x64", false, query).Count.ToString()).ToArray());
            Require(legacy.Result.SequenceEqual(current.Result), "Repeated search counts changed");
            Require(current.Bytes < legacy.Bytes, "Projection did not reduce repeated-query allocation");
            Console.WriteLine($"METRIC catalog projection: {projectionClock.Elapsed.TotalMilliseconds:F1} ms / {projectionBytes} bytes; 8 queries over 20000 rows: previous {legacy.Ms:F1} ms / {legacy.Bytes} bytes; snapshot {current.Ms:F1} ms / {current.Bytes} bytes");
        });
        check("Shared version keys preserve historical ordering and recommendation ties", () =>
        {
            var versions = new[] { "3.14.0a2", "3.14.0a10", "3.14.0b1", "3.14.0rc1", "3.14.0rc11", "3.14.0", "3.14.1", "3.15.0a1", "3.13.99", "unknown", "3.14", "3.14.0RC2", "3.14.0.1" };
            foreach (var left in versions)
            foreach (var right in versions)
                Require(Math.Sign(LegacyCompare(left, right)) == Math.Sign(RuntimeCatalog.CompareVersions(left, right)), "Ordering changed: " + left + " / " + right);
            PythonRuntime Runtime(string tag, string version) => new(tag, "PythonCore", tag, version, tag, "", "", false);
            var x86 = Runtime("3.14-32", "3.14.0"); var x64 = Runtime("3.14-64", "3.14.0");
            Require(ReferenceEquals(RuntimeCatalog.Recommended([x86, x64, Runtime("3.15-dev-64", "3.15.0a1")], "x64"), x64), "Preferred architecture or preview exclusion lost");
            Require(RuntimeCatalog.Recommended([x64 with { Company = "PythonEmbed" }], "x64") is null, "Specialized build recommended");
            var source = Enumerable.Range(0, 20_000).Select(i => versions[i % versions.Length]).ToArray();
            var old = Measure(() => source.OrderByDescending(v => v, Comparer<string>.Create(LegacyCompare)).ToArray());
            var current = Measure(() => source.OrderByDescending(RuntimeCatalog.VersionKey).ToArray());
            Require(old.Result.SequenceEqual(current.Result), "Large catalog ordering changed");
            Console.WriteLine($"METRIC 20000-version sort: legacy {old.Ms:F1} ms / {old.Bytes} allocated bytes; keyed {current.Ms:F1} ms / {current.Bytes} allocated bytes");
        });
    }

    private static (string[] Result, double Ms, long Bytes) Measure(Func<string[]> action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew(); var result = action(); clock.Stop();
        return (result, clock.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    // Independent reference retained from the original comparator, not a mirror of the new key.
    private static int LegacyCompare(string left, string right)
    {
        Version Core(string value) => Version.TryParse(Regex.Match(value, @"^\d+(?:\.\d+){1,3}").Value, out var version) ? version : new(0, 0);
        var result = Core(left).CompareTo(Core(right)); if (result != 0) return result;
        (int, int) Suffix(string value)
        {
            var match = Regex.Match(value, @"(?:\d)(a|b|rc)(\d+)", RegexOptions.IgnoreCase);
            return match.Success ? (match.Groups[1].Value.ToLowerInvariant() switch { "a" => 0, "b" => 1, _ => 2 }, int.Parse(match.Groups[2].Value)) : (3, 0);
        }
        return Suffix(left).CompareTo(Suffix(right));
    }
}
