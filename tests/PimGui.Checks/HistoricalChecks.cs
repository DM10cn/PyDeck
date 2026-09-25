using PimGui.Core;
using System.Net;
using System.Text.Json.Nodes;

static class HistoricalChecks
{
    public static async Task RunAsync(Action<string, Action> check, Func<string, Func<Task>, Task> checkAsync, string scratch)
    {
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        static JsonObject Entry(string version, string id = "pythoncore-3.14-64", string tag = "3.14-64") => new()
        {
            ["id"] = id, ["company"] = "PythonCore", ["tag"] = tag, ["sort-version"] = version,
            ["install-for"] = new JsonArray(version + (tag.Contains('t') ? "t" : "") + "-64", tag),
            ["url"] = "packages/" + version + ".zip", ["hash"] = new JsonObject { ["sha256"] = new string('0', 64) }
        };
        static string Page(string? next, params JsonObject[] entries)
        {
            var root = new JsonObject { ["versions"] = new JsonArray(entries.Select(e => (JsonNode)e.DeepClone()).ToArray()) };
            if (next is not null) root["next"] = next;
            return root.ToJsonString();
        }
        async Task RejectAsync<T>(Func<Task> action) where T : Exception
        { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
        check("Catalog identities distinguish historical micro releases while installed IDs stay unique", () =>
        {
            var json = Page(null, Entry("3.14.7"), Entry("3.14.6"));
            var catalog = RuntimeParser.ParseCatalog(json);
            Require(catalog.Count == 2 && catalog[0].CatalogIdentity != catalog[1].CatalogIdentity && !catalog[0].SameIdentity(catalog[1]), "Micro identity collapsed");
            try { RuntimeParser.Parse(json); throw new Exception("Duplicate installed IDs accepted"); } catch (FormatException) { }
            try { RuntimeParser.ParseCatalog(Page(null, Entry("3.14.7"), Entry("3.14.7"))); throw new Exception("Duplicate exact identity accepted"); } catch (FormatException) { }
        });
        check("Exact selectors preserve micro, architecture and free-threaded preview variants", () =>
        {
            var stable = RuntimeParser.Parse(Page(null, Entry("3.14.6"))).Single();
            Require(stable.ExactSelector == "PythonCore/3.14.6-64", "Minor alias used");
            var preview = RuntimeParser.Parse(Page(null, Entry("3.15.0rc2", "pythoncore-3.15t-64", "3.15t-dev-64"))).Single();
            Require(preview.ExactSelector == "PythonCore/3.15.0rc2t-64", "Preview/threaded selector lost");
            Require(PimClient.BuildArguments(RuntimeAction.Install, stable).Last() == stable.ExactSelector, "Install not exact");
            Require(PimClient.BuildArguments(RuntimeAction.Repair, stable with { IsManaged = true }).Last() == stable.ExactSelector, "Repair not exact");
            Require(PimClient.BuildArguments(RuntimeAction.Update, stable with { IsManaged = true }).Last() == stable.Selector, "Update pinned to old micro");
            var versions = new[] { "3.15.0a9", "3.15.0rc1", "3.15.0b3", "3.15.0rc2", "3.15.0" };
            Require(RuntimeCatalog.Filter(versions.Select(v => preview with { Version = v }), "All architectures", true, "").Select(r => r.Version)
                .SequenceEqual(new[] { "3.15.0", "3.15.0rc2", "3.15.0rc1", "3.15.0b3", "3.15.0a9" }), "History prereleases sorted incorrectly");
        });
        await checkAsync("History pagination retains every micro, relative package URL and selected source", async () =>
        {
            var handler = new CatalogHandler(uri => new(HttpStatusCode.OK) { Content = new StringContent(uri.AbsolutePath.EndsWith("index.json")
                ? Page("recent.json", Entry("3.14.7"), Entry("3.14.6")) : Page(null, Entry("3.14.6"), Entry("3.14.5"))) });
            using var http = new HttpClient(handler);
            var items = await HistoricalCatalog.LoadAsync(http, "https://catalog.example.test/index.json");
            Require(handler.Requests.Count == 2 && items.Select(i => i.Version).SequenceEqual(new[] { "3.14.7", "3.14.6", "3.14.5" }), "History incomplete/order wrong");
            Require(items.All(i => i.CatalogIndex == "https://catalog.example.test/index.json" && i.DownloadMetadata!.Contains("https://catalog.example.test/packages/")), "Source or relative URL lost");
        });
        await checkAsync("History rejects loops, cross-origin links, HTTPS downgrade and conflicting entries", async () =>
        {
            foreach (var next in new[] { "index.json", "http://catalog.example.test/old.json", "https://unapproved.example.test/old.json" })
            {
                var handler = new CatalogHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Page(next, Entry("3.14.7"))) });
                using var http = new HttpClient(handler);
                try { await HistoricalCatalog.LoadAsync(http, "https://catalog.example.test/index.json"); throw new Exception("Unsafe next accepted"); }
                catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException) { }
                Require(handler.Requests.Count == 1, "Contacted unapproved page");
            }
            var changed = Entry("3.14.7"); changed["hash"]!["sha256"] = new string('1', 64);
            var conflict = new CatalogHandler(uri => new(HttpStatusCode.OK) { Content = new StringContent(uri.AbsolutePath.EndsWith("index.json") ? Page("old.json", Entry("3.14.7")) : Page(null, changed)) });
            using (var http = new HttpClient(conflict)) await RejectAsync<IOException>(async () => { await HistoricalCatalog.LoadAsync(http, "https://catalog.example.test/index.json"); });
            var redirect = new CatalogHandler(_ => { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new("https://unapproved.example.test/index.json"); return response; });
            using (var http = new HttpClient(redirect)) await RejectAsync<InvalidDataException>(async () => { await HistoricalCatalog.LoadAsync(http, "https://catalog.example.test/index.json"); });
            Require(redirect.Requests.Count == 1, "Followed unapproved redirect");
        });
        await checkAsync("History enforces page count, payload size and cancellation", async () =>
        {
            var pages = 0;
            var many = new CatalogHandler(_ => new(HttpStatusCode.OK) { Content = new StringContent(Page("page-" + (++pages) + ".json")) });
            using (var http = new HttpClient(many)) await RejectAsync<IOException>(async () => { await HistoricalCatalog.LoadAsync(http, "https://catalog.example.test/index.json"); });
            Require(pages == 64, "Pagination bound not enforced");
            var large = new CatalogHandler(_ => { var content = new StringContent("{}"); content.Headers.ContentLength = 9 * 1024 * 1024; return new(HttpStatusCode.OK) { Content = content }; });
            using (var http = new HttpClient(large)) await RejectAsync<IOException>(async () => { await HistoricalCatalog.LoadAsync(http, "https://catalog.example.test/index.json"); });
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            using (var http = new HttpClient(large)) await RejectAsync<OperationCanceledException>(async () => { await HistoricalCatalog.LoadAsync(http, "https://catalog.example.test/index.json", cancellationToken: cancelled.Token); });
        });
        await checkAsync("Historical mutations resolve through PIM and refuse a different micro or failed feed validation", async () =>
        {
            var selected = RuntimeParser.Parse(Page(null, Entry("3.14.6"))).Single();
            foreach (var failure in new[] { "wrong-version", "signature", "success" })
            {
                var commands = new List<string[]>();
                var runner = new FakeRunner((_, arguments) =>
                {
                    var args = arguments.ToArray(); commands.Add(args);
                    if (args[0] == "help") return Task.FromResult(new CommandResult(0, "Python installation manager 26.3 --only-managed --online --force", ""));
                    if (args[0] == "list" && !args.Contains("--online")) return Task.FromResult(new CommandResult(0, Page(null), ""));
                    if (failure == "signature" && args[0] == "list") return Task.FromResult(new CommandResult(1, "", "Signature validation failed"));
                    return Task.FromResult(new CommandResult(0, args[0] == "list" ? Page(null, Entry(failure == "wrong-version" ? "3.14.7" : "3.14.6")) : "done", ""));
                });
                var client = new PimClient(runner, Path.Combine(scratch, "history-" + failure + ".lock"));
                Require(await client.DiscoverAsync(Path.Combine(scratch, "pymanager.exe")), "Discovery");
                if (failure == "success") await client.ChangeAsync(RuntimeAction.Install, selected, _ => { });
                else await RejectAsync<InvalidOperationException>(() => client.ChangeAsync(RuntimeAction.Install, selected, _ => { }));
                Require(commands.Where(c => c[0] == "list" && c.Contains("--online")).All(c => c.Contains(selected.ExactSelector)), "Validation used minor selector");
                Require(commands.Count(c => c[0] == "install") == (failure == "success" ? 1 : 0), "Mutation happened before exact validation");
                if (failure == "success") Require(commands.Single(c => c[0] == "install").Contains(selected.ExactSelector), "Latest installed instead");
            }
        });
        await checkAsync("Replacing a shared PIM ID requires acknowledgement of the current installed micro", async () =>
        {
            var selected = RuntimeParser.Parse(Page(null, Entry("3.14.6"))).Single();
            var installed = "3.14.7"; var mutations = 0;
            var runner = new FakeRunner((_, args) =>
            {
                if (args[0] == "help") return Task.FromResult(new CommandResult(0, "Python installation manager 26.3 --only-managed --online --force", ""));
                if (args[0] == "install") { mutations++; return Task.FromResult(new CommandResult(0, "done", "")); }
                return Task.FromResult(new CommandResult(0, Page(null, Entry(args.Contains("--online") ? selected.Version : installed)), ""));
            });
            var client = new PimClient(runner, Path.Combine(scratch, "history-replacement.lock"));
            Require(await client.DiscoverAsync(Path.Combine(scratch, "pymanager.exe")), "Discovery");
            await RejectAsync<InvalidOperationException>(() => client.ChangeAsync(RuntimeAction.Install, selected, _ => { }));
            await RejectAsync<InvalidOperationException>(() => client.ChangeAsync(RuntimeAction.Install, selected, _ => { }, expectedInstalledVersion: "3.14.5"));
            Require(mutations == 0, "Unconfirmed replacement executed");
            await client.ChangeAsync(RuntimeAction.Install, selected, _ => { }, expectedInstalledVersion: installed);
            Require(mutations == 1 && client.LastExpectedRuntime?.Version == "3.14.6", "Acknowledged exact replacement failed");
        });
        await checkAsync("Historical repair stays on its micro, Update advances, and changed registrations stop both", async () =>
        {
            var installed = RuntimeParser.Parse(Page(null, Entry("3.14.6"))).Single() with { IsManaged = true };
            foreach (var action in new[] { RuntimeAction.Repair, RuntimeAction.Update })
            {
                var raced = false; var mutations = new List<string[]>();
                var runner = new FakeRunner((_, args) =>
                {
                    if (args[0] == "help") return Task.FromResult(new CommandResult(0, "Python installation manager 26.3 --only-managed --online --force", ""));
                    if (args[0] == "install") { mutations.Add(args.ToArray()); return Task.FromResult(new CommandResult(0, "done", "")); }
                    var version = args.Contains("--online") ? (action == RuntimeAction.Repair ? "3.14.6" : "3.14.7")
                        : raced && args.Contains(installed.Selector) ? "3.14.5" : "3.14.6";
                    return Task.FromResult(new CommandResult(0, Page(null, Entry(version)), ""));
                });
                var client = new PimClient(runner, Path.Combine(scratch, "history-" + action + ".lock"));
                Require(await client.DiscoverAsync(Path.Combine(scratch, "pymanager.exe")), "Discovery");
                await client.ChangeAsync(action, installed, _ => { });
                Require(mutations.Count == 1 && client.LastExpectedRuntime?.Version == (action == RuntimeAction.Repair ? "3.14.6" : "3.14.7"), "Wrong mutation target");
                Require(mutations[0].Contains(action == RuntimeAction.Repair ? installed.ExactSelector : installed.Selector), "Wrong mutation selector");
                raced = true;
                await RejectAsync<InvalidOperationException>(() => client.ChangeAsync(action, installed, _ => { }));
                Require(mutations.Count == 1, "Changed registration mutated");
            }
        });
        await checkAsync("Install rechecks external PIM changes after resolution and before mutation", async () =>
        {
            var selected = RuntimeParser.Parse(Page(null, Entry("3.14.6"))).Single();
            foreach (var acknowledged in new string?[] { null, "3.14.7" })
            {
                var resolved = false; var mutations = 0; var installedReadsAfterResolution = 0;
                var runner = new FakeRunner((_, args) =>
                {
                    if (args[0] == "help") return Task.FromResult(new CommandResult(0, "Python installation manager 26.3 --only-managed --online --force", ""));
                    if (args[0] == "install") { mutations++; return Task.FromResult(new CommandResult(0, "done", "")); }
                    if (args.Contains("--online"))
                    {
                        resolved = true;
                        return Task.FromResult(new CommandResult(0, Page(null, Entry(selected.Version)), ""));
                    }
                    if (resolved) installedReadsAfterResolution++;
                    var state = resolved ? Page(null, Entry("3.14.8")) : acknowledged is null ? Page(null) : Page(null, Entry(acknowledged));
                    return Task.FromResult(new CommandResult(0, state, ""));
                });
                var client = new PimClient(runner, Path.Combine(scratch, "history-external-" + (acknowledged ?? "absent") + ".lock"));
                Require(await client.DiscoverAsync(Path.Combine(scratch, "pymanager.exe")), "Discovery");
                await RejectAsync<InvalidOperationException>(() => client.ChangeAsync(RuntimeAction.Install, selected, _ => { }, expectedInstalledVersion: acknowledged));
                Require(mutations == 0 && installedReadsAfterResolution > 0, "External version change was overwritten");
            }
        });
        await checkAsync("Offline history prepares only the selected micro from a shared-ID bundle", async () =>
        {
            var directory = Path.Combine(scratch, "offline-history"); Directory.CreateDirectory(directory);
            var archive = Path.Combine(directory, "package.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
            { using var writer = new StreamWriter(zip.CreateEntry("python.exe").Open()); writer.Write("fixture only"); }
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archive)));
            var versions = new[] { Entry("3.14.7"), Entry("3.14.6") };
            foreach (var entry in versions) { entry["url"] = "package.zip"; entry["hash"]!["sha256"] = hash; }
            File.WriteAllText(Path.Combine(directory, "index.json"), Page(null, versions));
            var bundle = OfflineBundle.Load(directory);
            Require(bundle.Runtimes.Count == 2, "Offline micros collapsed");
            using var prepared = await bundle.PrepareAsync(bundle.Runtimes.Single(r => r.Version == "3.14.6"));
            Require(RuntimeParser.Parse(File.ReadAllText(prepared.IndexPath)).Single().Version == "3.14.6", "Prepared another micro");
        });
        await checkAsync("Legacy offline bundles receive an exact private alias and replace only the confirmed micro", async () =>
        {
            var directory = Path.Combine(scratch, "legacy-offline-history"); Directory.CreateDirectory(directory);
            var archive = Path.Combine(directory, "package.zip");
            using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
            { using var writer = new StreamWriter(zip.CreateEntry("python.exe").Open()); writer.Write("fixture only"); }
            var entry = Entry("3.14.6"); entry.Remove("install-for"); entry["url"] = "package.zip";
            entry["hash"]!["sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archive)));
            var original = Page(null, entry);
            File.WriteAllText(Path.Combine(directory, "index.json"), original);
            var bundle = OfflineBundle.Load(directory); var selected = bundle.Runtimes.Single(); var mutations = 0;
            var runner = new FakeRunner((_, args) =>
            {
                if (args[0] == "help") return Task.FromResult(new CommandResult(0, "Python installation manager 26.3 --only-managed --online --force", ""));
                if (args[0] == "list") return Task.FromResult(new CommandResult(0, Page(null, Entry("3.14.7")), ""));
                mutations++;
                Require(args.Contains(selected.ExactSelector) && !args.Contains(selected.Selector), "Offline install used the minor alias");
                var source = args.Single(a => a.StartsWith("--source="))["--source=".Length..];
                var staged = JsonNode.Parse(File.ReadAllText(source))!["versions"]!.AsArray().Single()!;
                Require(staged["sort-version"]!.GetValue<string>() == selected.Version && staged["install-for"]!.AsArray()
                    .Any(tag => tag!.GetValue<string>() == "3.14.6-64"), "Legacy snapshot cannot resolve the exact request");
                return Task.FromResult(new CommandResult(0, "installed", ""));
            });
            var client = new PimClient(runner, Path.Combine(scratch, "legacy-offline-history.lock"));
            Require(await client.DiscoverAsync(Path.Combine(scratch, "pymanager.exe")), "Discovery");
            await client.InstallOfflineAsync(bundle, selected, _ => { }, expectedInstalledVersion: "3.14.7");
            Require(mutations == 1 && File.ReadAllText(Path.Combine(directory, "index.json")) == original, "Mutation missing or original bundle changed");
        });
    }

    private sealed class CatalogHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); Requests.Add(request.RequestUri!); return Task.FromResult(respond(request.RequestUri!)); }
    }
}
