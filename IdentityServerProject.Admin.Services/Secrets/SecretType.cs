namespace IdentityServerProject.Services.Secrets;

/// <summary>
///     Closed set of secret types Duende IdentityServer recognizes at token-endpoint authentication
///     time. Member names must match the framework's string constants exactly.
/// </summary>
public enum SecretType
{
    SharedSecret,
    X509CertificateBase64,
    X509Name,
    X509Thumbprint
}

public static class SecretTypeExtensions
{
    public static string ToSecretTypeValue(this SecretType type) => type switch
    {
        SecretType.SharedSecret => "SharedSecret",
        SecretType.X509CertificateBase64 => "X509CertificateBase64",
        SecretType.X509Name => "X509Name",
        SecretType.X509Thumbprint => "X509Thumbprint",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unrecognized secret type.")
    };

    public static SecretType ParseSecretType(this string value) => value switch
    {
        "SharedSecret" => SecretType.SharedSecret,
        "X509CertificateBase64" => SecretType.X509CertificateBase64,
        "X509Name" => SecretType.X509Name,
        "X509Thumbprint" => SecretType.X509Thumbprint,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unrecognized secret type.")
    };
}