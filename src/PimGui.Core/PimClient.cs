namespace PimGui.Core;

public enum RuntimeAction { Install, Update, Uninstall }

public sealed partial class PimClient(IProcessRunner runner, string? mutationLockPath = null)
{
    private readonly SemaphoreSlim mutation = new(1, 1);
    public string? Executable { get; private set; }
    public string LastDiscoveryError { get; private set; } = "";

    public async Task<bool> DiscoverAsync(string? preferredPath = null)
    {
        Executable = null;
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
                Executable = fullPath;
                return true;
            }
            catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or OperationCanceledException or ArgumentException or UnauthorizedAccessException)
            { LastDiscoveryError = ex is OperationCanceledException ? "Python Install Manager did not respond." : ex.Message; }
        }
        if (LastDiscoveryError.Length == 0) LastDiscoveryError = "Python Install Manager was not found. Install it or choose pymanager.exe in Settings.";
        return false;
    }

    public async Task<IReadOnlyList<PythonRuntime>> ListAsync(bool online = false, CancellationToken cancellationToken = default)
    {
        var executable = RequireExecutable();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(online ? 120 : 25));
        var args = new List<string> { "list", "--format=json" };
        if (online) args.Add("--online");
        var result = await runner.RunAsync(executable, args, cancellationToken: timeout.Token);
        EnsureSuccess(result);
        var runtimes = RuntimeParser.Parse(result.Output);
        if (online) return runtimes;
        var managedResult = await runner.RunAsync(executable, ["list", "--format=json", "--only-managed"], cancellationToken: timeout.Token);
        EnsureSuccess(managedResult);
        var managed = RuntimeParser.Parse(managedResult.Output).Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return runtimes.Select(r => r with { IsManaged = managed.Contains(r.Id) }).ToArray();
    }

    public static IReadOnlyList<string> BuildArguments(RuntimeAction action, PythonRuntime runtime)
    {
        if ((action == RuntimeAction.Update || action == RuntimeAction.Uninstall) && !runtime.IsManaged)
            throw new InvalidOperationException("This Python installation is not managed by Python Install Manager.");
        if (runtime.Id.Length > 256 || !System.Text.RegularExpressions.Regex.IsMatch(runtime.Id, @"\A[A-Za-z0-9][A-Za-z0-9_.-]*\z"))
            throw new ArgumentException("Invalid Python runtime ID.");
        return action switch
        {
            RuntimeAction.Install => ["install", "--yes", "--by-id", runtime.Id],
            RuntimeAction.Update => ["install", "--yes", "--update", "--by-id", runtime.Id],
            RuntimeAction.Uninstall => ["uninstall", "--yes", "--by-id", runtime.Id],
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }

    public async Task<CommandResult> ChangeAsync(RuntimeAction action, PythonRuntime runtime, Action<string> output, PimOperation? operation = null)
    {
        if (!await mutation.WaitAsync(0)) throw new InvalidOperationException("Another Python operation is already running.");
        try
        {
            var args = BuildArguments(action, runtime);
            using var operationLock = AcquireOperationLock();
            // Re-query under the lock: a card may be stale after changes made in another window or terminal.
            var candidates = await ListAsync(online: action == RuntimeAction.Install, cancellationToken: operation?.Token ?? default);
            var current = candidates.SingleOrDefault(r => r.Id.Equals(runtime.Id, StringComparison.OrdinalIgnoreCase));
            if (current is null || current.Company != runtime.Company || current.Tag != runtime.Tag ||
                (action != RuntimeAction.Install && (!current.IsManaged || !string.Equals(current.Prefix, runtime.Prefix, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("This Python entry has changed. Refresh the list before trying again.");
            output($"> pymanager {string.Join(' ', args)}");
            // No automatic timeout. Only an explicit user request can stop a mutation.
            var result = await runner.RunAsync(RequireExecutable(), args, output, operation?.Token ?? default, operation is null ? null : operation.Observe);
            EnsureSuccess(result);
            return result;
        }
        finally { mutation.Release(); }
    }

    private string RequireExecutable() => Executable ?? throw new InvalidOperationException("Connect to Python Install Manager first.");
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
