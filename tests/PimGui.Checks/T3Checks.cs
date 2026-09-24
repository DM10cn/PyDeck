using PimGui.Core;
using System.Text.Json.Nodes;

static class T3Checks
{
    public static async Task RunAsync(Action<string, Action> check, Func<string, Func<Task>, Task> checkAsync, string scratch)
    {
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Unsafe input accepted"); }
        check("App updates compare numeric stable versions and pin browser destination", () =>
        {
            var json = "{\"tag_name\":\"v0.10.0\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://untrusted.example\"}";
            var release = AppUpdates.Parse(json, new Version(0, 6, 1, 0));
            Require(release?.Page.AbsoluteUri == "https://github.com/DM10cn/PyDeck/releases/tag/v0.10.0", "Untrusted release URL");
            Require(AppUpdates.Parse(json, new Version(0, 10, 0, 0)) is null, "Same version update");
            Require(AppUpdates.Parse(json.Replace("v0.10.0", "v0.11.0-beta"), new Version(0, 6, 1)) is null, "Historical beta accepted");
            Require(AppUpdates.Parse(json.Replace("\"draft\":false", "\"draft\":true"), new Version(0, 6, 1)) is null, "Draft accepted");
        });
        check("Installation source rejects HTTP, embedded credentials and query strings", () =>
        {
            foreach (var bad in new[] { "http://example.test/index.json", "https://user:secret@example.test/index.json", "https://example.test/index.json?token=secret", "file:///C:/index.json", "https://example.test/#fragment" }) Reject(() => InstallationSource.Validate(bad));
            Require(InstallationSource.Validate("") == InstallationSource.Official, "Default source");
            Require(!InstallationSource.SameOrigin(new("https://example.test"), new("https://example.test:444")), "Port boundary");
        });
        check("Minor series and distribution filters preserve independent preview classification", () =>
        {
            var r = new PythonRuntime("test", "PythonTest", "3.15t-64", "3.15.0rc1", "Python", "", "", false);
            Require(RuntimeCatalog.MinorSeries(r) == "3.15" && RuntimeCatalog.MatchesDistribution(r, "Tests") && RuntimeCatalog.MatchesDistribution(r, "FreeThreaded") && r.IsPrerelease, "Combined variant flags lost");
            Require(!RuntimeCatalog.MatchesDistribution(r, "Embedded") && !RuntimeCatalog.MatchesDistribution(r, "Other"), "Wrong distribution");
        });
        check("Shebang edits preserve advanced values and reject executable commands", () =>
        {
            var path = Path.Combine(scratch, "shebang.json");
            File.WriteAllText(path, "{\"unknown\":123,\"shebang_templates\":{\"custom\":\"C:\\\\custom.exe -x\"}}");
            var before = PimConfiguration.Read(path, false);
            var rules = (JsonObject)before.Values["shebang_templates"]!.DeepClone();
            rules["/usr/bin/example"] = "py -V:PythonCore/3.14-64";
            PimConfiguration.Save(before, new() { ["shebang_templates"] = rules, ["shebang_can_run_anything"] = false }, false);
            var after = PimConfiguration.Read(path, false);
            Require(JsonNode.DeepEquals(before.Values["shebang_templates"]!["custom"], after.Values["shebang_templates"]!["custom"]) && after.Values["unknown"]!.GetValue<int>() == 123, "Advanced settings changed");
            Reject(() => ShebangRules.ValidateMapping("/usr/bin/python", "cmd.exe /c anything"));
            Reject(() => ShebangRules.ValidateMapping("#!python", "py"));
            File.AppendAllText(path, " ");
            try { PimConfiguration.Save(after, new() { ["shebang_can_run_anything"] = true }, false); throw new Exception("Stale config accepted"); } catch (IOException) { }
        });
        await checkAsync("Source snapshot rejects a stale catalog card before mutation", async () =>
        {
            var manager = Path.Combine(scratch, "source-pim", "pymanager.exe"); Directory.CreateDirectory(Path.GetDirectoryName(manager)!); File.WriteAllText(manager, "fixture");
            var commands = new List<string>();
            var runner = new FakeRunner((_, args) => { commands.Add(args[0]); return Task.FromResult(new CommandResult(0, "Python installation manager 26.3 --only-managed --online --force", "")); });
            var client = new PimClient(runner, Path.Combine(scratch, "source.lock")) { SelectedSource = () => "https://example.test/index.json" };
            Require(await client.DiscoverAsync(manager), "Discovery");
            var runtime = new PythonRuntime("python-3", "PythonCore", "3.14-64", "3.14.7", "Python", "", "", false) { CatalogIndex = InstallationSource.Official };
            try { await client.ChangeAsync(RuntimeAction.Install, runtime, _ => { }); throw new Exception("Stale source accepted"); } catch (IOException) { }
            Require(commands.All(c => c == "help"), "Mutation started");
        });
        await checkAsync("Package origins and redirects require approval before contacting a different host", async () =>
        {
            var metadata = new JsonObject { ["id"] = "pythoncore-3.14-64", ["company"] = "PythonCore", ["tag"] = "3.14-64", ["sort-version"] = "3.14.7", ["url"] = "https://cdn.example.test/package.zip", ["hash"] = new JsonObject { ["sha256"] = new string('0', 64) } };
            var runtime = RuntimeParser.Parse(new JsonObject { ["versions"] = new JsonArray(metadata) }.ToJsonString()).Single() with { CatalogIndex = "https://source.example.test/index.json" };
            var handler = new RedirectHandler(); using var http = new HttpClient(handler);
            var prompts = 0;
            try { await PackageDownload.FetchAsync(runtime, Path.Combine(scratch, "origin-denied"), http, null, _ => { prompts++; return Task.FromResult(false); }); throw new Exception("Unapproved origin accepted"); } catch (InvalidDataException) { }
            Require(prompts == 1 && handler.Requests.Count == 0, "Contacted an unapproved host");
            runtime = runtime with { CatalogIndex = "https://cdn.example.test/index.json" };
            prompts = 0;
            try { await PackageDownload.FetchAsync(runtime, Path.Combine(scratch, "redirect-denied"), http, null, _ => { prompts++; return Task.FromResult(false); }); throw new Exception("Unapproved redirect accepted"); } catch (InvalidDataException) { }
            Require(prompts == 1 && handler.Requests.Count == 1 && handler.Requests[0].Host == "cdn.example.test", "Redirect denial retried or followed");
            handler.Requests.Clear(); handler.Target = "http://cdn.example.test/insecure.zip";
            try { await PackageDownload.FetchAsync(runtime, Path.Combine(scratch, "redirect-http"), http, null, _ => Task.FromResult(true)); throw new Exception("HTTPS downgrade accepted"); } catch (ArgumentException) { }
            Require(handler.Requests.Count == 1, "Insecure redirect contacted");
        });
        check("Invalid saved source is surfaced as a preference warning", () =>
        {
            var dir = Path.Combine(scratch, "invalid-source-settings"); Directory.CreateDirectory(dir);
            var store = new SettingsStore(dir); File.WriteAllText(store.FilePath, "{\"InstallationIndex\":\"http://invalid.example.test\"}");
            Require(store.Load().InstallationIndex == "" && store.LoadWarning is not null, "Invalid saved source used silently");
        });
        check("Environment inspection and removal do not execute or delete files", () =>
        {
            var root = Path.Combine(scratch, "imported env"); Directory.CreateDirectory(Path.Combine(root, "Scripts"));
            File.WriteAllText(Path.Combine(root, "pyvenv.cfg"), "home = C:\\missing-base-92342\nversion = 3.14.7\n");
            File.WriteAllText(Path.Combine(root, "Scripts", "python.exe"), "not executable");
            var env = VirtualEnvironments.Inspect(root); Require(env.State == "Base interpreter missing", "False health");
            var registry = new VirtualEnvironments(Path.Combine(scratch, "environments-state")); registry.Remember(env); registry.Remember(env, true);
            Require(registry.Read().Count == 0 && File.Exists(env.Executable), "Removal deleted files");
        });
        await checkAsync("Venv cancellation retains incomplete directories and rejects overwrites", async () =>
        {
            var baseDir = Path.Combine(scratch, "venv-base"); Directory.CreateDirectory(baseDir);
            var exe = Path.Combine(baseDir, "python.exe"); File.WriteAllText(exe, "fixture");
            var runtime = new PythonRuntime("python", "PythonCore", "3.14-64", "3.14.7", "Python", exe, baseDir, false);
            var registry = new VirtualEnvironments(Path.Combine(scratch, "cancel-state"));
            using var operation = new PimOperation();
            var runner = new OperationRunner((args, token, observe) => { File.WriteAllText(Path.Combine(args[^1], "partial"), "keep"); operation.Cancel(); token.ThrowIfCancellationRequested(); throw new Exception(); });
            try { await registry.CreateAsync(runtime, scratch, "cancel-env", runner, operation); throw new Exception("Not cancelled"); } catch (OperationCanceledException) { }
            Require(registry.Read().Single().State == "Incomplete environment" && File.Exists(Path.Combine(scratch, "cancel-env", "partial")), "Partial state lost");
            using var second = new PimOperation();
            try { await registry.CreateAsync(runtime, scratch, "cancel-env", runner, second); throw new Exception("Overwrite accepted"); } catch (IOException) { }
        });
        var exePath = Environment.GetEnvironmentVariable("PYDECK_TEST_VENV_PYTHON");
        if (!string.IsNullOrEmpty(exePath)) await checkAsync("Real venv create, probe, import and forget preserve project files", async () =>
        {
            var runtime = new PythonRuntime("test", "PythonCore", "3", "3", "Python", exePath, Path.GetDirectoryName(exePath)!, false);
            var registry = new VirtualEnvironments(Path.Combine(scratch, "real-state")); using var operation = new PimOperation();
            var env = await registry.CreateAsync(runtime, scratch, "真实 env", new ProcessRunner(), operation);
            Require(env.State == "Environment ready", env.State);
            Require(VirtualEnvironments.Inspect(env.Path).State == "Not checked", "Import failed");
            registry.Remember(env, true); Require(File.Exists(env.Executable), "Files removed");
            Console.WriteLine("  Real environment retained for inspection: " + env.Path);
        });
    }
    private sealed class RedirectHandler : HttpMessageHandler
    {
        public string Target { get; set; } = "https://other.example.test/package.zip";
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
            response.Headers.Location = new Uri(Target);
            return Task.FromResult(response);
        }
    }
}
