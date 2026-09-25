namespace PimGui.Core;

public sealed record BuildOptions(bool IncludeTk = true, bool IncludeTests = false, bool IncludeSymbols = false,
    string Architecture = "x64", string Configuration = "Release", string Version = "3.14.7",
    bool Pgo = false, bool IncludePip = true, bool IncludeSsl = true, bool IncludeCtypes = true,
    bool IncludeSqlite = true, string SourceArchive = "", string OutputParent = "")
{
    public void Validate()
    {
        if (Architecture != "x64" || Configuration is not ("Release" or "Debug") ||
            !System.Text.RegularExpressions.Regex.IsMatch(Version, @"\A[23]\.\d{1,2}\.\d{1,3}(?:(?:a|b|rc)\d{1,2})?\z"))
            throw new ArgumentException("Choose a CPython release version and an x64 Release or Debug build");
        if (Pgo && Configuration != "Release") throw new ArgumentException("PGO requires Release");
        if (IncludePip && !IncludeSsl) throw new ArgumentException("Enable SSL before including pip");
        if (SourceArchive.Length > 0) BuildRecipe.BatchPath(SourceArchive);
        if (OutputParent.Length > 0) BuildRecipe.BatchPath(OutputParent);
    }
    public string ExecutableName => Configuration == "Debug" ? "python_d.exe" : "python.exe";
    public static BuildOptions Preset(string name, BuildOptions previous) => (name switch
    {
        "Performance" => new BuildOptions(Pgo: true),
        "Debug" => new BuildOptions(Configuration: "Debug", IncludeSymbols: true),
        "Minimal" => new BuildOptions(IncludeTk: false, IncludePip: false),
        "Standard" => new BuildOptions(),
        _ => previous
    }) with { Version = previous.Version, SourceArchive = previous.SourceArchive, OutputParent = previous.OutputParent };
}

public static class BuildRecipe
{
    public const string Version = "3.14.7";
    public const string SourceUrl = "https://www.python.org/ftp/python/3.14.7/Python-3.14.7.tgz";
    // Published messageDigest in Python-3.14.7.tgz.sigstore, retrieved from python.org.
    public const string SourceSha256 = "62859805f6fdf25e2bcbf3fa3217801e1996887ca33e6a2af80674bdfa2dbe07";

    public static string CompilerArguments(BuildOptions options, BuildToolchain tools, string? stage = null)
    {
        options.Validate(); tools.Validate();
        if (stage is not (null or "PGInstrument" or "PGUpdate")) throw new ArgumentException("Invalid build stage");
        return "-p x64 -c " + (stage ?? (options.Pgo ? "PGInstrument" : options.Configuration)) + " -e" + (options.IncludeTk ? "" : " --no-tkinter") +
            (options.IncludeSsl ? "" : " --no-ssl") + (options.IncludeCtypes || options.Pgo ? "" : " --no-ctypes") +
            " \"/p:PlatformToolset=" + tools.PlatformToolset + "\" \"/p:WindowsTargetPlatformVersion=" + tools.SdkVersion +
            "\" \"/p:VCToolsVersion=" + tools.CompilerVersion + "\" /nodeReuse:false";
    }

    public static string[] TrainingArguments(string source) => source.Contains(' ')
        ? ["-m", "test", "--pgo", "-x", "test_tabnanny"] : ["-m", "test", "--pgo"];

    public static string[] LayoutArguments(BuildOptions options, string source, string destination, string temp)
    {
        options.Validate();
        var result = new List<string> { "-I", Path.Combine(source, "PC", "layout", "main.py"), "-s", source,
            "-b", Path.Combine(source, "PCbuild", "amd64"), "--arch", "amd64", "--copy", destination, "-t", temp,
            "--include-alias", "--include-stable", "--include-venv", "--include-dev" };
        // ensurepip installs into the final prefix after layout, so console launchers never retain a staging path.
        if (options.IncludeTk) result.AddRange(["--include-tcltk", "--include-idle"]);
        if (options.IncludeTests) result.Add("--include-tests");
        if (options.IncludeSymbols) result.Add("--include-symbols");
        if (options.Configuration == "Debug") result.Add("--debug");
        return result.ToArray();
    }

    public static string BatchPath(string path)
    {
        path = ExecutionPaths.LocalPath(path);
        // PCbuild is a batch pipeline with CALL expansion. Reject metacharacters even inside quoted paths.
        if (path.IndexOfAny(['%', '!', '^', '&', '|', '<', '>', '"', '\r', '\n']) >= 0)
            throw new ArgumentException("Build paths cannot contain shell metacharacters");
        SafeFiles.RequireNoLinks(path);
        return path;
    }
}
