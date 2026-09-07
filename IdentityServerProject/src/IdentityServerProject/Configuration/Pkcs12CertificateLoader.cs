using System.Security.Cryptography.X509Certificates;

namespace IdentityServerProject.Configuration;

/// <summary>
///     Loads PKCS#12 certificates with the strongest key storage the running platform
///     actually supports.
///     <para>
///         <see cref="X509KeyStorageFlags.EphemeralKeySet" /> is the flag worth asking for:
///         it keeps the private key in memory instead of writing it into a key store, which
///         is what a signing key and a Data Protection key-wrapping key both want. macOS
///         rejects it outright with <see cref="PlatformNotSupportedException" />, so asking
///         for it unconditionally stops the host booting there — and because the certificate
///         load happens before the signing-credential guard, the failure surfaces as a
///         platform error that says nothing about the configuration the operator is trying
///         to get right. Fall back to the platform default rather than lose the diagnosis.
///     </para>
/// </summary>
public static class Pkcs12CertificateLoader
{
    /// <summary>
    ///     <see cref="X509KeyStorageFlags.EphemeralKeySet" /> everywhere it is supported;
    ///     <see cref="X509KeyStorageFlags.DefaultKeySet" /> on macOS, where the key is
    ///     materialised in a temporary keychain instead.
    /// </summary>
    public static X509KeyStorageFlags KeyStorageFlags =>
        OperatingSystem.IsMacOS()
            ? X509KeyStorageFlags.DefaultKeySet
            : X509KeyStorageFlags.EphemeralKeySet;

    public static X509Certificate2 LoadFromFile(string path, string password) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, password, KeyStorageFlags);
}
