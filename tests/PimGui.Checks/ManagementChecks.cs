using PimGui.Core;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

static class ManagementChecks
{
    public static async Task RunAsync(Action<string, Action> check, Func<string, Func<Task>, Task> checkAsync, string scratch)
    {
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        check("Configuration editing preserves unknown fields and detects external changes", () =>
        {
            var path = Path.Combine(scratch, "management.json");
            File.WriteAllText(path, "{\"unknown\":{\"keep\":true},\"default_tag\":\"3.12\"}");
            var snapshot = PimConfiguration.Read(path, false);
            var backup = PimConfiguration.Save(snapshot, new JsonObject { ["default_tag"] = "PythonCore/3.13-64", ["automatic_install"] = false }, false);
            Require(File.Exists(backup) && JsonNode.Parse(File.ReadAllText(path))!["unknown"]!["keep"]!.GetValue<bool>(), "Unrelated configuration lost");
            try { PimConfiguration.Save(snapshot, new JsonObject { ["default_tag"] = "3.14" }, false); throw new Exception("Stale edit accepted"); }
            catch (IOException) { }
            var current = PimConfiguration.Read(path, false);
            PimConfiguration.Save(current, new JsonObject { ["default_tag"] = null }, false);
            Require(!PimConfiguration.Read(path, false).Values.ContainsKey("default_tag"), "Reset did not remove override");
        });
        check("Proxy settings reject embedded credentials and secrets never enter settings JSON", () =>
        {
            try { new NetworkSettings("Custom", "http://user:secret@example.test").Validate(); throw new Exception("Credential URL accepted"); } catch (ArgumentException) { }
            var settings = new NetworkSettings("Custom", "http://localhost:7890", "person");
            var env = settings.Environment("test-password");
            Require(env["HTTPS_PROXY"]!.Contains("test-password"), "Child proxy credential missing");
            Require(!System.Text.Json.JsonSerializer.Serialize(settings).Contains("test-password"), "Secret serialized");
            Require(SensitiveText.Redact("test-password http://person:test-password@localhost", "test-password").Contains("test-password") == false, "Secret leaked");
            Require(new NetworkSettings("Direct").Environment(null)["NO_PROXY"] == "*", "Direct mode not explicit");
            Require(new NetworkSettings().Environment(null).Count == 0, "System proxy environment changed");
        });
        check("KB / MB boundary and unknown transfer totals", () =>
        {
            Require(TransferUnits.Bytes(1024 * 1024 - 1).EndsWith("KB") && TransferUnits.Bytes(1024 * 1024).EndsWith("MB"), "Unit boundary");
            using var operation = new PimOperation(); operation.Transfer(1024, null, 512, null);
            Require(operation.Current.Percent is null && operation.Current.Remaining is null && operation.Current.DownloadedBytes == 1024, "Invented progress");
            operation.Cancel(); operation.Transfer(2048, 4096, 512, TimeSpan.FromSeconds(4));
            Require(operation.Current.Phase == OperationPhase.Stopping, "Late transfer overwrote cancellation");
        });
        await checkAsync("Live output never leaks credentials split across the line limit", async () =>
        {
            var lines = new List<string>();
            var result = await new ProcessRunner(password: () => "boundary-secret-value").RunAsync(Environment.ProcessPath!, ["--emit-proxy-secret"], lines.Add);
            Require(result.ExitCode == 0 && !result.Output.Contains("boundary-secret-value"), "Captured secret leaked");
            Require(lines.All(line => !line.Contains("boundary") && !line.Contains("secret-value")) && lines.Contains("[Long output line omitted]"), "Streaming secret fragment leaked");
        });
        check("Update comparison prevents downgrade and orders prereleases", () =>
        {
            Require(RuntimeCatalog.CompareVersions("3.14.6", "3.14.7") < 0 && RuntimeCatalog.CompareVersions("3.14.7", "3.14.7") == 0, "Patch comparison");
            Require(RuntimeCatalog.CompareVersions("3.15.0rc1", "3.15.0") < 0 && RuntimeCatalog.CompareVersions("3.15.0b1", "3.15.0a9") > 0, "Preview comparison");
        });
        check("PATH diagnostics report shadowing without executing candidates", () =>
        {
            var first = Path.Combine(scratch, "path-first"); var second = Path.Combine(scratch, "path-second"); Directory.CreateDirectory(first); Directory.CreateDirectory(second);
            File.WriteAllText(Path.Combine(first, "python.exe"), "not executable"); File.WriteAllText(Path.Combine(second, "python.exe"), "not executable");
            var report = PathDiagnostics.Inspect([], null, first + Path.PathSeparator + second, second);
            var python = report.Commands.Single(c => c.Command == "python");
            Require(python.Candidates.Count == 2 && python.Path == Path.Combine(first, "python.exe") && report.PathChanged && python.Version is null, "PATH diagnostics incorrect");
        });
        await checkAsync("PIM compatibility probes reject legacy CLI and rediscover changed capabilities", async () =>
        {
            var manager = Path.Combine(scratch, "pymanager.exe"); File.WriteAllText(manager, "fixture");
            var supports = false;
            var client = new PimClient(new FakeRunner((_, args) => Task.FromResult(new CommandResult(0,
                args.Last() == "list" ? "Python installation manager 25.0\n--only-managed --online" : supports ? "Python installation manager 26.3\n--force --by-id --refresh" : "old install help", ""))));
            Require(await client.DiscoverAsync(manager) && !client.SupportsRepair, "Old capabilities fabricated");
            supports = true;
            Require(await client.DiscoverAsync(manager) && client.SupportsRepair, "Capabilities not refreshed after manager upgrade");
        });
        await checkAsync("Health checks reject missing executables", async () =>
        {
            var runtime = new PythonRuntime("pythoncore-3.13-64", "PythonCore", "3.13-64", "3.13.15", "Python", Path.Combine(scratch, "missing.exe"), scratch, false, true);
            Require(!(await RuntimeHealth.CheckAsync(runtime)).Healthy, "Missing runtime healthy");
            Require(PimClient.BuildArguments(RuntimeAction.Repair, runtime).Contains("--force"), "Repair does not use PIM");
        });
        await checkAsync("Ambiguous selectors stop before any PIM mutation", async () =>
        {
            var runtime = new PythonRuntime("pythoncore-3.13-64", "PythonCore", "3.13-64", "3.13.15", "Python", "", "", false);
            var entry = new JsonObject { ["id"] = runtime.Id, ["company"] = runtime.Company, ["tag"] = runtime.Tag, ["sort-version"] = runtime.Version };
            var mutations = 0;
            var runner = new FakeRunner((_, args) =>
            {
                if (args[0] == "help") return Task.FromResult(new CommandResult(0, "Python installation manager 26.3\n--only-managed --online --force", ""));
                if (args[0] != "list") { mutations++; return Task.FromResult(new CommandResult(0, "", "")); }
                var items = new JsonArray(entry.DeepClone());
                if (args.Contains(runtime.ExactSelector)) { var other = entry.DeepClone(); other["id"] = "other-runtime"; items.Add(other); }
                return Task.FromResult(new CommandResult(0, new JsonObject { ["versions"] = items }.ToJsonString(), ""));
            });
            var client = new PimClient(runner, Path.Combine(scratch, "ambiguous.lock"));
            Require(await client.DiscoverAsync(Path.Combine(scratch, "pymanager.exe")), "Discovery failed");
            try { await client.ChangeAsync(RuntimeAction.Install, runtime, _ => { }); throw new Exception("Ambiguous selector accepted"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("ambiguous")) { }
            Require(mutations == 0, "Mutation executed before resolution check");
        });
        check("Configuration restore preserves reviewed content and refuses stale snapshots", () =>
        {
            var path = Path.Combine(scratch, "restore.json"); File.WriteAllText(path, "{\"unknown\":42}");
            var backup = PimConfiguration.Save(PimConfiguration.Read(path, false), new JsonObject { ["automatic_install"] = false }, false);
            var snapshot = PimConfiguration.Read(path, false);
            PimConfiguration.Restore(snapshot, backup);
            Require(File.ReadAllText(path) == "{\"unknown\":42}", "Backup was not restored exactly");
            try { PimConfiguration.Restore(snapshot, backup); throw new Exception("Stale restore accepted"); } catch (IOException) { }
        });
        await checkAsync("Failed downloads never produce an installable index", async () =>
        {
            var metadata = new JsonObject { ["id"] = "pythoncore-3.13-64", ["company"] = "PythonCore", ["tag"] = "3.13-64", ["sort-version"] = "3.13.15", ["url"] = "https://www.python.org/test.zip", ["hash"] = new JsonObject { ["sha256"] = new string('0', 64) } };
            var runtime = RuntimeParser.Parse(new JsonObject { ["versions"] = new JsonArray(metadata) }.ToJsonString()).Single();
            var directory = Path.Combine(scratch, "bad-transfer");
            using var http = new HttpClient(new BytesHandler(Encoding.UTF8.GetBytes("corrupt archive")));
            using var operation = new PimOperation();
            try { await PackageDownload.FetchAsync(runtime, directory, http, operation); throw new Exception("Bad hash accepted"); } catch (IOException) { }
            Require(!File.Exists(Path.Combine(directory, "index.json")), "Unverified index published");
        });
        await checkAsync("Interrupted download resumes at the verified byte offset and validates the full hash", async () =>
        {
            using var memory = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, true))
            { using var entry = zip.CreateEntry("python.exe").Open(); entry.Write(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8192)); }
            var bytes = memory.ToArray();
            var metadata = new JsonObject { ["id"] = "pythoncore-3.13-64", ["company"] = "PythonCore", ["tag"] = "3.13-64", ["sort-version"] = "3.13.15", ["url"] = "https://www.python.org/test.zip", ["hash"] = new JsonObject { ["sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) } };
            var runtime = RuntimeParser.Parse(new JsonObject { ["versions"] = new JsonArray(metadata) }.ToJsonString()).Single();
            var handler = new ResumeHandler(bytes); using var http = new HttpClient(handler); using var operation = new PimOperation();
            var bundle = await PackageDownload.FetchAsync(runtime, Path.Combine(scratch, "resumed-transfer"), http, operation);
            Require(handler.Requests == 2 && bundle.Runtimes.Single().Id == runtime.Id, "Download was not resumed and verified");
        });
        await checkAsync("Custom proxy reaches a real loopback proxy and surfaces authentication failure", async () =>
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            try
            {
                using var http = new NetworkSettings("Custom", $"http://127.0.0.1:{port}").CreateClient(null);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var request = http.GetAsync("http://example.invalid/test", timeout.Token);
                using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
                var buffer = new byte[8192]; var count = await peer.GetStream().ReadAsync(buffer, timeout.Token);
                Require(Encoding.ASCII.GetString(buffer, 0, count).Contains("http://example.invalid/test"), "Proxy not used");
                await peer.GetStream().WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 407 Proxy Authentication Required\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), timeout.Token);
                using var response = await request;
                Require(response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired, "Proxy failure hidden");
            }
            finally { listener.Stop(); }
        });
        await checkAsync("Connection latency is excluded from speed and ETA sampling", async () =>
        {
            using var memory = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, true))
            { using var entry = zip.CreateEntry("python.exe").Open(); entry.Write(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8192)); }
            var bytes = memory.ToArray();
            var metadata = new JsonObject { ["id"] = "pythoncore-3.13-64", ["company"] = "PythonCore", ["tag"] = "3.13-64", ["sort-version"] = "3.13.15", ["url"] = "https://www.python.org/test.zip", ["hash"] = new JsonObject { ["sha256"] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)) } };
            var runtime = RuntimeParser.Parse(new JsonObject { ["versions"] = new JsonArray(metadata) }.ToJsonString()).Single();
            var samples = new List<OperationProgress>(); using var operation = new PimOperation(samples.Add);
            using var http = new HttpClient(new LatencyHandler(bytes));
            await PackageDownload.FetchAsync(runtime, Path.Combine(scratch, "latency-transfer"), http, operation);
            var transferring = samples.Where(p => p.DownloadedBytes > 0 && p.DownloadedBytes < p.TotalBytes).ToArray();
            Require(transferring.Length > 0 && transferring.All(p => p.Remaining is null), "Connection wait was mistaken for transfer observations");
        });
        await checkAsync("Custom proxy authenticates after a real HTTP challenge", async () =>
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var secret = "sample-proxy-password";
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using var http = new NetworkSettings("Custom", $"http://127.0.0.1:{port}", "test-user").CreateClient(secret);
                var request = http.GetStringAsync("http://example.invalid/test", timeout.Token);
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
                    using var reader = new StreamReader(peer.GetStream(), Encoding.ASCII, leaveOpen: true);
                    var headers = new StringBuilder();
                    while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } line) headers.AppendLine(line);
                    if (attempt == 1) Require(headers.ToString().Contains("Proxy-Authorization: Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("test-user:" + secret)), StringComparison.OrdinalIgnoreCase), "Proxy credentials not sent on challenge");
                    var response = attempt == 0 ? "HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"test\"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n" : "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK";
                    await peer.GetStream().WriteAsync(Encoding.ASCII.GetBytes(response), timeout.Token);
                }
                Require(await request == "OK", "Authenticated transfer failed");
            }
            finally { listener.Stop(); }
        });
    }
    private sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
    private sealed class LatencyHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            await Task.Delay(2100, token);
            var content = new StreamContent(new PacedStream(bytes)); content.Headers.ContentLength = bytes.Length;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        }
    }
    private sealed class PacedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            await Task.Delay(300, token);
            return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 4096)], token);
        }
    }
    private sealed class ResumeHandler(byte[] bytes) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            if (Requests == 1)
            {
                var content = new StreamContent(new InterruptedStream(bytes)); content.Headers.ContentLength = bytes.Length;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
            }
            if (request.Headers.Range?.Ranges.Single().From != 1024) throw new Exception("Incorrect resume offset");
            var tail = new ByteArrayContent(bytes[1024..]); tail.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(1024, bytes.Length - 1, bytes.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = tail });
        }
    }
    private sealed class InterruptedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (Position >= 1024) throw new IOException("Simulated transport interruption");
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 1024 - (int)Position)], token);
        }
    }
}
