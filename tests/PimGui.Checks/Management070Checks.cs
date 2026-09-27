using PimGui.Core;
using System.IO.Compression;

static class Management070Checks
{
    public static void Run(Action<string, Action> check, string scratch)
    {
        static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        static void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is ArgumentException or IOException) { return; } throw new Exception("Unsafe operation accepted"); }
        check("Package inputs reject pip option injection and indirect requirements", () =>
        {
            foreach (var text in new[] { "--target=C:/other", "-r other.txt", "https://evil/package.whl", "package @ https://evil", "../local", "a; calc", "a\n--user", "a\r--root" })
                Reject(() => EnvironmentPackages.ParseRequirements(text));
            Require(EnvironmentPackages.ParseRequirements("# comment\nrequests>=2.32,<3\nnumpy==2.3.3\nfoo[bar,baz]").Length == 3, "Valid requirements rejected");
            var args = EnvironmentPackages.Arguments(PackageAction.Install, ["requests"]);
            Require(args.Contains("--require-virtualenv") && args.Contains("https://pypi.org/simple") && !args.Contains("--user"), "pip destination unsafe");
            Reject(() => EnvironmentPackages.Arguments(PackageAction.Uninstall, ["pip"]));
        });
        check("Runtime usage retains missing registered environments and matches live base paths", () =>
        {
            var root = Path.Combine(scratch, "usage"); Directory.CreateDirectory(root);
            var runtime = new PythonRuntime("build-1", "CPython", "3.14-64", "3.14.7", "Python", Path.Combine(root, "python.exe"), root, false);
            var missing = new VirtualEnvironment(Path.Combine(root, "gone")) { BaseRuntimeId = runtime.Id };
            var pathBased = new VirtualEnvironment(Path.Combine(root, "env"), BasePath: root.ToUpperInvariant() + "\\");
            Require(RuntimeUsage.Find(runtime, [missing, pathBased]).Count == 2, "Usage dependencies disappeared");
        });
        check("Build cleanup rejects stale snapshots, used runtimes and foreign directories", () =>
        {
            var root = Path.Combine(scratch, "storage"); var builds = new BuildStore(root); var storage = new BuildStorage(root);
            var record = new BuildRecord(Guid.NewGuid().ToString("N"), new(), BuildState.Ready, DateTimeOffset.UtcNow);
            builds.Save(record); var runtime = builds.RuntimeDirectory(record); Directory.CreateDirectory(runtime);
            File.WriteAllText(Path.Combine(runtime, ".pydeck-build-id"), record.Id); File.WriteAllText(Path.Combine(runtime, "python.exe"), "fixture");
            var cache = Path.Combine(builds.Root, "Cache"); Directory.CreateDirectory(cache); var file = Path.Combine(cache, "source.tgz"); File.WriteAllText(file, "old");
            var entry = storage.Scan().Single(e => e.Kind == "Source downloads"); File.WriteAllText(file, "changed");
            Reject(() => storage.Clean(entry)); Require(File.Exists(file), "Stale cleanup deleted data");
            storage.Clean(storage.Scan().Single(e => e.Kind == "Source downloads")); Require(!Directory.Exists(cache), "Cleanup did not remove cache");
            Require(File.Exists(Path.Combine(runtime, "python.exe")) && File.Exists(Path.Combine(builds.JobDirectory(record.Id), "build.json")), "Cleanup crossed category");
            var environments = new VirtualEnvironments(root);
            var beforeUsage = storage.Scan().Single(e => e.Kind == "Built runtime");
            var env = new VirtualEnvironment(Path.Combine(root, "project"), BasePath: runtime); environments.Remember(env);
            Reject(() => storage.Clean(beforeUsage)); // An environment created after review must still block deletion.
            entry = storage.Scan().Single(e => e.Kind == "Built runtime"); Require(entry.Users == 1, "Usage not counted"); Reject(() => storage.Clean(entry));
            environments.Remember(env, remove: true); storage.Clean(storage.Scan().Single(e => e.Kind == "Built runtime"));
            Require(!Directory.Exists(runtime) && builds.Read().Single().State == BuildState.Removed, "Runtime inventory stale after cleanup");
            Reject(() => storage.Clean(new("Source downloads", scratch, 0, "")));
        });
        check("Bootstrap ZIP extraction rejects traversal before writing outside staging", () =>
        {
            var root = Path.Combine(scratch, "bootstrap-archive"); Directory.CreateDirectory(root);
            var archive = Path.Combine(root, "bad.zip");
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create)) using (var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open())) writer.Write("escape");
            Reject(() => BuildDependencies.Extract(archive, Path.Combine(root, "stage"), default));
            Require(!File.Exists(Path.Combine(root, "escape.txt")), "Bootstrap archive escaped staging");
        });
        check("Bootstrap interrupted downloads resume at the received byte offset", () =>
        {
            using var handler = new ResumeHandler(); using var client = new HttpClient(handler); using var operation = new PimOperation();
            var file = Path.Combine(scratch, "bootstrap-resume.partial");
            BuildDependencies.DownloadAsync(client, file, operation, default).GetAwaiter().GetResult();
            Require(File.ReadAllText(file) == "abcd" && handler.Calls == 2, "Bootstrap resume lost or duplicated bytes");
        });
    }
    private sealed class ResumeHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (Calls == 1)
            {
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent("ab"u8.ToArray()) };
                response.Content.Headers.ContentLength = 4; return Task.FromResult(response);
            }
            if (request.Headers.Range?.Ranges.Single().From != 2) throw new Exception("Wrong resume offset");
            var resumed = new HttpResponseMessage(System.Net.HttpStatusCode.PartialContent) { Content = new ByteArrayContent("cd"u8.ToArray()) };
            resumed.Content.Headers.ContentRange = new(2, 3, 4); return Task.FromResult(resumed);
        }
    }
    public static async Task<int> LiveAsync(string scratch, string python, string seed)
    {
        Directory.CreateDirectory(scratch);
        var tools = new BuildDependencies(scratch);
        var cache = Path.Combine(new BuildStore(scratch).Root, "Cache"); Directory.CreateDirectory(cache);
        if (seed != "-") File.Copy(seed, Path.Combine(cache, "python-" + BuildDependencies.Version + ".nupkg"), false);
        using var operation = new PimOperation(_ => { });
        var bootstrap = await tools.PrepareAsync(new(), null, operation);
        var detected = await BuildToolchain.DetectAsync(bootstrap);
        Console.WriteLine("PASS private bootstrap integrity, execution and toolchain discovery: " + detected.PlatformToolset);
        var envPath = Path.Combine(scratch, "test venv");
        var result = await new ProcessRunner().RunAsync(python, ["-I", "-m", "venv", "--without-pip", envPath]);
        if (result.ExitCode != 0) throw new IOException(result.Error);
        var packages = new EnvironmentPackages(scratch, new());
        var injectedTarget = Path.Combine(scratch, "outside-target");
        Environment.SetEnvironmentVariable("PIP_TARGET", injectedTarget);
        if ((await packages.ListAsync(envPath)).HasPip) throw new Exception("Expected no pip");
        var state = await packages.ChangeAsync(envPath, PackageAction.PreparePip, [], Console.WriteLine);
        if (!state.HasPip) throw new Exception("ensurepip failed");
        state = await packages.ChangeAsync(envPath, PackageAction.Install, ["colorama==0.4.6"], Console.WriteLine);
        if (!state.Packages.Any(p => p.Name == "colorama" && p.Version == "0.4.6") || state.DependencyIssue.Length > 0) throw new Exception("Package install verification failed");
        var text = EnvironmentPackages.Export(state); if (!EnvironmentPackages.ParseRequirements(text).Contains("colorama==0.4.6")) throw new Exception("Export/import mismatch");
        state = await packages.ChangeAsync(envPath, PackageAction.Uninstall, ["colorama"], Console.WriteLine);
        if (state.Packages.Any(p => p.Name == "colorama")) throw new Exception("Uninstall verification failed");
        if (Directory.Exists(injectedTarget)) throw new Exception("Inherited PIP_TARGET redirected the installation");
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        try { await packages.ChangeAsync(envPath, PackageAction.Install, ["colorama"], Console.WriteLine, stopped.Token); throw new Exception("Cancelled pip operation ran"); }
        catch (OperationCanceledException) { }
        if ((await packages.ListAsync(envPath)).Packages.Any(p => p.Name == "colorama")) throw new Exception("Cancelled operation changed packages");
        Environment.SetEnvironmentVariable("PIP_TARGET", null);
        Console.WriteLine("PASS real isolated venv: prepare pip, install, check, requirements round-trip and uninstall");
        return 0;
    }
}
