using PimGui.Core;
using System.Text.Json.Nodes;

if (args.Contains("--emit-long-output")) { Console.Write(new string('x', 9 * 1024 * 1024)); return 0; }
if (args.Contains("--emit-proxy-secret")) { Console.WriteLine(new string('x', 2040) + "boundary-secret-value"); Console.WriteLine("boundary-secret-value"); return 0; }
if (args.Contains("--wait-child")) { await Task.Delay(TimeSpan.FromSeconds(45)); return 0; }
if (args.Contains("--touch-marker")) { File.WriteAllText(args[^1], "started"); return 0; }
if (args.Contains("--cancellation-parent"))
{
    var childStart = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    childStart.ArgumentList.Add("--wait-child");
    using var child = System.Diagnostics.Process.Start(childStart)!;
    Console.WriteLine("CHILD:" + child.Id);
    Console.WriteLine("BITS:" + Environment.GetEnvironmentVariable("PYMANAGER_ENABLE_BITS_DOWNLOAD"));
    Console.Write("Downloading: " + new string('.', 33)); Console.Out.Flush();
    await Task.Delay(TimeSpan.FromSeconds(45)); return 0;
}

var failures = new List<string>();
var passed = 0;
void Check(string name, Action action)
{
    try { action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
async Task CheckAsync(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception ex) { failures.Add(name + ": " + ex.Message); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
}
void Require(bool value, string message) { if (!value) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
const string json = """
{"versions":[{"id":"pythoncore-3.14-64","company":"PythonCore","tag":"3.14-64","sort-version":"3.14.7","display-name":"Python 3.14.7","default":true,"prefix":"C:\\Python with spaces","unknown":{"preserve":true}}]}
""";
var runtime = RuntimeParser.Parse(json).Single();
Check("PIM envelope, default flag, unknown fields, and paths", () => Require(runtime.IsDefault && runtime.Version == "3.14.7" && runtime.Prefix == @"C:\Python with spaces", "Parse mismatch"));
Check("Empty result is valid; unexpected envelope is an error", () => { Require(RuntimeParser.Parse("{\"versions\":[]}").Count == 0, "Empty list"); Throws<FormatException>(() => RuntimeParser.Parse("{}")); });
Check("Malformed JSON is not an empty list", () => Throws<FormatException>(() => RuntimeParser.Parse("not json")));
Check("Missing identity is rejected", () => Throws<FormatException>(() => RuntimeParser.Parse("{\"versions\":[{\"tag\":\"3.14\"}]}")));
Check("Preview and architecture detection", () => { var r = runtime with { Tag = "3.15t-dev-arm64", Version = "3.15.0rc2" }; Require(r.IsPrerelease && r.IsFreeThreaded && r.Architecture == "ARM64", "Preview classification"); });
Check("Unmanaged runtime cannot be uninstalled", () => Throws<InvalidOperationException>(() => PimClient.BuildArguments(RuntimeAction.Uninstall, runtime)));
Check("Uninstall uses a validated selector and has no purge flag", () => { var args = PimClient.BuildArguments(RuntimeAction.Uninstall, runtime with { IsManaged = true }); Require(args.SequenceEqual(new[] { "uninstall", "--yes", runtime.Selector }), "Unsafe uninstall arguments"); });
Check("Update uses a validated selector and update semantics", () => Require(PimClient.BuildArguments(RuntimeAction.Update, runtime with { IsManaged = true }).SequenceEqual(new[] { "install", "--yes", "--update", runtime.Selector }), "Wrong update arguments"));
Check("Option-shaped runtime IDs are rejected", () => Throws<ArgumentException>(() => PimClient.BuildArguments(RuntimeAction.Install, runtime with { Id = "--purge" })));

var scratch = Path.Combine(Environment.CurrentDirectory, "artifacts", "checks", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
var fakeManager = Path.Combine(scratch, "pymanager.exe"); File.WriteAllBytes(fakeManager, []);
var lockPath = Path.Combine(scratch, "operation.lock");
Check("Default configuration retains unknown values and creates a backup", () =>
{
    var path = Path.Combine(scratch, "pymanager.json");
    const string before = "{\"install\":{\"source\":\"https://example.test/feed\"},\"automatic_install\":false}";
    File.WriteAllText(path, before);
    var backup = DefaultVersionConfig.SetDefault(path, "PythonCore/3.14-64");
    var root = JsonNode.Parse(File.ReadAllText(path))!;
    Require(root["default_tag"]!.GetValue<string>() == "PythonCore/3.14-64" && root["automatic_install"]!.GetValue<bool>() == false && root["install"]!["source"]!.GetValue<string>() == "https://example.test/feed", "Unrelated settings changed");
    Require(File.ReadAllText(backup) == before, "Backup mismatch");
});
Check("Malformed configuration stays untouched", () =>
{
    var path = Path.Combine(scratch, "broken.json"); File.WriteAllText(path, "{invalid");
    Throws<System.Text.Json.JsonException>(() => DefaultVersionConfig.SetDefault(path, "3.14"));
    Require(File.ReadAllText(path) == "{invalid", "Broken file was overwritten");
});
Check("Appearance settings persist", () => { var store = new SettingsStore(Path.Combine(scratch, "settings")); store.Save(new() { Design = "Fluent", Theme = "Light" }); Require(store.Load().Design == "Fluent" && store.Load().Theme == "Light", "Settings mismatch"); });

async Task WaitForStagedWriteAsync(string path)
{
    var deadline = System.Diagnostics.Stopwatch.StartNew();
    while (!Directory.EnumerateFiles(scratch, Path.GetFileName(path) + ".*.tmp").Any())
    {
        if (deadline.Elapsed > TimeSpan.FromSeconds(3)) throw new Exception("Writer did not stage a replacement");
        await Task.Delay(5);
    }
}
await CheckAsync("Atomic settings replacement tolerates a short-lived reader lock", async () =>
{
    var path = Path.Combine(scratch, "transient-reader.json"); File.WriteAllText(path, "old");
    using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    var writer = Task.Run(() => AtomicJson.Write(path, "new"));
    try { await WaitForStagedWriteAsync(path); await Task.Delay(40); Require(!writer.IsCompleted, "Reader did not block replacement"); }
    finally { reader.Dispose(); }
    await writer;
    Require(File.ReadAllText(path) == "new" && !Directory.EnumerateFiles(scratch, "transient-reader.json.*.tmp").Any(), "Replacement did not finish cleanly");
});
Check("Persistent settings locks retain the original and remove staging files", () =>
{
    var path = Path.Combine(scratch, "persistent-reader.json"); File.WriteAllText(path, "old");
    using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    var elapsed = System.Diagnostics.Stopwatch.StartNew();
    Throws<IOException>(() => AtomicJson.Write(path, "new"));
    Require(elapsed.Elapsed < TimeSpan.FromSeconds(3), "Lock retry is unbounded");
    Require(File.ReadAllText(path) == "old" && !Directory.EnumerateFiles(scratch, "persistent-reader.json.*.tmp").Any(), "Failed save changed the original or left staging files");
});
await CheckAsync("Atomic settings retries reject an intervening external edit", async () =>
{
    var path = Path.Combine(scratch, "concurrent-settings.json"); File.WriteAllText(path, "old");
    using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    var writer = Task.Run(() => AtomicJson.Write(path, "our value"));
    try { await WaitForStagedWriteAsync(path); File.WriteAllText(path, "external value"); }
    finally { reader.Dispose(); }
    try { await writer; throw new Exception("External edit was overwritten"); }
    catch (IOException ex) { Require(ex.Message.Contains("changed while saving"), "Unexpected failure: " + ex.Message); }
    Require(File.ReadAllText(path) == "external value", "External edit was lost");
});

await CheckAsync("Discovery rejects the legacy launcher", async () =>
{
    var client = new PimClient(new FakeRunner((_, _) => Task.FromResult(new CommandResult(0, "Python Launcher for Windows", ""))));
    Require(!await client.DiscoverAsync(fakeManager), "Legacy launcher accepted");
});
await CheckAsync("A failed list is surfaced instead of showing no runtimes", async () =>
{
    var client = new PimClient(new FakeRunner((_, args) => Task.FromResult(args[0] == "help" ? new CommandResult(0, "Python installation manager 26.3\n--only-managed --online", "") : new CommandResult(5, "", "Access denied"))));
    Require(await client.DiscoverAsync(fakeManager), "Discovery failed");
    try { await client.ListAsync(); throw new Exception("Failed process was accepted"); } catch (InvalidOperationException ex) { Require(ex.Message.Contains("Access denied"), "Failure detail lost"); }
});
await CheckAsync("Managed state comes from only-managed query", async () =>
{
    var client = new PimClient(new FakeRunner((_, args) => Task.FromResult(new CommandResult(0, args[0] == "help" ? "Python installation manager 26.3\n--only-managed --online" : args.Contains("--only-managed") ? "{\"versions\":[]}" : json, ""))));
    await client.DiscoverAsync(fakeManager);
    Require(!(await client.ListAsync()).Single().IsManaged, "Unmanaged entry was elevated to managed");
});
await CheckAsync("Concurrent mutation is rejected until the first exits", async () =>
{
    var pending = new TaskCompletionSource<CommandResult>();
    var client = new PimClient(new FakeRunner((_, args) => args[0] == "help" ? Task.FromResult(new CommandResult(0, "Python installation manager 26.3\n--only-managed --online", "")) :
        args[0] == "list" ? Task.FromResult(new CommandResult(0, json, "")) : pending.Task), lockPath);
    await client.DiscoverAsync(fakeManager);
    var first = client.ChangeAsync(RuntimeAction.Install, runtime, _ => { });
    try { await client.ChangeAsync(RuntimeAction.Install, runtime, _ => { }); throw new Exception("Concurrent mutation accepted"); }
    catch (InvalidOperationException ex) { Require(ex.Message.Contains("already running"), "Wrong rejection"); }
    finally { pending.SetResult(new CommandResult(0, "", "")); }
    await first;
});
Check("Catalog recommendation excludes previews and specialized packages and compares versions numerically", () =>
{
    var items = new[] { runtime with { Version = "3.9.9" }, runtime with { Version = "3.14.7" },
        runtime with { Version = "3.15.0rc2" }, runtime with { Company = "PythonEmbed", Version = "3.16.0" },
        runtime with { Tag = "3.16t-64", Version = "3.16.0" }, runtime with { Company = "PythonTest", Version = "3.16.0" } };
    Require(RuntimeCatalog.Recommended(items, "x64")?.Version == "3.14.7", "Unsafe recommendation");
    Require(items.Count(r => r.IsSpecialized) == 3, "Specialized distribution classification");
    Require(RuntimeCatalog.Filter(items, "ARM64", false, "").Count == 0, "Architecture ignored");
    Require(RuntimeCatalog.Filter(items, "x64", false, "").All(r => !r.IsPrerelease), "Preview not filtered");
});
Check("Legacy and invalid preferences normalize without enabling risky options", () =>
{
    var defaults = new AppSettings();
    Require(defaults.Language == "en-US" && defaults.ConfirmBeforeUninstall && !defaults.ShowPreviewReleases && !defaults.ShowSpecializedPackages, "Incorrect defaults");
    var prefs = new AppSettings { Design = "bad", Theme = "bad", Language = "bad", Transparency = "bad", Backdrop = "bad", DefaultArchitecture = "bad" }.Normalize();
    Require(prefs == defaults, "Invalid preference not normalized");
    var store = new SettingsStore(Path.Combine(scratch, "new-settings"));
    var chosen = defaults with { Language = "zh-TW", ShowPreviewReleases = true, ShowSpecializedPackages = true, ConfirmBeforeUninstall = false, DefaultArchitecture = "ARM64", Transparency = "Off", Backdrop = "Acrylic" };
    store.Save(chosen); Require(store.Load() == chosen, "Python, language, and transparency preferences not persisted");
});
Check("Transparency matrix enforces Material, Off, Windows setting, contrast, and unsupported fallback", () =>
{
    foreach (var design in new[] { "Material", "Fluent" })
    foreach (var choice in new[] { "System", "On", "Off" })
    foreach (var windows in new[] { true, false })
    foreach (var contrast in new[] { true, false })
    {
        var prefs = new AppSettings { Design = design, Transparency = choice };
        var expected = design == "Fluent" && !contrast && (choice == "On" || choice == "System" && windows);
        Require((BackdropPolicy.Resolve(prefs, windows, contrast, true, true) == "Mica") == expected, "Policy matrix mismatch");
        Require(BackdropPolicy.Resolve(prefs, windows, contrast, false, false) == "Solid", "Unsupported material enabled");
    }
    Require(BackdropPolicy.Resolve(new() { Design = "Fluent", Backdrop = "Acrylic", Transparency = "On" }, true, false, false, true) == "Acrylic", "Acrylic ignored");
});
Check("Language tables contain matching keys and format placeholders", () =>
{
    var english = Strings.Table("en-US");
    foreach (var language in Strings.Languages)
    {
        var table = Strings.Table(language);
        Require(table.Keys.Order().SequenceEqual(english.Keys.Order()), "Incomplete locale: " + language);
        foreach (var (key, value) in table)
        {
            var pattern = @"\{\d+(?:[^}]*)?\}";
            Require(System.Text.RegularExpressions.Regex.Matches(key, pattern).Select(m => m.Value).Order().SequenceEqual(
                System.Text.RegularExpressions.Regex.Matches(value, pattern).Select(m => m.Value).Order()), "Placeholder mismatch: " + key);
            Require(!string.IsNullOrWhiteSpace(value), "Empty translation");
        }
        Strings.Language = language;
        Require(Strings.T("INSTALLED VERSIONS  /  {0}", 4).Contains('4'), "Localized count lost");
    }
    Strings.Language = "en-US";
    Require(Strings.T("Settings") == "Settings", "English fallback changed");
});
Check("Malformed and duplicate runtime identities are rejected", () =>
{
    Throws<FormatException>(() => RuntimeParser.Parse("{\"versions\":[1]}"));
    var root = JsonNode.Parse(json)!; root["versions"]!.AsArray().Add(root["versions"]![0]!.DeepClone());
    Throws<FormatException>(() => RuntimeParser.Parse(root.ToJsonString()));
    foreach (var id in new[] { "--purge", "/all", "python;calc", "python\nnext", "python with spaces", "*" })
        Throws<ArgumentException>(() => PimClient.BuildArguments(RuntimeAction.Install, runtime with { Id = id }));
});
Check("Manager execution rejects relative, network, device, alternate-stream, and unrelated executables", () =>
{
    foreach (var path in new[] { "pymanager.exe", @"\\server\share\pymanager.exe", @"\\?\C:\pymanager.exe", @"C:\pymanager.exe:stream", @"C:\tool.exe", @"C:\foo;bar\pymanager.exe" })
        Throws<ArgumentException>(() => ExecutionPaths.Manager(path));
    Require(ExecutionPaths.Manager(fakeManager) == fakeManager, "Local manager rejected");
});
await CheckAsync("Stale management status prevents destructive commands", async () =>
{
    var mutated = false;
    var client = new PimClient(new FakeRunner((_, arguments) =>
    {
        if (arguments[0] is "install" or "uninstall") mutated = true;
        return Task.FromResult(new CommandResult(0, arguments[0] == "help" ? "Python installation manager 26.3\n--only-managed --online" : arguments.Contains("--only-managed") ? "{\"versions\":[]}" : json, ""));
    }), lockPath);
    await client.DiscoverAsync(fakeManager);
    try { await client.ChangeAsync(RuntimeAction.Uninstall, runtime with { IsManaged = true }, _ => { }); throw new Exception("Stale card accepted"); }
    catch (InvalidOperationException ex) { Require(ex.Message.Contains("changed"), "Wrong error"); }
    Require(!mutated, "Destructive command executed");
});
await CheckAsync("Separate clients share an operation lock", async () =>
{
    var pending = new TaskCompletionSource<CommandResult>();
    var runner = new FakeRunner((_, arguments) => arguments[0] is "install" ? pending.Task :
        Task.FromResult(new CommandResult(0, arguments[0] == "help" ? "Python installation manager 26.3\n--only-managed --online" : json, "")));
    var first = new PimClient(runner, lockPath); var second = new PimClient(runner, lockPath);
    await first.DiscoverAsync(fakeManager); await second.DiscoverAsync(fakeManager);
    var operation = first.ChangeAsync(RuntimeAction.Install, runtime, _ => { });
    try { await second.ChangeAsync(RuntimeAction.Install, runtime, _ => { }); throw new Exception("Cross-client mutation accepted"); }
    catch (InvalidOperationException ex) { Require(ex.Message.Contains("Another PyDeck"), "Wrong lock error"); }
    finally { pending.SetResult(new CommandResult(0, "", "")); }
    await operation;
});
await CheckAsync("Large output without newlines stays bounded and an observer cannot deadlock the child", async () =>
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var result = await new ProcessRunner().RunAsync(Environment.ProcessPath!, ["--emit-long-output"], _ => throw new Exception("observer"), timeout.Token);
    Require(result.ExitCode == 0 && result.OutputTruncated && result.Output.Length <= 8 * 1024 * 1024, "Output cap failed");
});
Check("UI copy uses short labels without CJK sentence stops", () =>
{
    foreach (var language in new[] { "zh-CN", "zh-TW", "ja-JP" })
        Require(Strings.Table(language).Values.All(value => !value.Contains('。')), "CJK sentence stop: " + language);
    var settings = new AppSettings { CatalogSource = "Offline" };
    var store = new SettingsStore(Path.Combine(scratch, "offline-settings")); store.Save(settings);
    Require(store.Load().CatalogSource == "Offline" && new AppSettings().Language == "en-US", "Offline or default language preference lost");
});
string MakeBundle(string name, string entryName = "python.txt", string? sourceUrl = null)
{
    var directory = Path.Combine(scratch, name); Directory.CreateDirectory(directory);
    var zip = Path.Combine(directory, "python.zip");
    using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
    using (var writer = new StreamWriter(archive.CreateEntry(entryName).Open())) writer.Write("fixture, not an executable");
    var root = JsonNode.Parse(json)!.AsObject(); var item = root["versions"]![0]!;
    item["url"] = sourceUrl ?? "python.zip";
    item["hash"] = new JsonObject { ["sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(zip))) };
    File.WriteAllText(Path.Combine(directory, "index.json"), root.ToJsonString());
    return directory;
}
await CheckAsync("Offline bundle creates a checksum-verified local snapshot and cleans up only its own files", async () =>
{
    var directory = MakeBundle("valid-bundle"); var bundle = OfflineBundle.Load(directory);
    string index;
    using (var prepared = await bundle.PrepareAsync(bundle.Runtimes.Single()))
    {
        index = prepared.IndexPath;
        Require(File.Exists(prepared.ArchivePath), "Snapshot missing");
        var config = JsonNode.Parse(File.ReadAllText(prepared.ConfigPath))!;
        Require(config["install"]!["fallback_source"]!.GetValue<string>() == index, "Fallback was not local");
        Require(OfflineBundle.Load(Path.GetDirectoryName(index)!).Runtimes.Count == 1, "Snapshot index invalid");
    }
    Require(!File.Exists(index) && File.Exists(Path.Combine(directory, "python.zip")), "Cleanup removed the wrong files");
});
Check("Offline indexes reject network, traversal, encoded, device and absolute package paths", () =>
{
    var invalid = new[] { "https://example.test/python.zip", "../python.zip", @"..\python.zip", "nested/../../python.zip", "%2e%2e/python.zip", @"C:\python.zip", @"\\server\python.zip", "python.zip:stream", ".. /python.zip", "NUL.zip", "COM1/python.zip" };
    for (var i = 0; i < invalid.Length; i++)
        Throws<IOException>(() => OfflineBundle.Load(MakeBundle("bad-url-" + i, sourceUrl: invalid[i])));
});
Check("Offline index rejects chained and signed feeds, duplicate IDs, missing files and missing hashes", () =>
{
    foreach (var key in new[] { "next", "requires_signature", "source_settings" })
    {
        var directory = MakeBundle("bad-root-" + key); var index = Path.Combine(directory, "index.json");
        var root = JsonNode.Parse(File.ReadAllText(index))!; root[key] = true; File.WriteAllText(index, root.ToJsonString());
        Throws<IOException>(() => OfflineBundle.Load(directory));
    }
    var missing = MakeBundle("missing"); File.Delete(Path.Combine(missing, "python.zip"));
    Throws<IOException>(() => OfflineBundle.Load(missing));
    var hashless = MakeBundle("hashless"); var path = Path.Combine(hashless, "index.json");
    var data = JsonNode.Parse(File.ReadAllText(path))!; data["versions"]![0]!.AsObject().Remove("hash"); File.WriteAllText(path, data.ToJsonString());
    Throws<IOException>(() => OfflineBundle.Load(hashless));
    data["versions"]!.AsArray().Add(data["versions"]![0]!.DeepClone()); File.WriteAllText(path, data.ToJsonString());
    Throws<FormatException>(() => OfflineBundle.Load(hashless));
});
await CheckAsync("Corrupt packages and unsafe ZIP entries fail before PIM executes", async () =>
{
    foreach (var name in new[] { "tampered", "unsafe-zip" })
    {
        var directory = MakeBundle(name, name == "unsafe-zip" ? "../outside.txt" : "python.txt");
        var bundle = OfflineBundle.Load(directory);
        if (name == "tampered") File.AppendAllText(Path.Combine(directory, "python.zip"), "changed");
        var called = false;
        var client = new PimClient(new FakeRunner((_, arguments) =>
        { if (arguments[0] != "help") called = true; return Task.FromResult(new CommandResult(0, "Python installation manager 26.3\n--only-managed --online", "")); }), lockPath);
        await client.DiscoverAsync(fakeManager);
        try { await client.InstallOfflineAsync(bundle, bundle.Runtimes.Single(), _ => { }); throw new Exception("Unsafe package accepted"); }
        catch (IOException) { }
        Require(!called, "PIM executed an unsafe package");
    }
});
await CheckAsync("Offline install uses only a verified local source without online lookup", async () =>
{
    var bundle = OfflineBundle.Load(MakeBundle("offline-command")); var calls = 0;
    var client = new PimClient(new FakeRunner((_, arguments) =>
    {
        if (arguments[0] == "help") return Task.FromResult(new CommandResult(0, "Python installation manager 26.3\n--only-managed --online", ""));
        calls++;
        Require(arguments[0] == "install" && !arguments.Contains("--by-id") && arguments.Contains("--dry-run") && arguments[^1] == runtime.Selector, "Wrong offline command");
        var source = arguments.Single(a => a.StartsWith("--source="))[9..];
        Require(File.Exists(source) && !arguments.Any(a => a.Contains("https:")), "Network or missing source");
        Require(OfflineBundle.Load(Path.GetDirectoryName(source)!).Runtimes.Single().Id == runtime.Id, "Wrong offline snapshot");
        return Task.FromResult(new CommandResult(0, "", ""));
    }), lockPath);
    await client.DiscoverAsync(fakeManager); await client.InstallOfflineAsync(bundle, bundle.Runtimes.Single(), _ => { }, dryRun: true);
    Require(calls == 1, "Offline install queried a catalog");
});
await CheckAsync("Offline download uses a fresh folder and never issues a managed installation", async () =>
{
    var fixture = MakeBundle("download-source"); var calls = 0;
    var client = new PimClient(new FakeRunner((_, arguments) =>
    {
        if (arguments[0] == "help") return Task.FromResult(new CommandResult(0, "Python installation manager 26.3\n--only-managed --online", ""));
        if (arguments[0] == "list") return Task.FromResult(new CommandResult(0, json, ""));
        calls++;
        var destination = arguments.Single(a => a.StartsWith("--download="))[11..];
        Require(destination.StartsWith(scratch + Path.DirectorySeparatorChar) && Directory.GetFiles(destination).Length == 0, "Download target was not fresh");
        foreach (var file in Directory.GetFiles(fixture)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        return Task.FromResult(new CommandResult(0, "", ""));
    }), lockPath);
    await client.DiscoverAsync(fakeManager);
    var first = await client.DownloadOfflineAsync(runtime, scratch, _ => { });
    var second = await client.DownloadOfflineAsync(runtime, scratch, _ => { });
    Require(calls == 2 && first != second && File.Exists(Path.Combine(first, "index.json")), "Existing bundle overwritten");
});
Check("PIM streamed dots give bounded stage estimates, with unknown output left indeterminate", () =>
{
    using var operation = new PimOperation();
    operation.Observe(new("Unrelated output: 81%\n", false));
    Require(operation.Current.Percent is null, "Unrelated percentage accepted");
    operation.Observe(new("Down", false)); operation.Observe(new("loading: " + new string('.', 33), false));
    Require(operation.Current == new OperationProgress(OperationPhase.Downloading, 50, true), "Chunked progress was not parsed live");
    operation.Observe(new("Installing something\n", true));
    Require(operation.Current.Percent == 50, "stderr corrupted the stdout progress line");
    operation.Observe(new(new string('.', 33) + "✅\nExtracting: " + new string('.', 34), false));
    Require(operation.Current.Phase == OperationPhase.Extracting && operation.Current.Percent == 50, "Stage reset failed");
    operation.Observe(new(new string('.', 34) + "✅\n", false));
    Require(operation.Current.Phase == OperationPhase.Finalizing && operation.Current.Percent is null, "Stage completion claimed whole-operation completion");
    operation.Observe(new("Downloading: " + new string('.', 66) + "\n", false));
    Require(operation.Current.Phase == OperationPhase.Verifying, "PIM 26.3 plain-text completion not recognized");
    operation.Cancel(); operation.Report(OperationPhase.Downloading, 100);
    Require(operation.Current.Phase == OperationPhase.Stopping, "Late progress overwrote cancellation");
});
await CheckAsync("Cancellation stops only the launched process tree and receives progress before EOF", async () =>
{
    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var childPid = 0; var bitsDisabled = false;
    using var operation = new PimOperation(progress => { if (progress.Percent == 50) ready.TrySetResult(); });
    var task = new ProcessRunner().RunAsync(Environment.ProcessPath!, ["install", "--cancellation-parent"], line =>
    {
        if (line.StartsWith("CHILD:")) childPid = int.Parse(line[6..]);
        if (line == "BITS:0") bitsDisabled = true;
    }, operation.Token, operation.Observe);
    try
    {
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Require(!task.IsCompleted, "Progress arrived only after the process ended");
    }
    finally { operation.Cancel(); }
    try { await task.WaitAsync(TimeSpan.FromSeconds(10)); throw new Exception("Cancelled process reported success"); }
    catch (OperationCanceledException) { }
    Require(childPid > 0 && bitsDisabled, "Process identity or cancellable download environment missing");
    try
    {
        using var child = System.Diagnostics.Process.GetProcessById(childPid);
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }
    catch (ArgumentException) { }
});
await CheckAsync("Already-cancelled operations never start a child or stage an offline package", async () =>
{
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    var marker = Path.Combine(scratch, "cancelled-marker");
    try { await new ProcessRunner().RunAsync(Environment.ProcessPath!, ["--touch-marker", marker], cancellationToken: cancellation.Token); throw new Exception("Child started"); }
    catch (OperationCanceledException) { }
    Require(!File.Exists(marker), "Cancelled child touched disk");
    var bundle = OfflineBundle.Load(MakeBundle("cancelled-prepare"));
    try { using var ignored = await bundle.PrepareAsync(bundle.Runtimes.Single(), cancellation.Token); throw new Exception("Cancelled bundle prepared"); }
    catch (OperationCanceledException) { }
});
await CheckAsync("Cancellation keeps the operation lock until the child task finishes, then permits retry", async () =>
{
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var starts = 0;
    var runner = new OperationRunner(async (arguments, token, observe) =>
    {
        if (arguments[0] == "help") return new CommandResult(0, "Python installation manager 26.3\n--only-managed --online", "");
        if (arguments[0] == "list") return new CommandResult(0, json, "");
        if (++starts == 1)
        {
            started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) { await release.Task; throw; }
        }
        return new CommandResult(0, "", "");
    });
    var first = new PimClient(runner, lockPath); var second = new PimClient(runner, lockPath);
    await first.DiscoverAsync(fakeManager); await second.DiscoverAsync(fakeManager);
    using var operation = new PimOperation();
    var running = first.ChangeAsync(RuntimeAction.Install, runtime, _ => { }, operation);
    await started.Task; operation.Cancel();
    try { await second.ChangeAsync(RuntimeAction.Install, runtime, _ => { }); throw new Exception("Lease released before cancellation finished"); }
    catch (InvalidOperationException ex) { Require(ex.Message.Contains("Another PyDeck"), "Unexpected lease error"); }
    finally { release.TrySetResult(); }
    try { await running; throw new Exception("Cancellation swallowed"); } catch (OperationCanceledException) { }
    await second.ChangeAsync(RuntimeAction.Install, runtime, _ => { });
});
await ManagementChecks.RunAsync(Check, CheckAsync, scratch);
await T3Checks.RunAsync(Check, CheckAsync, scratch);
if (args.Contains("--live"))
{
    await CheckAsync("Live PIM read-only integration", async () =>
    {
        var client = new PimClient(new ProcessRunner()); Require(await client.DiscoverAsync(), client.LastDiscoveryError);
        var items = await client.ListAsync(); Console.WriteLine($"  Installed: {items.Count}, default: {items.FirstOrDefault(r => r.IsDefault)?.Version ?? "none"}");
        var online = await client.ListAsync(true); Require(online.Count > 0, "Online catalog is empty"); Console.WriteLine($"  Online: {online.Count}");
    });
}
var fixtureArgument = Array.IndexOf(args, "--offline-fixture");
if (fixtureArgument >= 0 && fixtureArgument + 1 < args.Length)
{
    await CheckAsync("Real PIM offline dry-run and isolated extraction preserve installed runtimes and default", async () =>
    {
        var client = new PimClient(new ProcessRunner()); Require(await client.DiscoverAsync(), client.LastDiscoveryError);
        var before = await client.ListAsync();
        var bundle = OfflineBundle.Load(args[fixtureArgument + 1]); var selected = bundle.Runtimes.Single();
        Require(selected.IsEmbeddable, "Use the official embeddable fixture for isolated live verification");
        await client.InstallOfflineAsync(bundle, selected, Console.WriteLine, dryRun: true);
        using var prepared = await bundle.PrepareAsync(selected);
        var target = Path.Combine(scratch, "isolated-python");
        var stages = new System.Collections.Concurrent.ConcurrentBag<OperationProgress>();
        using var operation = new PimOperation(stages.Add);
        var extraction = await new ProcessRunner().RunAsync(client.Executable!, ["install", "--yes", "--by-id",
            "--source=" + prepared.IndexPath, "--config=" + prepared.ConfigPath, "--target=" + target, selected.Id], cancellationToken: operation.Token, observe: operation.Observe);
        Require(extraction.ExitCode == 0, "Isolated extraction failed: " + extraction.Error + extraction.Output);
        Console.WriteLine("  PIM progress output: " + extraction.Output.Trim());
        Console.WriteLine("  Stages: " + string.Join(", ", stages.Select(p => p.Phase).Distinct()));
        Require(stages.Any(p => p.Phase == OperationPhase.Extracting) && stages.Any(p => p.Phase == OperationPhase.Finalizing), "Real PIM progress was not recognized");
        var version = await new ProcessRunner().RunAsync(Path.Combine(target, "python.exe"), ["--version"]);
        Require(version.ExitCode == 0 && version.Output.Contains(selected.Version), "Extracted Python did not run");
        Console.WriteLine("  Isolated runtime: " + version.Output.Trim() + " in " + target);
        var after = await client.ListAsync();
        Require(before.SequenceEqual(after), "Installed runtimes or default changed during isolated verification");
    });
    if (args.Contains("--cancel-live-download"))
    {
        await CheckAsync("Real PIM offline-bundle download can be stopped without leaving a BITS job or changing installed runtimes", async () =>
        {
            var client = new PimClient(new ProcessRunner()); Require(await client.DiscoverAsync(), client.LastDiscoveryError);
            var before = await client.ListAsync();
            var runtime = OfflineBundle.Load(args[fixtureArgument + 1]).Runtimes.Single();
            var parent = Path.Combine(scratch, "cancelled-download"); Directory.CreateDirectory(parent);
            PimOperation? operation = null;
            operation = new PimOperation(progress =>
            {
                if (progress.Phase == OperationPhase.Downloading && progress.Percent is >= 0 and < 90) operation!.Cancel();
            });
            using (operation)
            {
                var cancelled = false;
                try { await client.DownloadOfflineAsync(runtime, parent, Console.WriteLine, operation); }
                catch (OperationCanceledException) when (operation.IsCancellationRequested) { cancelled = true; }
                Require(cancelled, "Download completed before an interruption could be observed");
                Require(!Directory.EnumerateFiles(parent, "*.job", SearchOption.AllDirectories).Any(), "A BITS job file was left in the isolated download folder");
            }
            Require(before.SequenceEqual(await client.ListAsync()), "Download cancellation changed installed runtimes");
            Console.WriteLine("  Cancelled download destination: " + parent);
        });
    }
}
Console.WriteLine($"\n{passed} passed; {failures.Count} failed.");
return failures.Count == 0 ? 0 : 1;

sealed class FakeRunner(Func<string, IReadOnlyList<string>, Task<CommandResult>> handler) : IProcessRunner
{
    public Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, Action<string>? output = null, CancellationToken cancellationToken = default, Action<ProcessOutput>? observe = null) => handler(executable, arguments);
}

sealed class OperationRunner(Func<IReadOnlyList<string>, CancellationToken, Action<ProcessOutput>?, Task<CommandResult>> handler) : IProcessRunner
{
    public Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, Action<string>? output = null, CancellationToken cancellationToken = default, Action<ProcessOutput>? observe = null)
        => handler(arguments, cancellationToken, observe);
}
