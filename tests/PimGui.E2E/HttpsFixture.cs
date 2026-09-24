using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;

// This assembly refuses to start outside the disposable Windows Sandbox.
sealed class HttpsFixture : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly X509Certificate2 certificate;
    private readonly Task serving;
    private readonly string archive;
    public JsonObject Metadata { get; }
    public string Index { get; }
    public HttpsFixture(string bundle)
    {
        if (!File.Exists(@"C:\PyDeckE2E\ISOLATED") || File.ReadAllText(@"C:\PyDeckE2E\ISOLATED").Trim() != Environment.MachineName) throw new IOException("Sandbox required");
        Metadata = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(bundle, "index.json")))!["versions"]![0]!.DeepClone();
        archive = Path.Combine(bundle, Metadata["url"]!.GetValue<string>());
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
        var eku = new OidCollection { new("1.3.6.1.5.5.7.3.1") }; request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, false));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        // Windows Schannel cannot serve TLS with the ephemeral key from CreateSelfSigned.
        // The imported key is scoped to this disposable Sandbox, never the host.
        certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pkcs12), null, X509KeyStorageFlags.MachineKeySet);
        using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine)) { store.Open(OpenFlags.ReadWrite); store.Add(certificate); }
        listener.Start(); Index = $"https://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}/index.json";
        Metadata["id"] = "pydeck-e2e-3.14-64"; Metadata["company"] = "PyDeckE2E"; Metadata["tag"] = "3.14-64";
        Metadata["url"] = new Uri(new Uri(Index), "package.zip").AbsoluteUri;
        Metadata.Remove("alias"); Metadata.Remove("shortcuts");
        serving = ServeAsync();
    }
    private async Task ServeAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                using var tls = new SslStream(client.GetStream(), false);
                await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, stop.Token);
                using var reader = new StreamReader(tls, Encoding.ASCII, leaveOpen: true);
                var first = await reader.ReadLineAsync(stop.Token) ?? "";
                for (var lines = 0; lines < 100; lines++) { if (string.IsNullOrEmpty(await reader.ReadLineAsync(stop.Token))) break; }
                var route = first.Split(' ').ElementAtOrDefault(1);
                if (route == "/package.zip")
                {
                    await using var stream = File.OpenRead(archive);
                    await tls.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {stream.Length}\r\nConnection: close\r\n\r\n"), stop.Token);
                    await stream.CopyToAsync(tls, stop.Token);
                }
                else
                {
                    var bytes = Encoding.UTF8.GetBytes(new JsonObject { ["versions"] = new JsonArray(Metadata.DeepClone()) }.ToJsonString());
                    var status = route == "/index.json" ? "200 OK" : "404 Not Found";
                    await tls.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n"), stop.Token);
                    await tls.WriteAsync(bytes, stop.Token);
                }
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException) { Console.WriteLine("HTTPS fixture: " + ex.Message); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (SocketException) when (stop.IsCancellationRequested) { break; }
        }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop(); await serving;
        using (var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine)) { store.Open(OpenFlags.ReadWrite); store.Remove(certificate); }
        certificate.Dispose(); stop.Dispose();
    }
}
