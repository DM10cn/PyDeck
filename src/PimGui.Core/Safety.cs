namespace PimGui.Core;

public static class SafeFiles
{
    public static string ReadText(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 2 * 1024 * 1024) throw new IOException("The configuration file is too large to read safely.");
        using var reader = new StreamReader(file);
        return reader.ReadToEnd();
    }

    // Refuse redirected write targets rather than replacing links or following them out of the expected folder.
    public static void RequireNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The configuration location is redirected by a link. Manage this configuration outside PyDeck.");
    }
}

public static class ExecutionPaths
{
    public static string LocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.Any(char.IsControl) || path.Contains(';') || path.IndexOf(':', 2) >= 0)
            throw new ArgumentException("Choose an absolute path on a local drive.");
        return Path.GetFullPath(path);
    }
    public static string Manager(string path)
    {
        var full = LocalPath(path);
        if (!new[] { "pymanager.exe", "py.exe" }.Contains(Path.GetFileName(full), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Choose pymanager.exe or a supported py.exe launcher.");
        return full;
    }
    public static void Runtime(PythonRuntime runtime)
    {
        var prefix = LocalPath(runtime.Prefix);
        var executable = LocalPath(runtime.Executable);
        var relative = Path.GetRelativePath(prefix, executable);
        if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || relative == ".." ||
            Path.IsPathRooted(relative) || !executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The interpreter must be an executable inside its installation folder.");
        if (!Directory.Exists(prefix) || !File.Exists(executable))
            throw new FileNotFoundException("This Python installation is no longer available. Refresh the version list.");
    }
}
