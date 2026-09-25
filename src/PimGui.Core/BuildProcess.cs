using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PimGui.Core;

/// <summary>A dedicated, bounded build process runner. The Windows job closes all descendants, including MSBuild nodes.</summary>
internal static class BuildProcess
{
    public static async Task RunAsync(string executable, IReadOnlyList<string>? args, string? commandLine, string directory,
        IReadOnlyDictionary<string, string?> environment, Action<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = directory, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        // Drop developer-shell injections and Python discovery overrides. All build inputs are explicit.
        foreach (var key in start.Environment.Keys.ToArray())
            if (key.StartsWith("PYTHON", StringComparison.OrdinalIgnoreCase) || key.StartsWith("PYMANAGER", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("MSBUILD", StringComparison.OrdinalIgnoreCase) || key.StartsWith("VSCMD", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("VCTools", StringComparison.OrdinalIgnoreCase) || key.StartsWith("WindowsSDK", StringComparison.OrdinalIgnoreCase) ||
                new[] { "EXTERNALS_DIR", "HOST_PYTHON", "VIRTUAL_ENV", "CL", "_CL_", "LINK", "_LINK_", "INCLUDE", "LIB", "LIBPATH", "TCL_LIBRARY", "TK_LIBRARY", "Platform", "Configuration" }.Contains(key, StringComparer.OrdinalIgnoreCase))
                start.Environment.Remove(key);
        start.Environment["PYTHONUTF8"] = "1"; start.Environment["PYTHON_MANAGER_AUTOMATIC_INSTALL"] = "false";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        foreach (var entry in environment) start.Environment[entry.Key] = entry.Value;
        if (commandLine is not null) start.Arguments = commandLine;
        else foreach (var arg in args ?? []) start.ArgumentList.Add(arg);
        using var group = new ProcessGroup();
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("Build process could not be started");
        try { group.Attach(process); }
        catch { try { process.Kill(true); } catch { } await process.WaitForExitAsync(CancellationToken.None); throw; }
        process.StandardInput.Close();
        Exception? logFailure = null;
        void Report(string line) { try { log(line); } catch (Exception ex) { Interlocked.CompareExchange(ref logFailure, ex, null); } }
        async Task Drain(StreamReader reader)
        {
            var buffer = new char[4096]; var line = new StringBuilder(); var omitted = false;
            while (await reader.ReadAsync(buffer) is var count && count > 0)
                for (var i = 0; i < count; i++)
                {
                    var ch = buffer[i];
                    if (ch is '\n' or '\r')
                    {
                        if (omitted) Report("[Long build output line omitted]"); else if (line.Length > 0) Report(line.ToString());
                        line.Clear(); omitted = false;
                    }
                    else if (!omitted) { if (line.Length >= 16384) { line.Clear(); omitted = true; } else line.Append(ch); }
                }
            if (omitted) Report("[Long build output line omitted]"); else if (line.Length > 0) Report(line.ToString());
        }
        var stdout = Drain(process.StandardOutput); var stderr = Drain(process.StandardError);
        try { await process.WaitForExitAsync(token); }
        finally
        {
            // Also closes descendants if the root exits while a child still owns a redirected pipe.
            group.Dispose();
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
        }
        token.ThrowIfCancellationRequested();
        if (logFailure is not null) throw new IOException("Could not write the build log", logFailure);
        if (process.ExitCode != 0) throw new IOException($"Build step failed (exit {process.ExitCode}). See the build log");
    }

    private sealed class ProcessGroup : IDisposable
    {
        private readonly SafeFileHandle handle;
        public ProcessGroup()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle.IsInvalid) throw new Win32Exception();
            var info = new ExtendedLimit { Basic = new BasicLimit { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE
            if (!SetInformationJobObject(handle, 9, ref info, (uint)Marshal.SizeOf<ExtendedLimit>())) { handle.Dispose(); throw new Win32Exception(); }
        }
        public void Attach(Process process) { if (!AssignProcessToJobObject(handle, process.Handle)) throw new Win32Exception(); }
        public void Dispose() => handle.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimit { public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong A, B, C, D, E, F; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimit { public BasicLimit Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimit info, uint length);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}
