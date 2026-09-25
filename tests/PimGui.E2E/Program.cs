using PimGui.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

// Destructive lifecycle acceptance MUST run in a disposable Windows Sandbox/VM.
if (args.Length != 3 || !File.Exists(@"C:\PyDeckE2E\ISOLATED") || Environment.MachineName != File.ReadAllText(@"C:\PyDeckE2E\ISOLATED").Trim())
    throw new InvalidOperationException("Run scripts/Test-PimLifecycle.ps1 in a disposable sandbox");
var manager = args[0]; var phase = args[1]; var output = args[2];
if (phase is not "seed" and not "upgraded" and not "t3" and not "history")
    throw new ArgumentException("Unknown lifecycle phase");
var root = @"C:\PyDeckE2E";
var results = new List<object>();
var failed = false;
void Require(bool value, string message) { if (!value) throw new IOException(message); }
async Task Check(string name, Func<Task> action)
{
    try { await action(); results.Add(new { name, status = "passed" }); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed = true; results.Add(new { name, status = "failed", error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); throw; }
    finally { File.WriteAllText(output, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); }
}
var scope = new ScopedRunner(root);
var client = new PimClient(scope, Path.Combine(root, "operation.lock")) { Network = () => new NetworkSettings("Direct") };
try
{
    await Check("Discover actual PIM " + phase, async () => Require(await client.DiscoverAsync(manager), client.LastDiscoveryError));
    if (phase == "seed")
    {
        await Check("Old PIM installation from an official bundled Python fixture", async () =>
        {
            var bundle = OfflineBundle.Load(@"C:\PyDeckResults\seed-fixture");
            var runtime = bundle.Runtimes.Single();
            File.WriteAllText(Path.Combine(root, "seed-version.txt"), runtime.Version);
            using var operation = new PimOperation();
            Require(!client.SupportsMutations, "Legacy manager unexpectedly enabled mutations");
            try { await client.InstallOfflineAsync(bundle, runtime, Console.WriteLine, operation: operation); throw new IOException("Old manager mutation was not blocked"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("26.3")) { }
            // Prepare the real pre-upgrade state with old PIM's supported selector command.
            using var prepared = await bundle.PrepareAsync(runtime);
            var seeded = await scope.RunAsync(manager, ["install", "--yes", "--source=" + prepared.IndexPath, "--config=" + prepared.ConfigPath, runtime.Selector], Console.WriteLine);
            Require(seeded.ExitCode == 0, seeded.Error + seeded.Output);
            var verified = await RuntimeHealth.VerifyAsync(client, RuntimeAction.Install, runtime);
            Require(verified.Confirmed, verified.Message);
            var selected = verified.Runtimes.Single();
            PimConfiguration.Save(PimConfiguration.Read(scope.UserConfig, false), new JsonObject { ["default_tag"] = selected.Selector, ["automatic_install"] = false }, false);
            Require((await client.ListAsync()).Single().IsDefault, "Old manager default failed");
        });
    }
    else if (phase == "history")
    {
        PythonRuntime latest = null!, previous = null!, current = null!;
        var evidence = new List<object>();
        async Task<PythonRuntime> VerifyHistoricalAsync(PythonRuntime expected, string step)
        {
            var verified = await RuntimeHealth.VerifyAsync(client, RuntimeAction.Install, expected);
            Require(verified.Confirmed, verified.Message);
            var actual = verified.Runtimes.Single();
            Require(actual.SameIdentity(expected), "Registered historical identity differs from the selection");
            Require(actual.Prefix.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase), "Unsafe historical fixture location");
            var executed = await new ProcessRunner().RunAsync(actual.Executable,
                ["-I", "-S", "-c", "import sys,json;print(json.dumps({'version':sys.version.split()[0],'executable':sys.executable}))"]);
            Require(executed.ExitCode == 0 && !executed.OutputTruncated, "Historical interpreter execution failed");
            var interpreter = JsonNode.Parse(executed.Output)!;
            Require(interpreter["version"]?.GetValue<string>() == expected.Version, "Executed Python micro differs from the selected micro");
            Require(string.Equals(interpreter["executable"]?.GetValue<string>(), actual.Executable, StringComparison.OrdinalIgnoreCase), "Historical executable path differs");
            var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(actual.Prefix, "__install__.json")))!;
            Require(metadata["sort-version"]?.GetValue<string>() == expected.Version, "Installed metadata contains a different micro");
            Require(metadata["source"]?.GetValue<string>() == InstallationSource.Official && InstallationSource.Installed(actual) == InstallationSource.Official,
                "Historical installation lost its official source or retained a temporary offline source");
            evidence.Add(new { step, expected.Id, expected.Version, actual.Executable, source = metadata["source"]!.GetValue<string>() });
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output)!, "history-evidence.json"),
                JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            return actual;
        }
        await Check("Official history exposes latest and previous stable micro with the same runtime ID", async () =>
        {
            Require(client.SupportsMutations && client.SupportsRepair, "History tests require PIM 26.3 or newer");
            Require((await client.ListAsync()).Count == 0, "History-only phase requires an empty isolated Python installation directory");
            var catalog = await client.ListCatalogAsync();
            var series = catalog.Where(r => r.Company == "PythonCore" && r.Architecture == "x64" && !r.IsSpecialized && !r.IsPrerelease)
                .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(r => r.Version, Comparer<string>.Create(RuntimeCatalog.CompareVersions)).ToArray())
                .Where(group => group.Length >= 2)
                .OrderByDescending(group => group[0].Version, Comparer<string>.Create(RuntimeCatalog.CompareVersions)).FirstOrDefault();
            Require(series is not null, "Official history does not contain a stable same-ID micro pair");
            latest = series![0]; previous = series[1];
            Require(latest.Id == previous.Id && RuntimeCatalog.MinorSeries(latest) == RuntimeCatalog.MinorSeries(previous) &&
                RuntimeCatalog.CompareVersions(latest.Version, previous.Version) > 0, "Invalid historical micro pair");
            Require(latest.CatalogIndex == InstallationSource.Official && previous.CatalogIndex == InstallationSource.Official,
                "History selection did not retain its source snapshot");
            Console.WriteLine($"History selection: {latest.Id}, {latest.Version} -> {previous.Version}, {previous.ExactSelector}");
        });
        await Check("Install the actual latest stable micro from the official source", async () =>
        {
            using var operation = new PimOperation();
            await client.ChangeAsync(RuntimeAction.Install, latest, Console.WriteLine, operation);
            current = await VerifyHistoricalAsync(latest, "latest installed");
        });
        await Check("Historical replacement requires explicit confirmation", async () =>
        {
            var rejected = false;
            try { await client.ChangeAsync(RuntimeAction.Install, previous, Console.WriteLine); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("confirm the replacement", StringComparison.OrdinalIgnoreCase)) { rejected = true; }
            Require(rejected, "Unconfirmed historical replacement was not rejected");
            current = await VerifyHistoricalAsync(latest, "unconfirmed replacement rejected");
        });
        await Check("Confirmed downgrade executes the exact historical micro and retains its official source", async () =>
        {
            using var operation = new PimOperation();
            await client.ChangeAsync(RuntimeAction.Install, previous, Console.WriteLine, operation, expectedInstalledVersion: current.Version);
            current = await VerifyHistoricalAsync(previous, "previous micro installed");
        });
        await Check("Stale historical replacement confirmation is rejected without changing the installed interpreter", async () =>
        {
            var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(current.Executable));
            var rejected = false;
            try { await client.ChangeAsync(RuntimeAction.Install, latest, Console.WriteLine, expectedInstalledVersion: latest.Version); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("changed after confirmation", StringComparison.OrdinalIgnoreCase)) { rejected = true; }
            Require(rejected, "Stale historical confirmation was not rejected");
            Require(before.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(current.Executable))), "Stale confirmation changed the interpreter file");
            current = await VerifyHistoricalAsync(previous, "stale confirmation rejected");
        });
        await Check("Repair restores the historical interpreter without upgrading its micro", async () =>
        {
            Require(current.Prefix.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase), "Unsafe historical damage fixture location");
            File.Move(current.Executable, current.Executable + ".damaged");
            Require(!(await RuntimeHealth.CheckAsync(current)).Healthy, "Historical damage was not detected");
            using var operation = new PimOperation();
            await client.ChangeAsync(RuntimeAction.Repair, current, Console.WriteLine, operation);
            current = await VerifyHistoricalAsync(previous, "historical micro repaired");
        });
        await Check("Historical uninstall removes its registration, executable and installation directory", async () =>
        {
            await client.ChangeAsync(RuntimeAction.Uninstall, current, Console.WriteLine);
            var verified = await RuntimeHealth.VerifyAsync(client, RuntimeAction.Uninstall, current);
            Require(verified.Confirmed && verified.Runtimes.Count == 0, verified.Message);
        });
    }
    else
    {
        string? offline = null;
        PythonRuntime current;
        if (phase == "t3")
        {
            offline = @"C:\PyDeckResults\seed-fixture";
            var seed = OfflineBundle.Load(offline);
            using var operation = new PimOperation();
            await client.InstallOfflineAsync(seed, seed.Runtimes.Single(), Console.WriteLine, operation: operation);
            current = (await client.ListAsync()).Single();
        }
        else
        {
        await Check("Windows Credential Manager round-trip without JSON secrets", () =>
        {
            var password = Guid.NewGuid().ToString("N");
            ProxyCredential.Save("http://127.0.0.1:7890", "e2e", password);
            Require(ProxyCredential.Load("http://127.0.0.1:7890", "e2e") == password, "Credential persistence failed");
            Require(ProxyCredential.Load("http://127.0.0.1:7891", "e2e") is null, "Credential used for another proxy");
            Require(!JsonSerializer.Serialize(new AppSettings { Network = new("Custom", "http://127.0.0.1:7890", "e2e") }).Contains(password), "Password serialized");
            ProxyCredential.Save("http://127.0.0.1:7890", "e2e", "");
            return Task.CompletedTask;
        });
        var old = (await client.ListAsync()).Single();
        await Check("PIM upgrade preserves runtimes and configuration", async () =>
        {
            Require(old.Version == File.ReadAllText(Path.Combine(root, "seed-version.txt")) && old.IsDefault, "Upgrade lost state");
            Require((await RuntimeHealth.CheckAsync(old)).Healthy, "Old interpreter stopped working");
            Require(PimConfiguration.Read(scope.UserConfig, false).Values["automatic_install"]!.GetValue<bool>() == false, "Configuration lost");
        });
        await Check("Real Python patch update and post-operation verification", async () =>
        {
            using var operation = new PimOperation();
            await client.ChangeAsync(RuntimeAction.Update, old, Console.WriteLine, operation);
            var verified = await RuntimeHealth.VerifyAsync(client, RuntimeAction.Update, client.LastExpectedRuntime!);
            Require(verified.Confirmed, verified.Message);
            Require(verified.Runtimes.Single().Version != old.Version, "This was not a real version upgrade");
            var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(verified.Runtimes.Single().Prefix, "__install__.json")))!;
            Require(metadata["source"]?.GetValue<string>()?.StartsWith("https://") == true, "Temporary source persisted in installed metadata");
        });
        current = (await client.ListAsync()).Single();
        await Check("Damage detection and exact-version PIM repair", async () =>
        {
            Require(current.Prefix.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase), "Unsafe damage fixture location");
            File.Move(current.Executable, current.Executable + ".damaged");
            Require(!(await RuntimeHealth.CheckAsync(current)).Healthy, "Missing executable was not detected");
            using var operation = new PimOperation();
            await client.ChangeAsync(RuntimeAction.Repair, current, Console.WriteLine, operation);
            var verified = await RuntimeHealth.VerifyAsync(client, RuntimeAction.Repair, current);
            Require(verified.Confirmed, verified.Message);
        });
        await Check("Cancel real download and reconcile without changing Python", async () =>
        {
            var before = await client.ListAsync();
            PimOperation? operation = null;
            operation = new PimOperation(p => { if (p.DownloadedBytes > 0) operation!.Cancel(); });
            using (operation)
            {
                try { await client.DownloadOfflineAsync(current, root, Console.WriteLine, operation); throw new IOException("Cancellation was not observed"); }
                catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
            }
            Require(before.SequenceEqual(await client.ListAsync()), "Cancellation changed installed runtimes");
        });
        await Check("Real download bytes, speed and reusable offline bundle", async () =>
        {
            var events = new List<OperationProgress>();
            using var operation = new PimOperation(p => events.Add(p));
            offline = await client.DownloadOfflineAsync(current, root, Console.WriteLine, operation);
            Require(events.Any(p => p.DownloadedBytes > 0) && events.Any(p => p.BytesPerSecond > 0), "Measured transfer data missing");
            Require(OfflineBundle.Load(offline).Runtimes.Single().Version == current.Version, "Wrong bundle");
        });
        await Check("Install second runtime and switch actual default", async () =>
        {
            var candidate = (await client.ListAsync(true)).First(r => r.Company == "PythonCore" && r.Architecture == "x64" && !r.IsSpecialized && r.Tag.StartsWith("3.13-") && !r.IsPrerelease);
            using var operation = new PimOperation();
            await client.ChangeAsync(RuntimeAction.Install, candidate, Console.WriteLine, operation);
            Require((await RuntimeHealth.VerifyAsync(client, RuntimeAction.Install, candidate)).Confirmed, "Second install failed");
            PimConfiguration.Save(PimConfiguration.Read(scope.UserConfig, false), new JsonObject { ["default_tag"] = candidate.Selector }, false);
            var active = (await client.ListAsync()).Single(r => r.IsDefault);
            Require(active.Id == candidate.Id, "Default list mismatch");
            var executed = await scope.RunAsync(manager, ["exec", "-I", "-S", "-c", "import sys;print(sys.executable)"]);
            Require(executed.ExitCode == 0 && executed.Output.Trim().Equals(active.Executable, StringComparison.OrdinalIgnoreCase), "Default execution mismatch");
            await client.ChangeAsync(RuntimeAction.Uninstall, active, Console.WriteLine);
            Require((await RuntimeHealth.VerifyAsync(client, RuntimeAction.Uninstall, active)).Confirmed, "Second uninstall failed");
        });
        }
        await Check("Custom HTTPS source install, retained origin repair and source switching", async () =>
        {
            await using var fixture = new HttpsFixture(offline!);
            using (var http = new HttpClient()) Console.WriteLine("HTTPS fixture reachable: " + (await http.GetStringAsync(fixture.Index)).Length);
            var selected = fixture.Index;
            var custom = new PimClient(scope, Path.Combine(root, "operation.lock")) { Network = () => new NetworkSettings("Direct"), SelectedSource = () => selected };
            Require(await custom.DiscoverAsync(manager), custom.LastDiscoveryError);
            var candidate = (await custom.ListAsync(true)).Single();
            Require(candidate.CatalogIndex == fixture.Index, "Source snapshot missing");
            using var op = new PimOperation();
            await custom.ChangeAsync(RuntimeAction.Install, candidate, Console.WriteLine, op);
            var installedCustom = (await custom.ListAsync()).Single(r => r.Id == candidate.Id);
            Require((await RuntimeHealth.CheckAsync(installedCustom)).Healthy && InstallationSource.Installed(installedCustom) == fixture.Index, "Custom install source or health");
            selected = InstallationSource.Official;
            await custom.ChangeAsync(RuntimeAction.Repair, installedCustom, Console.WriteLine, op);
            Require(InstallationSource.Installed(installedCustom) == fixture.Index, "Repair silently changed source");
            await custom.ChangeAsync(RuntimeAction.Update, installedCustom, Console.WriteLine, op);
            Require((await custom.ListAsync()).Single(r => r.Id == candidate.Id).Version == installedCustom.Version, "Unexpected update");
            await custom.ChangeAsync(RuntimeAction.Uninstall, installedCustom, Console.WriteLine);
            Require((await RuntimeHealth.VerifyAsync(custom, RuntimeAction.Uninstall, installedCustom)).Confirmed, "Custom cleanup");
        });
        await Check("Real PIM Shebang mapping and virtual environment lifecycle", async () =>
        {
            var sample = Path.Combine(root, "controlled-shebang.py");
            File.WriteAllText(sample, "#!/usr/bin/pydeck_e2e\nimport sys;print(sys.executable)\n");
            PimConfiguration.Save(PimConfiguration.Read(scope.UserConfig, false), new JsonObject {
                ["shebang_templates"] = new JsonObject { ["/usr/bin/pydeck_e2e"] = "py -V:" + current.Selector },
                ["shebang_can_run_anything"] = false }, false);
            var result = await scope.RunAsync(manager, ["exec", sample]);
            Require(result.ExitCode == 0 && result.Output.Trim().Equals(current.Executable, StringComparison.OrdinalIgnoreCase), "Shebang mapping failed");
            var environments = new VirtualEnvironments(Path.Combine(root, "environments")); using var op = new PimOperation();
            var env = await environments.CreateAsync(current, root, "venv 中文", new ProcessRunner(), op);
            Require(env.State == "Environment ready", env.State);
            environments.Remember(env, true); Require(File.Exists(env.Executable), "Forget deleted files");
        });
        await Check("Uninstall and reinstall exclusively from offline files", async () =>
        {
            current = (await client.ListAsync()).Single();
            await client.ChangeAsync(RuntimeAction.Uninstall, current, Console.WriteLine);
            Require((await RuntimeHealth.VerifyAsync(client, RuntimeAction.Uninstall, current)).Confirmed, "Uninstall failed");
            var bundle = OfflineBundle.Load(offline!);
            scope.BlockNetwork = true;
            using var operation = new PimOperation();
            await client.InstallOfflineAsync(bundle, bundle.Runtimes.Single(), Console.WriteLine, operation: operation);
            Require((await RuntimeHealth.VerifyAsync(client, RuntimeAction.Install, current)).Confirmed, "Offline reinstall failed");
            scope.BlockNetwork = false;
        });
        await Check("PIM alias refresh and final uninstall", async () =>
        {
            await client.RefreshRegistrationsAsync(Console.WriteLine);
            Require(Directory.EnumerateFiles(Path.Combine(root, "aliases"), "*.exe").Any(), "No PIM aliases generated");
            current = (await client.ListAsync()).Single();
            await client.ChangeAsync(RuntimeAction.Uninstall, current, Console.WriteLine);
            Require((await RuntimeHealth.VerifyAsync(client, RuntimeAction.Uninstall, current)).Confirmed, "Final cleanup not confirmed");
        });
    }
}
catch { failed = true; }
return failed ? 1 : 0;

sealed class ScopedRunner(string root) : IProcessRunner
{
    public string UserConfig => Path.Combine(root, "user.json");
    public bool BlockNetwork { get; set; }
    public async Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, Action<string>? output = null, CancellationToken cancellationToken = default, Action<ProcessOutput>? observe = null)
    {
        var effective = PimConfiguration.Read(UserConfig, false).Values;
        effective["install_dir"] = Path.Combine(root, "installs");
        effective["global_dir"] = Path.Combine(root, "aliases");
        effective["download_dir"] = Path.Combine(root, "cache");
        effective["include_unmanaged"] = false;
        effective["pep514_root"] = @"HKEY_CURRENT_USER\Software\PyDeckE2E\Python";
        effective["start_folder"] = "PyDeckE2E";
        var args = arguments.Where(a => !a.StartsWith("--config=")).ToList();
        var additional = arguments.FirstOrDefault(a => a.StartsWith("--config="));
        if (additional is not null)
            foreach (var entry in JsonNode.Parse(File.ReadAllText(additional[9..]))!.AsObject()) effective[entry.Key] = entry.Value?.DeepClone();
        var path = Path.Combine(root, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, effective.ToJsonString());
        // exec forwards its tail to Python. Launcher configuration must be in the
        // environment, not appended after -c as if it were an interpreter argument.
        var previousConfig = Environment.GetEnvironmentVariable("PYTHON_MANAGER_CONFIG");
        if (args[0] == "exec") Environment.SetEnvironmentVariable("PYTHON_MANAGER_CONFIG", path);
        else args.Add("--config=" + path);
        try
        {
            var network = BlockNetwork ? new NetworkSettings("Custom", "http://127.0.0.1:1") : new NetworkSettings("Direct");
            var result = await new ProcessRunner(() => network).RunAsync(executable, args, output, cancellationToken, observe);
            if (result.ExitCode != 0) Console.WriteLine("PIM failure: " + result.ExitCode + " " + result.Output + " " + result.Error);
            return result;
        }
        finally { Environment.SetEnvironmentVariable("PYTHON_MANAGER_CONFIG", previousConfig); File.Delete(path); }
    }
}
