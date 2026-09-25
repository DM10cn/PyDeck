namespace PimGui.Core;

public enum RuntimeAction { Install, Update, Uninstall, Repair }

public sealed partial class PimClient(IProcessRunner runner, string? mutationLockPath = null)
{
    private readonly SemaphoreSlim mutation = new(1, 1);
    public string? Executable { get; private set; }
    public string LastDiscoveryError { get; private set; } = "";
    public string ManagerVersion { get; private set; } = "Unknown";
    public bool SupportsRepair { get; private set; }
    public bool SupportsMutations => Version.TryParse(ManagerVersion, out var version) && version >= new Version(26, 3);
    public Func<NetworkSettings>? Network { get; set; }
    public Func<string?>? ProxyPassword { get; set; }
    public Func<string>? SelectedSource { get; set; }
    public Func<Uri, Task<bool>>? ConfirmDownloadOrigin { get; set; }
    public PythonRuntime? LastExpectedRuntime { get; private set; }

    public async Task<bool> DiscoverAsync(string? preferredPath = null)
    {
        Executable = null;
        SupportsRepair = false; ManagerVersion = "Unknown";
        LastDiscoveryError = "";
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferredPath)) candidates.Add(preferredPath.Trim().Trim('"'));
        else
        {
            var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
            candidates.Add(Path.Combine(apps, "pymanager.exe"));
            candidates.Add(Path.Combine(apps, "PythonSoftwareFoundation.PythonManager_3847v3x7pw1km", "pymanager.exe"));
            candidates.Add(Path.Combine(apps, "PythonSoftwareFoundation.PythonManager_qbz5n2kfra8p0", "pymanager.exe"));
            // A manager installed while PyDeck is open may have updated the registry PATH.
            // Read it again on reconnect instead of requiring a restart of this process.
            var pathValues = new List<string?> { Environment.GetEnvironmentVariable("PATH") };
            if (OperatingSystem.IsWindows())
            {
                pathValues.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User));
                pathValues.Add(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine));
            }
            var paths = string.Join(Path.PathSeparator, pathValues).Split(Path.PathSeparator);
            foreach (var name in new[] { "pymanager.exe", "py.exe" })
                foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
                    candidates.Add(Path.Combine(path.Trim().Trim('"'), name));
        }
        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fullPath = ExecutionPaths.Manager(candidate);
                if (!File.Exists(fullPath)) continue;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                var help = await runner.RunAsync(fullPath, ["help", "list"], cancellationToken: timeout.Token);
                if (help.OutputTruncated || help.ExitCode != 0 || !help.Output.Contains("--only-managed", StringComparison.Ordinal) ||
                    !help.Output.Contains("--online", StringComparison.Ordinal))
                { LastDiscoveryError = "The selected program is not a supported Python Install Manager."; continue; }
                var installHelp = await runner.RunAsync(fullPath, ["help", "install"], cancellationToken: timeout.Token);
                SupportsRepair = installHelp.ExitCode == 0 && installHelp.Output.Contains("--force");
                var versionMatch = System.Text.RegularExpressions.Regex.Match(installHelp.Output + help.Output, @"Python installation manager ([0-9.]+)");
                ManagerVersion = versionMatch.Success ? versionMatch.Groups[1].Value : "Unknown";
                Executable = fullPath;
                return true;
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or OperationCanceledException or ArgumentException or UnauthorizedAccessException)
            { LastDiscoveryError = ex is OperationCanceledException ? "Python Install Manager did not respond." : ex.Message; }
        }
        if (LastDiscoveryError.Length == 0) LastDiscoveryError = "Python Install Manager was not found. Install it or choose pymanager.exe in Settings.";
        return false;
    }

    public async Task<IReadOnlyList<PythonRuntime>> ListAsync(bool online = false, CancellationToken cancellationToken = default, string? source = null)
    {
        var executable = RequireExecutable();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(online ? 120 : 25));
        var args = new List<string> { "list", "--format=json" };
        var index = online && (source is not null || SelectedSource is not null) ? InstallationSource.Validate(source ?? SelectedSource!()) : null;
        if (online) args.Add(index is null ? "--online" : "--source=" + index);
        var result = await runner.RunAsync(executable, args, cancellationToken: timeout.Token);
        EnsureSuccess(result);
        var runtimes = RuntimeParser.Parse(result.Output);
        if (online) return runtimes.Select(r => r with { CatalogIndex = index }).ToArray();
        var managedResult = await runner.RunAsync(executable, ["list", "--format=json", "--only-managed"], cancellationToken: timeout.Token);
        EnsureSuccess(managedResult);
        var managed = RuntimeParser.Parse(managedResult.Output).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return runtimes.Select(r => r with { IsManaged = managed.Contains(r.Id) }).ToArray();
    }

    public async Task<IReadOnlyList<PythonRuntime>> ListCatalogAsync(CancellationToken cancellationToken = default, string? source = null)
    {
        RequireExecutable();
        var index = InstallationSource.Validate(source ?? SelectedSource?.Invoke() ?? InstallationSource.Official);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var http = (Network?.Invoke() ?? new NetworkSettings()).CreateClient(ProxyPassword?.Invoke());
        return await HistoricalCatalog.LoadAsync(http, index, ConfirmDownloadOrigin, timeout.Token);
    }

    public static IReadOnlyList<string> BuildArguments(RuntimeAction action, PythonRuntime runtime)
    {
        if (action is RuntimeAction.Update or RuntimeAction.Uninstall or RuntimeAction.Repair && !runtime.IsManaged)
            throw new InvalidOperationException("This Python installation is not managed by Python Install Manager.");
        if (runtime.Id.Length > 256 || !System.Text.RegularExpressions.Regex.IsMatch(runtime.Id, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z"))
            throw new ArgumentException("Invalid Python runtime ID.");
        if (runtime.Selector.Length > 256 || !System.Text.RegularExpressions.Regex.IsMatch(runtime.Selector, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*\z"))
            throw new ArgumentException("Invalid Python selector");
        if (action is RuntimeAction.Install or RuntimeAction.Repair && (runtime.ExactSelector.Length > 256 ||
            !System.Text.RegularExpressions.Regex.IsMatch(runtime.ExactSelector, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*/[A-Za-z0-9][A-Za-z0-9_.-]*\z")))
            throw new ArgumentException("Invalid exact Python selector");
        // PIM 25.2 / 26.3 --by-id feeds strings into registration code expecting tags.
        // Resolve selectors to an exact identity under the lock before calling a mutation.
        return action switch
        {
            RuntimeAction.Install => ["install", "--yes", runtime.ExactSelector],
            RuntimeAction.Update => ["install", "--yes", "--update", runtime.Selector],
            RuntimeAction.Uninstall => ["uninstall", "--yes", runtime.Selector],
            RuntimeAction.Repair => ["install", "--yes", "--force", runtime.ExactSelector],
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }

    public async Task<CommandResult> ChangeAsync(RuntimeAction action, PythonRuntime runtime, Action<string> output, PimOperation? operation = null, string? expectedInstalledVersion = null)
    {
        RequireMutationSupport();
        if (!await mutation.WaitAsync(0)) throw new InvalidOperationException("Another Python operation is already running.");
        try
        {
            var args = BuildArguments(action, runtime);
            string? index = null;
            if ((SelectedSource is not null || runtime.CatalogIndex is not null) && action != RuntimeAction.Uninstall)
            {
                index = action == RuntimeAction.Install ? InstallationSource.Validate(SelectedSource?.Invoke() ?? runtime.CatalogIndex!) : InstallationSource.Installed(runtime);
                if (action == RuntimeAction.Install && runtime.CatalogIndex != index)
                    throw new IOException("The installation source changed. Reload the catalog");
                args = args.Concat(["--source=" + index]).ToArray();
            }
            LastExpectedRuntime = runtime;
            if (action == RuntimeAction.Repair && !SupportsRepair) throw new InvalidOperationException("This PIM version does not support repair. Update Python Install Manager and reconnect");
            using var operationLock = AcquireOperationLock();
            // Re-query under the lock: a card may be stale after changes made in another window or terminal.
            if (action == RuntimeAction.Install)
                await CheckReplacementAsync(runtime, expectedInstalledVersion, operation?.Token ?? default);
            PythonRuntime? available = null;
            if (action != RuntimeAction.Install)
            {
                var candidates = await ListAsync(cancellationToken: operation?.Token ?? default);
                var current = candidates.SingleOrDefault(r => r.Id.Equals(runtime.Id, StringComparison.OrdinalIgnoreCase));
                if (current is null || !current.SameIdentity(runtime) || current.Tag != runtime.Tag || !current.IsManaged ||
                    !string.Equals(current.Prefix, runtime.Prefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("This Python entry has changed. Refresh the list before trying again.");
                await ResolveRuntimeAsync(runtime, online: false, exact: false, index, operation?.Token ?? default);
            }
            if (action != RuntimeAction.Uninstall)
            {
                // A raw history page is only for browsing. PIM re-reads and validates the
                // configured source/signature policy before we trust any package metadata.
                available = await ResolveRuntimeAsync(runtime, online: true, exact: action != RuntimeAction.Update, index, operation?.Token ?? default);
                LastExpectedRuntime = available;
                if (action == RuntimeAction.Update && RuntimeCatalog.CompareVersions(available.Version, runtime.Version) <= 0)
                { LastExpectedRuntime = runtime; return new CommandResult(0, "No newer version is available", ""); }
            }
            output($"> pymanager {string.Join(' ', args)}");
            // No automatic timeout. Only an explicit user request can stop a mutation.
            CommandResult result;
            if (Network is not null && action != RuntimeAction.Uninstall)
            {
                if (available is null) throw new IOException("This Python version is no longer available from the catalog");
                var directory = Path.Combine(Path.GetTempPath(), "PyDeck-download-" + Guid.NewGuid().ToString("N"));
                try
                {
                    using var http = Network().CreateClient(ProxyPassword?.Invoke());
                    var bundle = await PackageDownload.FetchAsync(available, directory, http, operation, ConfirmDownloadOrigin);
                    using var prepared = await bundle.PrepareAsync(bundle.Runtimes.Single(), operation?.Token ?? default);
                    // PIM consumes a verified bundled cache while retaining its original online source.
                    // A temporary offline source would otherwise be persisted in __install__.json.
                    if (!System.Text.RegularExpressions.Regex.IsMatch(available.Version, @"\A[0-9A-Za-z.+-]+\z")) throw new IOException("Invalid package version");
                    var cached = Path.Combine(directory, available.Id + "-" + available.Version + ".zip");
                    File.Copy(prepared.ArchivePath, cached, false);
                    var config = Path.Combine(directory, "download-config.json");
                    var downloadConfig = new System.Text.Json.Nodes.JsonObject { ["bundled_dir"] = directory };
                    if (index is not null) downloadConfig["install"] = new System.Text.Json.Nodes.JsonObject { ["fallback_source"] = index };
                    AtomicJson.Write(config, downloadConfig.ToJsonString());
                    try
                    {
                        var localArgs = args.Concat(["--config=" + config]).ToArray();
                        // The PyDeck lease does not lock an external PIM terminal. Downloads
                        // can take minutes, so recheck the acknowledged replacement immediately
                        // before starting the mutation, after all network and verification work.
                        if (action == RuntimeAction.Install)
                            await CheckReplacementAsync(runtime, expectedInstalledVersion, operation?.Token ?? default);
                        result = await runner.RunAsync(RequireExecutable(), localArgs, output, operation?.Token ?? default, operation is null ? null : operation.Observe);
                    }
                    finally { SafeFiles.RequireNoLinks(cached); File.Delete(cached); File.Delete(config); }
                }
                finally { RemoveDownload(directory); }
            }
            else
            {
                if (action == RuntimeAction.Install)
                    await CheckReplacementAsync(runtime, expectedInstalledVersion, operation?.Token ?? default);
                result = await runner.RunAsync(RequireExecutable(), args, output, operation?.Token ?? default, operation is null ? null : operation.Observe);
            }
            EnsureSuccess(result);
            return result;
        }
        finally { mutation.Release(); }
    }

    private async Task<PythonRuntime> ResolveRuntimeAsync(PythonRuntime runtime, bool online, bool exact, string? index, CancellationToken token)
    {
        var selector = exact ? runtime.ExactSelector : runtime.Selector;
        var arguments = new List<string> { "list", "--format=json", selector };
        // PIM treats a preview suffix as a prefix (rc1 also matches rc1t). --one asks
        // PIM for the same preferred package its installer selects; we then require
        // the complete ID and version to match rather than accepting another variant.
        if (online && exact) arguments.Add("--one");
        if (online) arguments.Add(index is null ? "--online" : "--source=" + index);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        var resolved = await runner.RunAsync(RequireExecutable(), arguments, cancellationToken: timeout.Token);
        EnsureSuccess(resolved);
        var matches = RuntimeParser.Parse(resolved.Output);
        if (matches.Count != 1 || !matches[0].Id.Equals(runtime.Id, StringComparison.OrdinalIgnoreCase) ||
            matches[0].Company != runtime.Company || matches[0].Tag != runtime.Tag)
            throw new InvalidOperationException("The Python selector is ambiguous. Refresh the list before trying again");
        if ((exact || !online) && !matches[0].SameIdentity(runtime))
            throw new InvalidOperationException("The requested Python version could not be resolved exactly. Refresh the catalog before trying again");
        return matches[0] with { CatalogIndex = index };
    }

    private async Task CheckReplacementAsync(PythonRuntime runtime, string? expectedInstalledVersion, CancellationToken token)
    {
        var current = (await ListAsync(cancellationToken: token)).SingleOrDefault(r => r.Id.Equals(runtime.Id, StringComparison.OrdinalIgnoreCase));
        if (expectedInstalledVersion is not null && (current is null || current.Version != expectedInstalledVersion))
            throw new InvalidOperationException("The installed Python version changed after confirmation. Refresh the list before trying again");
        if (current is not null && !current.SameIdentity(runtime) && (!current.IsManaged || current.Version != expectedInstalledVersion))
            throw new InvalidOperationException("Installing this micro version replaces the existing Python version. Refresh the list and confirm the replacement first");
    }

    private static void RemoveDownload(string directory)
    {
        try
        {
            SafeFiles.RequireNoLinks(directory);
            foreach (var name in new[] { "package.zip", "index.json" })
            { var path = Path.Combine(directory, name); SafeFiles.RequireNoLinks(path); if (File.Exists(path)) File.Delete(path); }
            if (Directory.Exists(directory)) Directory.Delete(directory, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public async Task RefreshRegistrationsAsync(Action<string> output)
    {
        RequireMutationSupport();
        if (!await mutation.WaitAsync(0)) throw new InvalidOperationException("Another Python operation is already running.");
        try
        {
            using var lease = AcquireOperationLock();
            var help = await runner.RunAsync(RequireExecutable(), ["help", "install"]);
            if (!help.Output.Contains("--refresh")) throw new IOException("This PIM version does not support refreshing aliases");
            EnsureSuccess(await runner.RunAsync(RequireExecutable(), ["install", "--refresh", "--yes"], output));
        }
        finally { mutation.Release(); }
    }

    private string RequireExecutable() => Executable ?? throw new InvalidOperationException("Connect to Python Install Manager first.");
    private void RequireMutationSupport()
    {
        if (!SupportsMutations) throw new InvalidOperationException("Update to PIM 26.3 or later before changing Python installations");
    }
    public IDisposable AcquireConfigurationLock() => AcquireOperationLock();
    private FileStream AcquireOperationLock()
    {
        var path = mutationLockPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PyDeck", "operation.lock");
        SafeFiles.RequireNoLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new InvalidOperationException("Another PyDeck window is managing Python. Wait for it to finish.", ex); }
    }
    private static void EnsureSuccess(CommandResult result)
    {
        if (result.OutputTruncated) throw new InvalidOperationException("Python Install Manager output exceeded the capture limit. Refresh the list to verify the operation's result.");
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            if (detail.Length > 4096) detail = detail[..4096] + "\n[Output shortened]";
            throw new InvalidOperationException($"Python Install Manager exited with code {result.ExitCode}.\n{detail.Trim()}");
        }
    }
}
