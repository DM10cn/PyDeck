using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PimGui.Core;

public sealed record NetworkSettings(string Mode = "System", string Address = "", string Username = "")
{
    public NetworkSettings Validate()
    {
        if (Mode is not ("System" or "Direct" or "Custom")) throw new ArgumentException("Invalid proxy mode");
        if (Username is null || Username.Any(char.IsControl)) throw new ArgumentException("Invalid proxy username");
        if (Mode == "Custom" && (!Uri.TryCreate(Address, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            uri.UserInfo.Length > 0 || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || string.IsNullOrEmpty(uri.Host)))
            throw new ArgumentException("Enter an HTTP proxy address without credentials, for example http://localhost:7890");
        return this;
    }
    public Dictionary<string, string?> Environment(string? password)
    {
        Validate();
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (Mode == "System") return env;
        foreach (var key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY" }) env[key] = null;
        if (Mode == "Direct") env["NO_PROXY"] = "*";
        if (Mode == "Custom")
        {
            var uri = new UriBuilder(Address);
            if (Username.Length > 0) { uri.UserName = Uri.EscapeDataString(Username); uri.Password = Uri.EscapeDataString(password ?? ""); }
            env["HTTP_PROXY"] = env["HTTPS_PROXY"] = uri.Uri.AbsoluteUri;
        }
        return env;
    }
    public HttpClient CreateClient(string? password)
    {
        Validate();
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseProxy = Mode != "Direct" };
        if (Mode == "Custom") handler.Proxy = new WebProxy(Address) { Credentials = Username.Length > 0 ? new NetworkCredential(Username, password) : null };
        // Default mode preserves the environment and Windows proxy configuration, as PIM does.
        else if (Mode == "System") handler.Proxy = HttpClient.DefaultProxy;
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }
}

public static partial class SensitiveText
{
    public static string Redact(string text, string? secret = null)
    {
        if (!string.IsNullOrEmpty(secret))
        {
            text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
            text = text.Replace(Uri.EscapeDataString(secret), "[redacted]", StringComparison.OrdinalIgnoreCase);
        }
        return Credentials().Replace(text, "$1[redacted]@");
    }
    [GeneratedRegex(@"(https?://)[^\s/@]+:[^\s/@]*@", RegexOptions.IgnoreCase)]
    private static partial Regex Credentials();
}

/// <summary>Generic credential owned by PyDeck; no password is serialized in settings.</summary>
public static class ProxyCredential
{
    private const string Target = "PyDeck/HTTPProxy";
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type; public string? TargetName, Comment; public long LastWritten;
        public uint CredentialBlobSize; public IntPtr CredentialBlob; public uint Persist, AttributeCount;
        public IntPtr Attributes; public string? TargetAlias, UserName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Write(ref Credential value, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Read(string target, uint type, uint flags, out IntPtr value);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Delete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr value);
    public static string? Load(string address, string username)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!Read(Target, 1, 0, out var pointer))
        {
            if (Marshal.GetLastWin32Error() == 1168) return null;
            throw new IOException("Windows Credential Manager could not be read");
        }
        try
        {
            var value = Marshal.PtrToStructure<Credential>(pointer);
            return value.UserName == username && value.Comment == address
                ? Marshal.PtrToStringUni(value.CredentialBlob, (int)value.CredentialBlobSize / 2) : null;
        }
        finally { CredFree(pointer); }
    }
    public static void Save(string address, string username, string password)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (password.Length > 1200 || password.Any(char.IsControl)) throw new ArgumentException("Invalid proxy password");
        if (password.Length == 0) { if (!Delete(Target, 1, 0) && Marshal.GetLastWin32Error() != 1168) throw new IOException("Windows Credential Manager could not be updated"); return; }
        var pointer = Marshal.StringToCoTaskMemUni(password);
        try
        {
            var value = new Credential { Type = 1, TargetName = Target, Comment = address, UserName = username,
                CredentialBlobSize = (uint)Encoding.Unicode.GetByteCount(password), CredentialBlob = pointer, Persist = 2 };
            if (!Write(ref value, 0)) throw new IOException("Windows Credential Manager could not be updated");
        }
        finally { Marshal.ZeroFreeCoTaskMemUnicode(pointer); }
    }
}
