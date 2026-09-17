using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace PeopleCore.Infrastructure.DataProtection;

/// <summary>
/// Encrypts each key's secret element with AES-GCM before the key ring is written to the database.
/// The output names <see cref="AesGcmXmlDecryptor"/>, which the framework instantiates to read it back.
/// </summary>
public sealed class AesGcmXmlEncryptor : IXmlEncryptor
{
    internal const string ElementName = "aesGcmEncryptedSecret";
    internal const int NonceSizeInBytes = 12;
    internal const int TagSizeInBytes = 16;

    private readonly KeyRingEncryptionKey _key;

    public AesGcmXmlEncryptor(KeyRingEncryptionKey key) => _key = key;

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        ArgumentNullException.ThrowIfNull(plaintextElement);

        var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeInBytes);
        var tag = new byte[TagSizeInBytes];
        var ciphertext = new byte[plaintext.Length];

        try
        {
            using var aes = new AesGcm(_key.Bytes, TagSizeInBytes);
            aes.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var element = new XElement(ElementName,
            new XComment(" This key is encrypted with DataProtection:KeyEncryptionKey. "),
            new XElement("nonce", Convert.ToBase64String(nonce)),
            new XElement("tag", Convert.ToBase64String(tag)),
            new XElement("ciphertext", Convert.ToBase64String(ciphertext)));

        return new EncryptedXmlInfo(element, typeof(AesGcmXmlDecryptor));
    }
}
