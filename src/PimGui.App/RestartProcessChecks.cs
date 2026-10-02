using System.Text.Json;

namespace PimGui.App;

public sealed partial class MainWindow
{
    private sealed record RestartProbeState(int OriginalPid, string ExpectedDesign);

    private async Task RunRestartProbeAsync(string directory)
    {
        var marker = Path.Combine(directory, "restart-parent.json");
        var result = Path.Combine(directory, "result.json");
        try
        {
            if (!RestartProbe || smokeDirectory is null || !Path.GetFullPath(store.DirectoryPath).StartsWith(Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Restart verification requires an isolated profile");
            Directory.CreateDirectory(directory);
            if (Environment.GetCommandLineArgs().Contains("--restart-arrived"))
            {
                var before = JsonSerializer.Deserialize<RestartProbeState>(await File.ReadAllTextAsync(marker))!;
                if (before.OriginalPid == Environment.ProcessId || ActiveDesign != before.ExpectedDesign || preferences.Design != before.ExpectedDesign)
                    throw new IOException("Restart did not create a new process with the persisted presentation");
                await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new
                {
                    passed = true, originalPid = before.OriginalPid, restartedPid = Environment.ProcessId,
                    design = ActiveDesign, profile = store.DirectoryPath,
                    checks = new[] { "Windows AppInstance.Restart launched a new process and applied the saved style in the same isolated profile" }
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                if (File.Exists(marker)) throw new IOException("Restart verification requires a fresh directory");
                using (var work = StartWork(PimGui.Core.WorkKind.Build) ?? throw new IOException("Cannot start restart work fixture"))
                    if (await RequestAppRestartAsync() || restartInProgress || !workCoordinator.Contains(PimGui.Core.WorkKind.Build))
                        throw new IOException("Restart accepted an active work lease");
                var operation = BeginOperation("Isolated restart protection check");
                try
                {
                    if (await RequestAppRestartAsync() || restartInProgress || operation.IsCancellationRequested)
                        throw new IOException("Restart accepted or cancelled an unfinished operation");
                }
                finally { FinishOperation(operation); }
                var target = ActiveDesign == "Fluent" ? "Material" : "Fluent";
                SavePreferences(preferences with { Design = target });
                if (preferences.Design != target || store.Load().Design != target) throw new IOException("Could not save the restart fixture style");
                await File.WriteAllTextAsync(marker, JsonSerializer.Serialize(new RestartProbeState(Environment.ProcessId, target)));
                // Only self-created directory arguments are forwarded, never a normal smoke run
                // or the user's profile. Windows paths cannot contain a double quote.
                await RequestAppRestartAsync("--smoke-test \"" + directory + "\" --restart-only --restart-arrived");
                throw new IOException("Windows did not accept the isolated restart request");
            }
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(result, JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Close(); }
    }
}
