using PimGui.Core;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

static class BuildChecks
{
    public static async Task RunAsync(Action<string, Action> check, Func<string, Func<Task>, Task> checkAsync, string scratch)
    {
        static void Require(bool value, string error) { if (!value) throw new Exception(error); }
        static void Reject<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
        static async Task RejectAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
        var root = Path.Combine(scratch, "build-checks"); Directory.CreateDirectory(root);
        check("CPython recipes accept exact releases and reject unsafe or incompatible options", () =>
        {
            new BuildOptions().Validate();
            Reject<ArgumentException>(() => new BuildOptions(Architecture: "ARM64").Validate());
            new BuildOptions(Configuration: "Debug").Validate();
            new BuildOptions(Version: "3.12.4").Validate();
            new BuildOptions(Version: "3.15.0rc2").Validate();
            Reject<ArgumentException>(() => new BuildOptions(Configuration: "Debug", Pgo: true).Validate());
            Reject<ArgumentException>(() => new BuildOptions(IncludeSsl: false).Validate());
            Reject<ArgumentException>(() => new BuildOptions(Version: "3.14.7&calc").Validate());
            Reject<ArgumentException>(() => BuildRecipe.BatchPath(Path.Combine(root, "percent%PATH%")));
            Reject<ArgumentException>(() => BuildRecipe.BatchPath(Path.Combine(root, "amp&echo")));
        });
        check("Source discovery keeps historical micro releases and isolates preview URLs", () =>
        {
            var versions = BuildSources.ParseVersions("<a href=\"3.9.9/\">old</a><a href=\"3.14.7/\">new</a><a href=\"3.14.6/\">previous</a><a href=\"../../x/\">bad</a>");
            Require(versions.SequenceEqual(new[] { "3.14.7", "3.14.6", "3.9.9" }), "Historical releases lost or lexicographic sort");
            Require(BuildSources.Url("3.15.0rc2") == "https://www.python.org/ftp/python/3.15.0/Python-3.15.0rc2.tgz", "Preview source URL");
            var input = new BuildOptions(Version: "3.12.4", OutputParent: root);
            Require(BuildOptions.Preset("Performance", input).Pgo && BuildOptions.Preset("Performance", input).Version == input.Version, "Preset changed source");
            Require(!BuildOptions.Preset("Minimal", input).IncludePip && BuildOptions.Preset("Debug", input).ExecutableName == "python_d.exe", "Preset options");
        });
        check("Local build identities coexist and are rejected by every PIM mutation", () =>
        {
            var store = new BuildStore(Path.Combine(root, "inventory"));
            var a = new BuildRecord(Guid.NewGuid().ToString("N"), new(), BuildState.Ready, DateTimeOffset.UtcNow);
            var b = a with { Id = Guid.NewGuid().ToString("N") };
            b = b with { Options = new BuildOptions(Version: "3.12.4", Configuration: "Debug", OutputParent: Path.Combine(root, "custom output")) };
            var failed = a with { Id = Guid.NewGuid().ToString("N"), State = BuildState.Failed };
            using (store.AcquireLease()) { store.Save(a); store.Save(b); store.Save(failed); }
            var runtimes = store.Merge([]);
            Require(runtimes.Count == 2 && runtimes[0].Id != runtimes[1].Id && runtimes.All(r => r.IsLocalBuild && !r.IsManaged), "Local identities collapsed or failure registered");
            var debug = runtimes.Single(r => r.LocalBuildId == b.Id);
            Require(debug.Version == "3.12.4" && debug.Executable.EndsWith("python_d.exe") && debug.Prefix.StartsWith(b.Options.OutputParent) && debug.BuildConfiguration?.Configuration == "Debug", "Runtime ignored recorded version, debug executable or custom output");
            foreach (var action in Enum.GetValues<RuntimeAction>()) Reject<InvalidOperationException>(() => PimClient.BuildArguments(action, runtimes[0] with { IsManaged = true }));
            var sentinel = Path.Combine(store.RuntimeDirectory(a.Id), "user-file.txt"); Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!); File.WriteAllText(sentinel, "keep");
            store.Remove(a.Id);
            Require(store.Runtimes().Count == 1 && File.ReadAllText(sentinel) == "keep", "Removal deleted runtime files");
            Reject<IOException>(() => store.JobDirectory("../escape"));
        });
        check("PGO command preserves training dependencies while keeping the selected runtime components", () =>
        {
            var fakeMsBuild = Path.Combine(root, "MSBuild.exe"); File.WriteAllText(fakeMsBuild, "fixture");
            var tools = new BuildToolchain(root, fakeMsBuild, "v145", "14.51.36231", "10.0.26100.0", Environment.ProcessPath!);
            var reduced = new BuildOptions(Pgo: true, IncludeCtypes: false);
            Require(BuildRecipe.CompilerArguments(reduced, tools).Contains("-c PGInstrument") && !BuildRecipe.CompilerArguments(reduced, tools).Contains("--no-ctypes"), "Training lost ctypes");
            Require(BuildRecipe.CompilerArguments(reduced, tools, "PGUpdate").Contains("-c PGUpdate") && !reduced.IncludeCtypes, "Optimized pass changed runtime choice");
            Require(BuildRecipe.CompilerArguments(reduced with { Pgo = false }, tools).Contains("--no-ctypes"), "Non-PGO build ignored component selection");
            Require(BuildRecipe.TrainingArguments("C:\\source with space").Contains("test_tabnanny") && !BuildRecipe.TrainingArguments("C:\\source").Contains("test_tabnanny"), "PGO path workaround applied outside its condition");
        });
        check("Build recovery respects the active lease and only marks unfinished records", () =>
        {
            var store = new BuildStore(Path.Combine(root, "recovery"));
            var a = new BuildRecord(Guid.NewGuid().ToString("N"), new(), BuildState.Compiling, DateTimeOffset.UtcNow);
            using (store.AcquireLease())
            {
                store.Save(a); store.RecoverInterrupted();
                Require(store.Read().Single().State == BuildState.Compiling, "Running job was interrupted");
                Reject<IOException>(() => store.AcquireLease());
            }
            store.RecoverInterrupted();
            Require(store.Read().Single().State == BuildState.Interrupted && store.Runtimes().Count == 0, "Interrupted job became ready");
        });
        check("Environment refresh preserves base runtime provenance", () =>
        {
            var environments = new VirtualEnvironments(Path.Combine(root, "venvs"));
            var path = Path.Combine(root, "project", ".venv");
            var environment = new VirtualEnvironment(path, "Environment ready", "3.14.7", root) { BaseRuntimeId = "local-build-test" };
            environments.Remember(environment);
            environments.Remember(environment with { BaseRuntimeId = null });
            Require(environments.Read().Single().BaseRuntimeId == environment.BaseRuntimeId, "Base ID lost");
            environments.Remember(environment with { BasePath = root + "-other", BaseRuntimeId = null });
            Require(environments.Read().Single().BaseRuntimeId is null, "Stale base ID retained after retargeting");
        });
        string Archive(string name, params (string Name, TarEntryType Type, string Content)[] entries)
        {
            var path = Path.Combine(root, name + ".tgz");
            using var file = File.Create(path); using var gzip = new GZipStream(file, CompressionLevel.Fastest); using var writer = new TarWriter(gzip);
            foreach (var entry in entries)
            {
                var item = new PaxTarEntry(entry.Type, entry.Name);
                item.ModificationTime = new DateTimeOffset(2026, 8, 5, 11, 21, 0, TimeSpan.Zero);
                if (entry.Type == TarEntryType.SymbolicLink) item.LinkName = "../../outside";
                else if (entry.Type == TarEntryType.RegularFile) item.DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(entry.Content));
                writer.WriteEntry(item); item.DataStream?.Dispose();
            }
            return path;
        }
        const string prefix = "Python-3.14.7/";
        await checkAsync("Source integrity and safe archive extraction", async () =>
        {
            var archive = Archive("valid", (prefix + "PCbuild/build.bat", TarEntryType.RegularFile, "fixture"));
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive)));
            await BuildArchive.VerifyAsync(archive, hash, default);
            await RejectAsync<IOException>(() => BuildArchive.VerifyAsync(archive, new string('0', 64), default));
            await BuildArchive.ExtractAsync(archive, Path.Combine(root, "unpacked"), default);
            Require(File.GetLastWriteTimeUtc(Path.Combine(root, "unpacked", prefix, "PCbuild", "build.bat")) == new DateTime(2026, 8, 5, 11, 21, 0, DateTimeKind.Utc), "Release timestamps were lost and would trigger source regeneration");
            await RejectAsync<IOException>(() => BuildArchive.ExtractAsync(archive, Path.Combine(root, "unpacked"), default));
            foreach (var (name, path, type) in new[] {
                ("traversal", prefix + "../../escape.txt", TarEntryType.RegularFile),
                ("ads", prefix + "file:stream", TarEntryType.RegularFile),
                ("device", prefix + "NUL.txt", TarEntryType.RegularFile),
                ("link", prefix + "link", TarEntryType.SymbolicLink) })
            {
                var bad = Archive(name, (path, type, "x"));
                await RejectAsync<IOException>(() => BuildArchive.ExtractAsync(bad, Path.Combine(root, "out-" + name), default));
            }
            var duplicate = Archive("duplicate", (prefix + "A.txt", TarEntryType.RegularFile, "a"), (prefix + "a.txt", TarEntryType.RegularFile, "b"));
            await RejectAsync<IOException>(() => BuildArchive.ExtractAsync(duplicate, Path.Combine(root, "out-duplicate"), default));
            await RejectAsync<IOException>(() => BuildArchive.ExtractAsync(archive, Path.Combine(root, "out-limit"), default, maximumBytes: 2));
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await RejectAsync<OperationCanceledException>(() => BuildArchive.ExtractAsync(archive, Path.Combine(root, "out-cancel"), cancelled.Token));
        });
        check("Build layout includes independent venv and development files without PIM registration", () =>
        {
            var args = BuildRecipe.LayoutArguments(new(false, false, false), root, root + "-output", root + "-temp");
            Require(args.Contains("--include-venv") && args.Contains("--include-dev") && args.Contains("--include-alias") &&
                !args.Contains("--include-tcltk") && !args.Contains("--include-install-json") && !args.Contains("--include-symbols"), "Incorrect layout capabilities");
            args = BuildRecipe.LayoutArguments(new(true, true, true), root, root + "-output", root + "-temp");
            Require(args.Contains("--include-tcltk") && args.Contains("--include-tests") && args.Contains("--include-symbols"), "Optional components lost");
        });
        await checkAsync("Build cancellation closes its Windows process group without leaving a child", async () =>
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var child = 0;
            await RejectAsync<OperationCanceledException>(() => BuildProcess.RunAsync(Environment.ProcessPath!, ["--cancellation-parent"], null, root,
                new Dictionary<string, string?>(), line =>
                {
                    if (!line.StartsWith("CHILD:")) return;
                    child = int.Parse(line[6..]); cancellation.Cancel();
                }, cancellation.Token));
            Require(child > 0, "Child never started");
            try { using var process = System.Diagnostics.Process.GetProcessById(child); Require(process.HasExited, "Child survived cancellation"); }
            catch (ArgumentException) { }
        });
        await checkAsync("Build log observer failure still drains pipes and reports a bounded failure", async () =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await RejectAsync<IOException>(() => BuildProcess.RunAsync(Environment.ProcessPath!, ["--emit-long-output"], null, root,
                new Dictionary<string, string?>(), _ => throw new IOException("Disk full fixture"), timeout.Token));
        });
    }
}
