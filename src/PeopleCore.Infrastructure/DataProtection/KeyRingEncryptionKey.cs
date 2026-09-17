using System.Security.Cryptography;
using System.Text;

namespace PeopleCore.Infrastructure.DataProtection;

/// <summary>
/// The AES-256 key the data protection key ring is encrypted with before it is written to the
/// database. It comes from configuration, never from the database, so a copy of the database alone
/// is not enough to forge a password reset link or read the stored SMTP password.
/// </summary>
/// <remarks>
/// A singleton in DI because the framework builds <see cref="AesGcmXmlDecryptor"/> itself, from the
/// type name stored beside each key, and the only thing it hands over is the service provider.
/// </remarks>
public sealed class KeyRingEncryptionKey
{
    public const int KeySizeInBytes = 32;

    private readonly byte[] _key;

    private KeyRingEncryptionKey(byte[] key) => _key = key;

    /// <summary>
    /// SHA-256 of the secret's UTF-8 bytes, so any sufficiently long random string will do and
    /// nobody has to produce exactly 32 bytes of base64.
    /// </summary>
    public static KeyRingEncryptionKey FromSecret(string secret) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    internal ReadOnlySpan<byte> Bytes => _key;
}
