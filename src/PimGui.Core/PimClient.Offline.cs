namespace PimGui.Core;

public sealed partial class PimClient
{
    public async Task InstallOfflineAsync(OfflineBundle bundle, PythonRuntime runtime, Action<string> output, bool dryRun = false, PimOperation? operation = null)
    {
        if (!await mutation.WaitAsync(0)) throw new InvalidOperationException("Another Python operation is already running.");
        try
        {
            _ = BuildArguments(RuntimeAction.Install, runtime);
            using var operationLock = AcquireOperationLock();
            // Checksum a private snapshot before PIM can consume it. No online catalog lookup.
            operation?.Report(OperationPhase.Verifying);
            using var prepared = await bundle.PrepareAsync(runtime, operation?.Token ?? default);
            var arguments = new List<string> { "install", "--yes", "--by-id", "--source=" + prepared.IndexPath,
                "--config=" + prepared.ConfigPath };
            if (dryRun) arguments.Add("--dry-run");
            arguments.Add(runtime.Id);
            output("> pymanager " + string.Join(' ', arguments));
            operation?.Report(OperationPhase.Preparing);
            EnsureSuccess(await runner.RunAsync(RequireExecutable(), arguments, output, operation?.Token ?? default, operation is null ? null : operation.Observe));
        }
        finally { mutation.Release(); }
    }

    public async Task<string> DownloadOfflineAsync(PythonRuntime runtime, string parentDirectory, Action<string> output, PimOperation? operation = null)
    {
        if (!await mutation.WaitAsync(0)) throw new InvalidOperationException("Another Python operation is already running.");
        try
        {
            _ = BuildArguments(RuntimeAction.Install, runtime);
            parentDirectory = ExecutionPaths.LocalPath(parentDirectory);
            SafeFiles.RequireNoLinks(parentDirectory);
            using var operationLock = AcquireOperationLock();
            var current = (await ListAsync(true, operation?.Token ?? default)).SingleOrDefault(r => r.Id == runtime.Id && r.Version == runtime.Version && r.Company == runtime.Company);
            if (current is null) throw new InvalidOperationException("This Python entry has changed. Refresh the list before trying again.");
            var directory = Path.Combine(parentDirectory, "Python-offline-" + runtime.Id + "-" + Guid.NewGuid().ToString("N")[..8]);
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Choose another download folder.");
            Directory.CreateDirectory(directory);
            string[] arguments = ["install", "--yes", "--by-id", "--download=" + directory, runtime.Id];
            output("> pymanager " + string.Join(' ', arguments));
            EnsureSuccess(await runner.RunAsync(RequireExecutable(), arguments, output, operation?.Token ?? default, operation is null ? null : operation.Observe));
            operation?.Report(OperationPhase.Verifying);
            var bundle = OfflineBundle.Load(directory);
            var downloaded = bundle.Runtimes.SingleOrDefault(r => r.Id == runtime.Id && r.Version == runtime.Version)
                ?? throw new IOException("The downloaded bundle does not match the selected Python version.");
            using var verified = await bundle.PrepareAsync(downloaded, operation?.Token ?? default);
            return directory;
        }
        finally { mutation.Release(); }
    }
}
