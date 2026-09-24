using System.Diagnostics;
using System.Text;

namespace PimGui.Core;

public sealed record CommandResult(int ExitCode, string Output, string Error, bool OutputTruncated = false);
public readonly record struct ProcessOutput(string Text, bool IsError);

public interface IProcessRunner
{
    Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        Action<string>? output = null, CancellationToken cancellationToken = default, Action<ProcessOutput>? observe = null);
}

public sealed class ProcessRunner(Func<NetworkSettings>? network = null, Func<string?>? password = null) : IProcessRunner
{
    public async Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments,
        Action<string>? output = null, CancellationToken cancellationToken = default, Action<ProcessOutput>? observe = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetTempPath()
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        var secret = password?.Invoke();
        if (network is not null)
            foreach (var item in network().Environment(secret)) start.Environment[item.Key] = item.Value;
        start.Environment["PYTHONUTF8"] = "1";
        start.Environment["PYTHON_COLORS"] = "0";
        start.Environment["NO_COLOR"] = "1";
        // Even a misidentified legacy launcher must not open a REPL or install a runtime.
        start.Environment["PYTHON_MANAGER_AUTOMATIC_INSTALL"] = "false";
        // A BITS transfer can outlive its parent process. Cancellable installs use PIM's
        // in-process download backends; never cancel unrelated system BITS jobs.
        if (cancellationToken.CanBeCanceled && arguments.FirstOrDefault() == "install")
            start.Environment["PYMANAGER_ENABLE_BITS_DOWNLOAD"] = "0";
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("The Python Install Manager process could not be started.");
        process.StandardInput.Close();
        var truncated = 0;
        var notifications = 0;
        async Task<string> ReadAsync(StreamReader reader, bool isError)
        {
            var buffer = new StringBuilder();
            var line = new StringBuilder();
            var longLine = false;
            var chunk = new char[2048];
            void Report()
            {
                if (output is not null && Interlocked.Increment(ref notifications) <= 2000)
                {
                    // Observers must never stop pipe draining and deadlock the child process.
                    // Never emit a cut fragment that might contain only half of a credential.
                    try { output(longLine ? "[Long output line omitted]" : SensitiveText.Redact(line.ToString(), secret)); } catch (Exception) { }
                }
                line.Clear();
                longLine = false;
            }
            while (await reader.ReadAsync(chunk) is var count && count > 0)
            {
                // PIM flushes progress dots without a newline. Forward fragments immediately.
                try { observe?.Invoke(new(new string(chunk, 0, count), isError)); } catch (Exception) { }
                var retained = Math.Min(count, 8 * 1024 * 1024 - buffer.Length);
                buffer.Append(chunk, 0, retained);
                if (retained < count) Interlocked.Exchange(ref truncated, 1);
                foreach (var ch in chunk.AsSpan(0, count))
                {
                    if (ch == '\n') Report();
                    else if (ch != '\r' && !longLine)
                    {
                        if (line.Length == 2048) { longLine = true; line.Clear(); }
                        else line.Append(ch);
                    }
                }
            }
            if (line.Length > 0 || longLine) Report();
            // A bounded capture can also end in the middle of a credential.
            var capturedText = buffer.ToString();
            if (buffer.Length == 8 * 1024 * 1024)
                capturedText = capturedText[..(capturedText.LastIndexOf('\n') + 1)];
            return capturedText;
        }
        var stdout = ReadAsync(process.StandardOutput, false);
        var stderr = ReadAsync(process.StandardError, true);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException)
        {
            // Exit won the race: let the caller verify the successful result instead of reporting cancellation.
            if (!process.HasExited)
            {
                var stopped = false;
                try { process.Kill(entireProcessTree: true); stopped = true; }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Keep the operation lease until exit even if Windows refuses termination.
                    try { output?.Invoke("Windows could not stop the process. Waiting for it to finish."); } catch (Exception) { }
                }
                await process.WaitForExitAsync();
                await Task.WhenAll(stdout, stderr);
                if (stopped) throw;
            }
        }
        var captured = await Task.WhenAll(stdout, stderr);
        if (notifications > 2000) { try { output?.Invoke("Further live output omitted; the operation continued normally."); } catch (Exception) { } }
        return new(process.ExitCode, SensitiveText.Redact(captured[0], secret), SensitiveText.Redact(captured[1], secret), truncated != 0);
    }
}
