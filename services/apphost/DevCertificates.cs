using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// Local HTTPS for Keycloak and the API, issued by a SmartRail development CA. Android will not
// trust the ASP.NET Core development certificate because it is not a CA; it does accept a real
// CA installed on the device, and so does every browser and the API once the CA is trusted on
// this machine. The files live in the user's local app data, never in the repository.
static class DevCertificates
{
    private const string CaSubject = "CN=SmartRail Development CA";

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartRail", "dev-certs");

    // The public CA certificate to trust on this machine and to install on a test device.
    public static string CaCertificatePath => Path.Combine(Folder, "smartrail-dev-ca.crt");

    // Holds the CA private key. Anyone with this file could issue certificates this machine
    // trusts, so it stays in the user's profile and is never shared or committed.
    private static string CaPfxPath => Path.Combine(Folder, "smartrail-dev-ca.pfx");

    private static string ServerPfxPath => Path.Combine(Folder, "localhost.pfx");

    // The localhost certificate, reused while it is valid for 30 more days and still issued by
    // the current CA; otherwise a new one is issued.
    public static X509Certificate2 LoadOrCreateServerCertificate()
    {
        Directory.CreateDirectory(Folder);
        var ca = LoadOrCreateCa();

        if (File.Exists(ServerPfxPath))
        {
            var existing = LoadPfx(ServerPfxPath);
            if (existing.NotAfter > DateTime.Now.AddDays(30) && IsIssuedBy(existing, ca))
            {
                return existing;
            }
        }

        using var server = IssueServerCertificate(ca);
        File.WriteAllBytes(ServerPfxPath, server.Export(X509ContentType.Pkcs12));
        return LoadPfx(ServerPfxPath);
    }

    private static X509Certificate2 LoadOrCreateCa()
    {
        if (File.Exists(CaPfxPath))
        {
            return LoadPfx(CaPfxPath);
        }

        using var key = RSA.Create(3072);
        var request = new CertificateRequest(CaSubject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var now = DateTimeOffset.UtcNow;
        using var ca = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(5));
        File.WriteAllBytes(CaPfxPath, ca.Export(X509ContentType.Pkcs12));
        File.WriteAllText(CaCertificatePath, ca.ExportCertificatePem());
        return LoadPfx(CaPfxPath);
    }

    private static X509Certificate2 IssueServerCertificate(X509Certificate2 ca)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1")], false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false));

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddDnsName("host.docker.internal");
        names.AddIpAddress(IPAddress.Loopback);
        names.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(names.Build());

        var now = DateTimeOffset.UtcNow;
        using var certificate = request.Create(ca, now.AddDays(-1), now.AddDays(397), RandomNumberGenerator.GetBytes(16));
        return certificate.CopyWithPrivateKey(key);
    }

    private static bool IsIssuedBy(X509Certificate2 certificate, X509Certificate2 ca)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(certificate);
    }

    private static X509Certificate2 LoadPfx(string path) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.Exportable);
}
