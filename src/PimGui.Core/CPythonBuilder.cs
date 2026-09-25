using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;

namespace PimGui.Core;

public sealed class CPythonBuilder(BuildStore store, NetworkSettings? network = null, string? proxyPassword = null)
{
    public async Task<BuildRecord> BuildAsync(BuildOptions options, BuildToolchain tools, PimOperation operation,
        Action<BuildRecord>? changed = null, Action<string>? output = null)
    {
        options.Validate(); tools.Validate();
        using var lease = store.AcquireLease();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
        deadline.CancelAfter(TimeSpan.FromHours(4));
        var token = deadline.Token;
        var record = new BuildRecord(Guid.NewGuid().ToString("N"), options, BuildState.Preparing, DateTimeOffset.UtcNow, Toolchain: tools);
        var work = store.JobDirectory(record.Id); var destination = store.RuntimeDirectory(record);
        BuildRecipe.BatchPath(work); BuildRecipe.BatchPath(destination);
        store.Save(record);
        using var log = new StreamWriter(new FileStream(store.LogPath(record.Id), FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        var logGate = new object(); long logCharacters = 0; var logLimitReported = false;
        void Write(string line)
        {
            line = SensitiveText.Redact(line, proxyPassword);
            lock (logGate)
            {
                if (logCharacters > 128 * 1024 * 1024)
                {
                    if (!logLimitReported) { log.WriteLine("[Build log limit reached: 128 MiB of text]"); logLimitReported = true; }
                    return;
                }
                logCharacters += line.Length; log.WriteLine(line);
            }
            try { output?.Invoke(line); } catch { }
        }
        void Stage(BuildState state, OperationPhase phase)
        {
            token.ThrowIfCancellationRequested(); record = record with { State = state }; store.Save(record);
            Write("=== " + state + " ==="); operation.Report(phase);
            try { changed?.Invoke(record); } catch { }
        }
        try
        {
            Write("CPython " + options.Version + " | x64 " + options.Configuration + " | " + record.Id);
            Write("Source: " + record.SourceUrl);
            Write("MSVC " + tools.CompilerVersion + " (" + tools.PlatformToolset + ") | SDK " + tools.SdkVersion);
            Write("Options: Tcl/Tk=" + options.IncludeTk + ", tests=" + options.IncludeTests + ", symbols=" + options.IncludeSymbols);
            Stage(options.SourceArchive.Length == 0 ? BuildState.Downloading : BuildState.Preparing,
                options.SourceArchive.Length == 0 ? OperationPhase.Downloading : OperationPhase.Preparing);
            using var http = (network ?? new()).CreateClient(proxyPassword);
            var publishedHash = options.SourceArchive.Length == 0 ? await BuildSources.PublishedHashAsync(http, options.Version, token) : null;
            var archive = options.SourceArchive.Length == 0
                ? await DownloadSourceAsync(options.Version, publishedHash, operation, token)
                : options.SourceArchive;
            operation.Report(OperationPhase.Verifying);
            // Copy imports first: the build never reads a changing archive or writes to the user's source.
            if (options.SourceArchive.Length > 0)
            {
                SafeFiles.RequireNoLinks(archive);
                if (new FileInfo(archive).Length > 64 * 1024 * 1024) throw new IOException("Source archive exceeds 64 MiB");
                var snapshot = Path.Combine(work, "import.tgz");
                File.Copy(archive, snapshot, false); archive = snapshot;
            }
            await using (var stream = File.OpenRead(archive))
                record = record with { SourceSha256 = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, token)),
                    SourceVerification = publishedHash is not null ? "Published SHA-256 over HTTPS; signature not verified" : options.SourceArchive.Length > 0 ? "Local archive fingerprint" : "HTTPS download fingerprint; no published digest" };
            store.Save(record); Write(record.SourceVerification + "\nSHA-256: " + record.SourceSha256);
            if (publishedHash is not null) await BuildArchive.VerifyAsync(archive, publishedHash, token);
            Stage(BuildState.Extracting, OperationPhase.Extracting);
            var unpack = Path.Combine(work, "source");
            await BuildArchive.ExtractAsync(archive, unpack, token, version: options.Version);
            var source = Path.Combine(unpack, "Python-" + options.Version);
            BuildSources.CheckCompatibility(source, options);
            var temp = Path.Combine(work, "temp"); Directory.CreateDirectory(temp);
            var env = new Dictionary<string, string?>((network ?? new()).Environment(proxyPassword), StringComparer.OrdinalIgnoreCase)
            {
                ["MSBUILD"] = "\"" + tools.MSBuild + "\"", ["PYTHON_FOR_BUILD"] = tools.BootstrapPython,
                ["EXTERNALS_DIR"] = Path.Combine(source, "externals"), ["TEMP"] = temp, ["TMP"] = temp,
                ["GIT_CEILING_DIRECTORIES"] = work,
                ["PYTHON_MANAGER_AUTOMATIC_INSTALL"] = "false"
            };
            Stage(BuildState.Compiling, OperationPhase.Compiling);
            var arguments = BuildRecipe.CompilerArguments(options, tools);
            Write("PCbuild\\build.bat " + arguments);
            await BuildProcess.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), null,
                "/d /s /c \"\"" + Path.Combine(source, "PCbuild", "build.bat") + "\" " + arguments + "\"", source, env, Write, token);
            if (options.Pgo)
            {
                // Separate phases so a failed training run cannot be hidden by a successful PGUpdate.
                Stage(BuildState.Training, OperationPhase.Training); Write("=== PGO training ===");
                var trainingPython = Path.Combine(source, "PCbuild", "amd64", "instrumented", "python.exe");
                if (!File.Exists(trainingPython)) trainingPython = Path.Combine(source, "PCbuild", "amd64", "python.exe");
                if (source.Contains(' ')) Write("PGO workload excludes test_tabnanny: its expected path quoting fails for source paths containing spaces");
                if (!options.IncludeCtypes) Write("ctypes is built for PGO training and will be omitted from the final runtime");
                await BuildProcess.RunAsync(trainingPython, BuildRecipe.TrainingArguments(source), null, source,
                    new Dictionary<string, string?>(env) { ["PYTHONHOME"] = source }, Write, token);
                Stage(BuildState.Compiling, OperationPhase.Compiling); Write("=== PGO optimized build ===");
                await BuildProcess.RunAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), null,
                    "/d /s /c \"\"" + Path.Combine(source, "PCbuild", "build.bat") + "\" " + BuildRecipe.CompilerArguments(options, tools, "PGUpdate") + "\"", source, env, Write, token);
            }
            Stage(BuildState.Assembling, OperationPhase.Assembling);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Runtime destination already exists");
            Directory.CreateDirectory(destination);
            await File.WriteAllTextAsync(Path.Combine(destination, ".pydeck-build-id"), record.Id, token);
            await BuildProcess.RunAsync(Path.Combine(source, "PCbuild", "amd64", options.ExecutableName),
                BuildRecipe.LayoutArguments(options, source, destination, Path.Combine(temp, "layout")), null, source, env, Write, token);
            // PC/layout's Debug filter excludes unsuffixed Tcl/Tk DLLs although _tkinter_d depends on them.
            if (options.Configuration == "Debug" && options.IncludeTk)
                foreach (var pattern in new[] { "tcl*.dll", "tk*.dll", "libtommath.dll", "zlib1.dll" })
                    foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "PCbuild", "amd64"), pattern))
                    {
                        VerifyX64(file);
                        var target = Path.Combine(destination, "DLLs", Path.GetFileName(file));
                        if (!File.Exists(target)) File.Copy(file, target, overwrite: false);
                    }
            if (!options.IncludeSqlite)
                foreach (var pattern in new[] { "_sqlite3*.pyd", "sqlite3*.dll" })
                    foreach (var file in Directory.EnumerateFiles(destination, pattern, SearchOption.AllDirectories)) File.Delete(file);
            if (!options.IncludeCtypes)
                foreach (var pattern in new[] { "_ctypes*.pyd", "libffi*.dll" })
                    foreach (var file in Directory.EnumerateFiles(destination, pattern, SearchOption.AllDirectories)) File.Delete(file);
            // Remove the source from its original location before probing. A source-bound executable must fail acceptance.
            Directory.Move(unpack, Path.Combine(work, "source-detached"));
            Stage(BuildState.Validating, OperationPhase.Testing);
            var python = Path.Combine(destination, options.ExecutableName);
            VerifyX64(python);
            var verification = Path.Combine(work, "verification"); Directory.CreateDirectory(verification);
            // Do not carry compiler discovery variables into the final runtime checks.
            var runtimeEnv = new Dictionary<string, string?> { ["TEMP"] = temp, ["TMP"] = temp };
            var modules = "import encodings,venv,ensurepip,zlib,bz2,lzma,hashlib,socket; " +
                (options.IncludeSsl ? "import ssl; " : "") + (options.IncludeSqlite ? "import sqlite3; " : "") +
                (options.IncludeCtypes ? "import ctypes; " : "") +
                (options.IncludeTk ? "import tkinter; tkinter.Tcl().eval('info patchlevel'); " : "") +
                (options.IncludeTests ? "import test.support; " : "");
            var identity = "import sys,os,platform; assert platform.machine().lower() in ('amd64','x86_64'); assert sys.maxsize>2**32; " +
                "assert platform.python_version()=='" + options.Version + "'; assert os.path.samefile(sys.prefix,sys.argv[1]); assert " +
                (options.Configuration == "Debug" ? "" : "not ") + "hasattr(sys,'gettotalrefcount'); ";
            await BuildProcess.RunAsync(python, ["-I", "-c", identity + modules + "print('PYDECK_RUNTIME_OK')", destination], null, verification, runtimeEnv, Write, token);
            if (options.IncludePip)
            {
                await BuildProcess.RunAsync(python, ["-I", "-m", "ensurepip", "--default-pip"], null, verification, runtimeEnv, Write, token);
                await BuildProcess.RunAsync(python, ["-I", "-m", "pip", "--version"], null, verification, runtimeEnv, Write, token);
            }
            var venv = Path.Combine(verification, "venv");
            await BuildProcess.RunAsync(python, options.IncludePip ? ["-I", "-m", "venv", venv] : ["-I", "-m", "venv", "--without-pip", venv], null, verification, runtimeEnv, Write, token);
            await BuildProcess.RunAsync(new VirtualEnvironment(venv).Executable, ["-I", "-c",
                "import sys,os; " + (options.IncludePip ? "import pip; " : "") + "assert os.path.samefile(sys.prefix,sys.argv[1]); assert os.path.samefile(sys.base_prefix,sys.argv[2]); print('PYDECK_VENV_OK')", venv, destination], null, verification, runtimeEnv, Write, token);
            token.ThrowIfCancellationRequested();
            record = record with { State = BuildState.Ready, Finished = DateTimeOffset.UtcNow };
            Write("Runtime verified outside the source tree: " + destination);
        }
        catch (Exception ex)
        {
            record = record with { State = operation.IsCancellationRequested ? BuildState.Cancelled : BuildState.Failed,
                Finished = DateTimeOffset.UtcNow, Error = SensitiveText.Redact(deadline.IsCancellationRequested && !operation.IsCancellationRequested
                    ? "Build exceeded the four-hour limit" : ex.Message, proxyPassword) };
            Write(record.State + ": " + record.Error);
        }
        store.Save(record);
        try { changed?.Invoke(record); } catch { }
        return record;
    }

    private async Task<string> DownloadSourceAsync(string version, string? hash, PimOperation operation, CancellationToken token)
    {
        var cache = Path.Combine(store.Root, "Cache"); SafeFiles.RequireNoLinks(cache); Directory.CreateDirectory(cache);
        var path = Path.Combine(cache, (hash ?? version + "-" + Guid.NewGuid().ToString("N")) + ".tgz"); SafeFiles.RequireNoLinks(path);
        if (hash is not null && File.Exists(path))
        {
            try { await BuildArchive.VerifyAsync(path, hash, token); return path; }
            catch (IOException) { File.Move(path, path + ".invalid-" + Guid.NewGuid().ToString("N")); }
        }
        using var client = (network ?? new()).CreateClient(proxyPassword);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(15));
        using var response = await client.GetAsync(BuildSources.Url(version), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        const long maximum = 64 * 1024 * 1024;
        var length = response.Content.Headers.ContentLength;
        if (length > maximum) throw new IOException("Source download exceeds the size limit");
        var partial = path + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token))
            {
                var buffer = new byte[65536]; long count = 0; var clock = Stopwatch.StartNew();
                while (await input.ReadAsync(buffer, timeout.Token) is var n && n > 0)
                {
                    count += n; if (count > maximum) throw new IOException("Source download exceeds the size limit");
                    await output.WriteAsync(buffer.AsMemory(0, n), timeout.Token);
                    var speed = count / Math.Max(0.001, clock.Elapsed.TotalSeconds);
                    operation.Transfer(count, length, speed, length is { } total ? TimeSpan.FromSeconds(Math.Max(0, total - count) / speed) : null);
                }
            }
            if (hash is not null) await BuildArchive.VerifyAsync(partial, hash, timeout.Token);
            File.Move(partial, path); return path;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    public static void VerifyX64(string executable)
    {
        SafeFiles.RequireNoLinks(executable);
        using var stream = File.OpenRead(executable); using var pe = new PEReader(stream);
        if (pe.PEHeaders.CoffHeader.Machine != Machine.Amd64) throw new IOException("The built runtime is not x64");
    }
}
