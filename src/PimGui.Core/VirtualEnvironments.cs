using System.Text.Json;

namespace PimGui.Core;

public sealed record VirtualEnvironment(string Path, string State = "Not checked", string Version = "", string BasePath = "")
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Executable => System.IO.Path.Combine(Path, "Scripts", "python.exe");
}

public sealed class VirtualEnvironments(string dataDirectory)
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true, EntryPoint = "CreateDirectoryW")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateDirectoryExclusive(string path, IntPtr securityAttributes);
    private string RegistryPath => System.IO.Path.Combine(dataDirectory, "environments.json");
    public IReadOnlyList<VirtualEnvironment> Read()
    {
        SafeFiles.RequireNoLinks(RegistryPath);
        return File.Exists(RegistryPath) ? JsonSerializer.Deserialize<VirtualEnvironment[]>(SafeFiles.ReadText(RegistryPath))
            ?? throw new IOException("Invalid environment list") : [];
    }
    public void Remember(VirtualEnvironment environment, bool remove = false)
    {
        Directory.CreateDirectory(dataDirectory);
        SafeFiles.RequireNoLinks(dataDirectory);
        var lockPath = System.IO.Path.Combine(dataDirectory, "environments.lock"); SafeFiles.RequireNoLinks(lockPath);
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        var previous = File.Exists(RegistryPath) ? SafeFiles.ReadText(RegistryPath) : null;
        var entries = Read().Where(e => !e.Path.Equals(environment.Path, StringComparison.OrdinalIgnoreCase)).ToList();
        if (!remove) entries.Add(environment);
        AtomicJson.Write(RegistryPath, JsonSerializer.Serialize(entries), previous, checkOriginal: true);
    }
    public static VirtualEnvironment Inspect(string path)
    {
        path = ExecutionPaths.LocalPath(path); SafeFiles.RequireNoLinks(path);
        var cfg = System.IO.Path.Combine(path, "pyvenv.cfg"); SafeFiles.RequireNoLinks(cfg);
        if (!File.Exists(cfg)) return new(path, "Incomplete environment");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in SafeFiles.ReadText(cfg).Split('\n'))
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2) values[parts[0].Trim()] = parts[1].Trim();
        }
        var home = values.GetValueOrDefault("home", "");
        var version = values.GetValueOrDefault("version", "");
        var result = new VirtualEnvironment(path, "Not checked", version, home);
        SafeFiles.RequireNoLinks(result.Executable);
        if (!File.Exists(result.Executable)) return result with { State = "Incomplete environment" };
        try { home = ExecutionPaths.LocalPath(home); SafeFiles.RequireNoLinks(home); }
        catch (Exception ex) when (ex is ArgumentException or IOException) { return result with { State = "Base interpreter missing" }; }
        if (!File.Exists(System.IO.Path.Combine(home, "python.exe"))) return result with { State = "Base interpreter missing" };
        return result;
    }
    public async Task<VirtualEnvironment> CreateAsync(PythonRuntime runtime, string parent, string name, IProcessRunner runner, PimOperation operation)
    {
        ExecutionPaths.Runtime(runtime); SafeFiles.RequireNoLinks(runtime.Executable);
        parent = ExecutionPaths.LocalPath(parent); SafeFiles.RequireNoLinks(parent);
        if (!Directory.Exists(parent) || string.IsNullOrWhiteSpace(name) || name != name.Trim() ||
            name.Length > 100 || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || name is "." or ".." || name.EndsWith('.') ||
            System.Text.RegularExpressions.Regex.IsMatch(name, @"\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException("Choose a valid environment name");
        var path = System.IO.Path.Combine(parent, name);
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("The environment folder already exists");
        operation.Token.ThrowIfCancellationRequested();
        if (!CreateDirectoryExclusive(path, IntPtr.Zero)) throw new IOException("The environment folder could not be reserved");
        SafeFiles.RequireNoLinks(path);
        // Record intent before starting Python. Cancellation never deletes a project directory.
        var environment = new VirtualEnvironment(path, "Incomplete environment", runtime.Version, runtime.Prefix);
        Remember(environment);
        try
        {
            operation.Token.ThrowIfCancellationRequested();
            var result = await runner.RunAsync(runtime.Executable, ["-I", "-m", "venv", path], cancellationToken: operation.Token);
            if (result.ExitCode != 0 || result.OutputTruncated) throw new IOException("Environment creation failed; partial files were kept");
            environment = await CheckAsync(path, runner, operation.Token);
            return environment;
        }
        finally { Remember(environment); }
    }
    public static async Task<VirtualEnvironment> CheckAsync(string path, IProcessRunner runner, CancellationToken token = default)
    {
        var environment = Inspect(path);
        if (environment.State != "Not checked") return environment;
        SafeFiles.RequireNoLinks(environment.BasePath);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        const string probe = "import sys,json,encodings,ssl;print(json.dumps({'prefix':sys.prefix,'base':sys.base_prefix,'version':'.'.join(map(str,sys.version_info[:3]))}))";
        try
        {
            var result = await runner.RunAsync(environment.Executable, ["-I", "-c", probe], cancellationToken: timeout.Token);
            using var json = JsonDocument.Parse(result.Output);
            var prefix = json.RootElement.GetProperty("prefix").GetString()!;
            var basePath = json.RootElement.GetProperty("base").GetString()!;
            if (result.ExitCode != 0 || result.OutputTruncated || !System.IO.Path.GetFullPath(prefix).TrimEnd('\\').Equals(environment.Path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) || prefix.Equals(basePath, StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFullPath(basePath).TrimEnd('\\').Equals(environment.BasePath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return environment with { State = "Environment check failed" };
            return environment with { State = "Environment ready", Version = json.RootElement.GetProperty("version").GetString()! };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or JsonException or System.ComponentModel.Win32Exception)
        { return environment with { State = "Environment check failed" }; }
    }
}
