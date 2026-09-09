using System.Net;
using System.Net.NetworkInformation;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;

namespace DataPilot.Api.Services;

public static class HttpsCertificate
{
    public const string Organization = "深圳市纷享科技有限公司";

    public static string DirectoryPath(IConfiguration config) => Path.GetFullPath(
        config["certificate-directory"] ?? config["DataPilot:Https:CertificateDirectory"] ??
        Path.Combine(AppContext.BaseDirectory, "..", "DataPilot-data", "certificates"));

    public static X509Certificate2 LoadOrCreate(string directory, string? hosts = null)
    {
        directory = Path.GetFullPath(directory);
        if (!string.Equals(Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar)), "certificates", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use a dedicated directory named 'certificates' for private certificate files.");
        var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot")) + Path.DirectorySeparatorChar;
        if (directory.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Private certificates must be outside wwwroot.");
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Certificate directory must not be a symbolic link.");
        if (Directory.EnumerateFileSystemEntries(directory).Any(p => Path.GetFileName(p) is not ("server.pfx" or "server.crt")))
            throw new InvalidOperationException("Certificate directory contains unrelated files; choose a dedicated certificates directory.");
        if (Directory.EnumerateFileSystemEntries(directory).Any(p => (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0))
            throw new InvalidOperationException("Certificate files must not be symbolic links.");
        RestrictDirectory(directory);
        var pfx = Path.Combine(directory, "server.pfx");
        if (!File.Exists(pfx))
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "::1", Dns.GetHostName() };
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                    if (!address.Address.IsIPv6LinkLocal) names.Add(address.Address.ToString());
            foreach (var host in (hosts ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) names.Add(host);
            var san = new SubjectAlternativeNameBuilder();
            foreach (var name in names)
                if (IPAddress.TryParse(name, out var ip)) san.AddIpAddress(ip);
                else if (Uri.CheckHostName(name) == UriHostNameType.Dns) san.AddDnsName(name);
                else throw new ArgumentException("Invalid certificate host: " + name);
            using var rsa = RSA.Create(3072);
            var request = new CertificateRequest(new X500DistinguishedName("CN=DataPilot, O=" + Organization), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            var start = DateTimeOffset.UtcNow.AddMinutes(-5);
            using var cert = request.CreateSelfSigned(start, start.AddYears(10));
            // No embedded password: the private PFX is protected by the directory ACL / Unix mode.
            // CreateNew prevents accidental overwrite or concurrent certificate replacement.
            using (var stream = new FileStream(pfx, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.Write(cert.Export(X509ContentType.Pfx));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(pfx, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.WriteAllText(Path.Combine(directory, "server.crt"), cert.ExportCertificatePem());
            Console.WriteLine("Created 10-year self-signed HTTPS certificate: " + directory);
            Console.WriteLine("Trust server.crt on client devices; NEVER distribute server.pfx.");
        }
        if ((File.GetAttributes(pfx) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Certificate must not be a symbolic link.");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(pfx, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        // Windows Schannel needs a user key container for server TLS; Unix supports ephemeral keys.
        var loaded = new X509Certificate2(pfx, (string?)null,
            OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
        if (!loaded.HasPrivateKey || loaded.NotAfter.ToUniversalTime() <= DateTime.UtcNow || loaded.NotBefore.ToUniversalTime() > DateTime.UtcNow)
        { loaded.Dispose(); throw new InvalidOperationException("HTTPS certificate is expired or missing its private key."); }
        Console.WriteLine("Using HTTPS certificate: " + pfx + "; existing certificates are never overwritten.");
        return loaded;
    }

    private static void RestrictDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            var acl = new DirectorySecurity();
            acl.SetAccessRuleProtection(true, false);
            foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(acl);
        }
        else File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
