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
public sealed class AesGcmXmlDecryptor : IXmlDecryptor
{
    private readonly KeyRingEncryptionKey _key;

    public AesGcmXmlDecryptor(IServiceProvider services) =>
        _key = services.GetRequiredService<KeyRingEncryptionKey>();

    public XElement Decrypt(XElement encryptedElement)
    {
        ArgumentNullException.ThrowIfNull(encryptedElement);

        var nonce = ReadBase64(encryptedElement, "nonce");
        var tag = ReadBase64(encryptedElement, "tag");
        var ciphertext = ReadBase64(encryptedElement, "ciphertext");
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

    private static byte[] ReadBase64(XElement element, string name) =>
        Convert.FromBase64String((string?)element.Element(name)
            ?? throw new CryptographicException($"The encrypted key is missing its <{name}> element."));
}
