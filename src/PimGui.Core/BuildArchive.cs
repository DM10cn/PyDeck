using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace PimGui.Core;

public static class BuildArchive
{
    public static async Task VerifyAsync(string archive, string expectedSha256, CancellationToken token)
    {
        SafeFiles.RequireNoLinks(archive);
        await using var stream = File.OpenRead(archive);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("CPython source checksum does not match the pinned release");
    }

    public static async Task ExtractAsync(string archive, string destination, CancellationToken token,
        long maximumBytes = 1024L * 1024 * 1024, int maximumEntries = 50000, string version = BuildRecipe.Version)
    {
        destination = ExecutionPaths.LocalPath(destination); SafeFiles.RequireNoLinks(destination);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Source folder already exists");
        Directory.CreateDirectory(destination);
        await using var input = File.OpenRead(archive);
        await using var gzip = new GZipStream(input, CompressionMode.Decompress);
        await using var tar = new TarReader(gzip);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0; var count = 0;
        while (await tar.GetNextEntryAsync(cancellationToken: token) is { } entry)
        {
            token.ThrowIfCancellationRequested();
            if (++count > maximumEntries || entry.Length > maximumBytes - expanded) throw new IOException("Source archive exceeds extraction limits");
            expanded += entry.Length;
            if (entry.EntryType is not (TarEntryType.Directory or TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                throw new IOException("Source archive contains links or special files");
            var relative = entry.Name.Replace('\\', '/').TrimEnd('/');
            var segments = relative.Split('/');
            if (segments[0] != "Python-" + version || segments.Any(s => s.Length == 0 || s is "." or ".." ||
                s.EndsWith('.') || s.EndsWith(' ') || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                System.Text.RegularExpressions.Regex.IsMatch(s, @"\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|\z)", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                throw new IOException("Source archive contains an unsafe path");
            var target = Path.GetFullPath(Path.Combine(destination, Path.Combine(segments)));
            if (!target.StartsWith(destination.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !names.Add(target))
                throw new IOException("Source archive contains duplicate or escaping paths");
            SafeFiles.RequireNoLinks(target);
            if (entry.EntryType == TarEntryType.Directory) Directory.CreateDirectory(target);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    if (entry.DataStream is not null) await entry.DataStream.CopyToAsync(output, token);
                // PCbuild uses timestamp dependencies for generated release sources, including the SBOM.
                File.SetLastWriteTimeUtc(target, entry.ModificationTime.UtcDateTime);
            }
        }
        if (!File.Exists(Path.Combine(destination, "Python-" + version, "PCbuild", "build.bat")))
            throw new IOException("The archive does not contain the supported CPython source tree");
    }
}
