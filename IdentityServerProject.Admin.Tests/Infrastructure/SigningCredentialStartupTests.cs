using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace IdentityServerProject.Admin.Tests.Infrastructure;

/// <summary>
///     Outside Development the host must refuse to start when no signing certificate is
///     configured, rather than falling back to a developer signing credential. A fallback
///     starts cleanly, serves discovery, and issues tokens from a key nobody chose — there
///     is no symptom to notice, which is exactly why it needs a test rather than a comment.
/// </summary>
public sealed class SigningCredentialStartupTests : IDisposable
{
    private const string CertificatePassword = "signing-credential-startup-tests";
    private readonly string _certificatePath;

    public SigningCredentialStartupTests()
    {
        _certificatePath = Path.Combine(
            Path.GetTempPath(), $"identityserver-signing-tests-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(_certificatePath, CreateSelfSignedPkcs12(CertificatePassword));
    }

    public void Dispose()
    {
        if (File.Exists(_certificatePath)) File.Delete(_certificatePath);
    }

    [Fact]
    public async Task OutsideDevelopment_WithoutASigningCertificate_StartupFailsAndNamesBothSettings()
    {
        using var baseFactory = new AdminWebFactory();
        using WebApplicationFactory<Program> factory =
            baseFactory.WithWebHostBuilder(ConfigureProduction(withSigningCertificate: false));

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
            await client.GetAsync("/Account/Login");
        });

        string report = exception.ToString();
        Assert.Contains("IdentityServer:SigningCertificatePath", report, StringComparison.Ordinal);
        Assert.Contains("IdentityServer:SigningCertificatePassword", report, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OutsideDevelopment_WithASigningCertificate_ClearsTheSigningCredentialGate()
    {
        using var baseFactory = new AdminWebFactory();
        using WebApplicationFactory<Program> factory =
            baseFactory.WithWebHostBuilder(ConfigureProduction(withSigningCertificate: true));

        Exception? exception = await Record.ExceptionAsync(async () =>
        {
            using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
            await client.GetAsync("/Account/Login");
        });

        // Asserts the gate is conditional, not that the whole Production host is healthy:
        // supplying the certificate must stop the startup failing for this reason.
        Assert.DoesNotContain(
            "SigningCertificatePath", exception?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    private Action<IWebHostBuilder> ConfigureProduction(bool withSigningCertificate)
    {
        var settings = new Dictionary<string, string?>
        {
            // Program.cs demands this pair outside Development as well, and checks it before
            // the signing credential, so it has to be satisfied to reach the gate under test.
            ["DataProtection:CertificatePath"] = _certificatePath,
            ["DataProtection:CertificatePassword"] = CertificatePassword
        };

        if (withSigningCertificate)
        {
            settings["IdentityServer:SigningCertificatePath"] = _certificatePath;
            settings["IdentityServer:SigningCertificatePassword"] = CertificatePassword;
        }

        return builder =>
        {
            // AdminWebFactory pins the environment to Testing; this runs after it and wins.
            builder.UseEnvironment("Production");

            // UseSetting, not ConfigureAppConfiguration: Program.cs reads these keys eagerly
            // off builder.Configuration while it runs, which is before ConfigureAppConfiguration
            // delegates are applied. Only host settings are in place that early.
            foreach ((string key, string? value) in settings) builder.UseSetting(key, value);
        };
    }

    private static byte[] CreateSelfSignedPkcs12(string password)
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=identityserver-signing-tests", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        return certificate.Export(X509ContentType.Pkcs12, password);
    }
}
