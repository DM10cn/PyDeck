using System.Text.Json;
using System.Text.RegularExpressions;

namespace PimGui.Core;

public sealed record EnvironmentPackage(string Name, string Version);
public sealed record PackageSnapshot(bool HasPip, IReadOnlyList<EnvironmentPackage> Packages, string DependencyIssue = "");
public enum PackageAction { Install, Upgrade, Uninstall, PreparePip, UpgradePip }

public sealed class EnvironmentPackages(string dataDirectory, NetworkSettings network, string? password = null)
{
    // Validate identity again inside the process that invokes pip, not only in a preceding probe.
    private const string Guard = "import sys,os; expected=os.path.normcase(os.path.realpath(sys.argv.pop(1))); assert os.path.normcase(os.path.realpath(sys.prefix))==expected and sys.prefix!=sys.base_prefix, 'Wrong virtual environment'; ";
    private const string Listing = Guard + "import json,importlib.metadata as m; root=os.path.normcase(os.path.realpath(sys.prefix))+os.sep; packages=[{'Name':d.metadata['Name'],'Version':d.version} for d in m.distributions() if d.metadata['Name'] and os.path.normcase(os.path.realpath(str(d.locate_file('')))).startswith(root)]; [print(json.dumps(p)) for p in packages]";
    private const string Pip = Guard + "import pip; assert os.path.normcase(os.path.realpath(pip.__file__)).startswith(expected+os.sep), 'pip must belong to this environment'; os.environ['PIP_CONFIG_FILE']=os.devnull; from pip._internal.cli.main import main; sys.exit(main(sys.argv[1:]))";

    public static string[] ParseRequirements(string text)
    {
        if (text.Length > 256 * 1024) throw new ArgumentException("Requirements file is too large");
        var lines = text.Split('\n').Select(s => s.Split('#', 2)[0].Trim()).Where(s => s.Length > 0).ToArray();
        if (lines.Length is 0 or > 500) throw new ArgumentException("Enter between 1 and 500 package requirements");
        foreach (var line in lines) ValidateRequirement(line);
        return lines;
    }
    public static void ValidateRequirement(string text)
    {
        if (text.Length > 300 || !Regex.IsMatch(text, @"\A[A-Za-z0-9][A-Za-z0-9._-]*(?:\[[A-Za-z0-9._-]+(?:,[A-Za-z0-9._-]+)*\])?(?:(?:==|!=|~=|>=|<=|>|<)[A-Za-z0-9.*+!_-]+(?:,(?:==|!=|~=|>=|<=|>|<)[A-Za-z0-9.*+!_-]+)*)?\z"))
            throw new ArgumentException("Use package names and version constraints; URLs, paths, options and markers are not supported");
    }
    private FileStream Lease()
    {
        SafeFiles.RequireNoLinks(dataDirectory); Directory.CreateDirectory(dataDirectory); var path = Path.Combine(dataDirectory, "environments.lock"); SafeFiles.RequireNoLinks(path);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
    }
    private static VirtualEnvironment CheckPath(string path)
    {
        var env = VirtualEnvironments.Inspect(path);
        if (env.State != "Not checked") throw new IOException(env.State);
        SafeFiles.RequireNoLinks(Path.Combine(env.Path, "Scripts"));
        SafeFiles.RequireNoLinks(Path.Combine(env.Path, "Lib", "site-packages"));
        return env;
    }
    private Dictionary<string, string?> ProcessEnvironment()
    {
        var env = network.Environment(password);
        env["PIP_CONFIG_FILE"] = "nul"; env["PYTHONNOUSERSITE"] = "1";
        return env;
    }
    public async Task<PackageSnapshot> ListAsync(string path, CancellationToken token = default)
    {
        using var lease = Lease();
        return await ListUnlockedAsync(path, token);
    }
    private async Task<PackageSnapshot> ListUnlockedAsync(string path, CancellationToken token)
    {
        var env = CheckPath(path);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var output = new System.Text.StringBuilder();
        await BuildProcess.RunAsync(env.Executable, ["-I", "-c", Listing, env.Path], null, Path.GetTempPath(), ProcessEnvironment(), line =>
        {
            if (output.Length + line.Length > 2 * 1024 * 1024) throw new IOException("Package list is too large");
            output.AppendLine(line);
        }, deadline.Token);
        var packages = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => JsonSerializer.Deserialize<EnvironmentPackage>(s) ?? throw new IOException("Invalid package list")).ToArray();
        var hasPip = packages.Any(p => p.Name.Equals("pip", StringComparison.OrdinalIgnoreCase));
        var issue = "";
        if (hasPip)
        {
            var checkOutput = new System.Text.StringBuilder();
            try
            {
                await BuildProcess.RunAsync(env.Executable, new[] { "-I", "-c", Pip, env.Path, "--disable-pip-version-check", "--no-input", "--require-virtualenv", "check" }, null,
                    Path.GetTempPath(), ProcessEnvironment(), line => { if (checkOutput.Length < 16000) checkOutput.AppendLine(SensitiveText.Redact(line, password)); }, deadline.Token);
            }
            catch (IOException) { issue = checkOutput.Length > 0 ? checkOutput.ToString() : "Package dependency check failed"; }
        }
        return new(hasPip, packages.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray(), issue);
    }
    public static string[] Arguments(PackageAction action, IReadOnlyList<string> requirements)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentException("Invalid package action");
        foreach (var requirement in requirements) ValidateRequirement(requirement);
        var args = new List<string> { "--disable-pip-version-check", "--no-input", "--require-virtualenv" };
        if (action == PackageAction.Uninstall)
        {
            if (requirements.Count != 1 || !Regex.IsMatch(requirements[0], @"\A[A-Za-z0-9][A-Za-z0-9._-]*\z") || requirements[0].Equals("pip", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose one installed package other than pip");
            args.AddRange(["uninstall", "--yes"]);
        }
        else
        {
            args.AddRange(["install", "--index-url", "https://pypi.org/simple", "--no-cache-dir"]);
            if (action is PackageAction.Upgrade or PackageAction.UpgradePip) args.Add("--upgrade");
        }
        if (action == PackageAction.UpgradePip) args.Add("pip");
        else { if (requirements.Count is 0 or > 500) throw new ArgumentException("Choose packages first"); args.AddRange(requirements); }
        return args.ToArray();
    }
    public async Task<PackageSnapshot> ChangeAsync(string path, PackageAction action, IReadOnlyList<string> requirements,
        Action<string> output, CancellationToken token = default)
    {
        using var lease = Lease();
        var env = CheckPath(path);
        // Keep potentially large environment scans off the UI thread; reject redirected package files.
        _ = await Task.Run(() => BuildStorage.Snapshot(env.Path), token);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromMinutes(30));
        var args = action == PackageAction.PreparePip
            ? new[] { "-I", "-c", Guard + "import ensurepip; ensurepip.bootstrap(upgrade=True)", env.Path }
            : new[] { "-I", "-c", Pip, env.Path }.Concat(Arguments(action, requirements)).ToArray();
        await BuildProcess.RunAsync(env.Executable, args, null, Path.GetTempPath(), ProcessEnvironment(),
            line => output(SensitiveText.Redact(line, password)), deadline.Token);
        var snapshot = await ListUnlockedAsync(path, deadline.Token);
        if (action is PackageAction.PreparePip or PackageAction.UpgradePip && !snapshot.HasPip) throw new IOException("pip is still unavailable");
        return snapshot;
    }
    public static string Export(PackageSnapshot snapshot) => string.Join(Environment.NewLine,
        snapshot.Packages.Select(p => { var line = p.Name + "==" + p.Version; ValidateRequirement(line); return line; })) + Environment.NewLine;
}
