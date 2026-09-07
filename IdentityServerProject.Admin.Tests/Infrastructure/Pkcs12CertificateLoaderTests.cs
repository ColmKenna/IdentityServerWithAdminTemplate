using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using IdentityServerProject.Configuration;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
///     The signing certificate and the Data Protection key-wrapping certificate are both
///     loaded through this one call, on whatever platform the host happens to run. Asking
///     for a key storage flag the platform rejects throws before either configuration guard
///     runs, so the host dies with a platform error instead of the message naming the
///     setting the operator got wrong. These load a real PKCS#12 file rather than asserting
///     which flag was chosen — the flag is only interesting insofar as the load works.
/// </summary>
public sealed class Pkcs12CertificateLoaderTests : IDisposable
{
    private const string Password = "pkcs12-certificate-loader-tests";
    private readonly string _certificatePath;

    public Pkcs12CertificateLoaderTests()
    {
        _certificatePath = Path.Combine(
            Path.GetTempPath(), $"identityserver-loader-tests-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(_certificatePath, CreateSelfSignedPkcs12(Password));
    }

    public void Dispose()
    {
        if (File.Exists(_certificatePath)) File.Delete(_certificatePath);
    }

    [Fact]
    public void LoadFromFile_OnTheRunningPlatform_ReturnsACertificateWithItsPrivateKey()
    {
        using X509Certificate2 certificate = Pkcs12CertificateLoader.LoadFromFile(_certificatePath, Password);

        Assert.Equal("CN=identityserver-loader-tests", certificate.Subject);

        // A signing credential and ProtectKeysWithCertificate both need the private key, so
        // "it loaded" is not enough — the key has to have come across with it.
        Assert.True(certificate.HasPrivateKey);
    }

    [Fact]
    public void LoadFromFile_WithTheWrongPassword_ThrowsCryptographicException()
    {
        // Program.cs translates this one into an InvalidOperationException naming the
        // setting. That mapping only holds while a bad password lands here as a
        // CryptographicException rather than as something the catch filter lets past.
        Assert.Throws<CryptographicException>(
            () => Pkcs12CertificateLoader.LoadFromFile(_certificatePath, "not-the-password"));
    }

    private static byte[] CreateSelfSignedPkcs12(string password)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=identityserver-loader-tests", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return certificate.Export(X509ContentType.Pkcs12, password);
    }
}
