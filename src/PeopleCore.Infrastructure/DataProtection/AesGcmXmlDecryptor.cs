using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace PeopleCore.Infrastructure.DataProtection;

/// <summary>
/// Reads back what <see cref="AesGcmXmlEncryptor"/> wrote. The framework creates this from the type
/// name stored with the key, passing the service provider, so the key comes from DI rather than
/// from a constructor argument.
/// </summary>
/// <remarks>
/// Every key already in the database names this type - by namespace and class - as the decryptor
/// that reads it back. Renaming or moving this type makes every existing key unreadable: outstanding
/// password reset links stop working, and the stored SMTP password has to be re-entered. If this
/// type ever needs to move, leave a type with the old name behind that forwards to the new one.
/// </remarks>
public sealed class AesGcmXmlDecryptor : IXmlDecryptor
{
    private readonly KeyRingEncryptionKey _key;

    public AesGcmXmlDecryptor(IServiceProvider services) =>
        _key = services.GetRequiredService<KeyRingEncryptionKey>();

    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        try
        {
            var nonce = ReadBase64(encryptedElement, "nonce");
            var tag = ReadBase64(encryptedElement, "tag");
            var ciphertext = ReadBase64(encryptedElement, "ciphertext");

            if (nonce.Length != AesGcmXmlEncryptor.NonceSizeInBytes)
                throw new CryptographicException(
                    $"The stored nonce is {nonce.Length} bytes; expected {AesGcmXmlEncryptor.NonceSizeInBytes}.");
            if (tag.Length != AesGcmXmlEncryptor.TagSizeInBytes)
                throw new CryptographicException(
                    $"The stored tag is {tag.Length} bytes; expected {AesGcmXmlEncryptor.TagSizeInBytes}.");

            var plaintext = new byte[ciphertext.Length];
            try
            {
                // A different secret fails here, on the tag, rather than yielding garbage XML.
                using var aes = new AesGcm(_key.Bytes, AesGcmXmlEncryptor.TagSizeInBytes);
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
                return XElement.Parse(Encoding.UTF8.GetString(plaintext));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            // Bad base64 (FormatException) or an AesGcm call rejecting a mis-sized argument
            // (ArgumentException) both mean corrupted storage, not a caller bug. EmailSettingsStore
            // and the anonymous reset endpoints only catch CryptographicException, so this has to be
            // reported as one rather than escaping as something they don't handle.
            throw new CryptographicException("The stored key data is corrupt and could not be decrypted.", ex);
        }
    }

    private static byte[] ReadBase64(XElement element, string name) =>
        Convert.FromBase64String((string?)element.Element(name)
            ?? throw new CryptographicException($"The encrypted key is missing its <{name}> element."));
}
