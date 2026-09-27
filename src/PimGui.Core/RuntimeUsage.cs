namespace PimGui.Core;

public static class RuntimeUsage
{
    public static IReadOnlyList<VirtualEnvironment> Find(PythonRuntime runtime, IEnumerable<VirtualEnvironment> environments)
    {
        static bool Same(string a, string b)
        {
            try { return Path.GetFullPath(a).TrimEnd('\\', '/').Equals(Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
        }
        return environments.Where(e => e.BaseRuntimeId == runtime.Id || Same(e.BasePath, runtime.Prefix) ||
        LiveBaseMatches(e, runtime.Prefix)).ToArray();
        static bool LiveBaseMatches(VirtualEnvironment e, string prefix)
        {
            try { return Same(VirtualEnvironments.Inspect(e.Path).BasePath, prefix); }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { return false; }
        }
    }
}
