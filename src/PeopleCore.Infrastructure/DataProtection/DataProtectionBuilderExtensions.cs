using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace PeopleCore.Infrastructure.DataProtection;

public static class DataProtectionBuilderExtensions
{
    /// <summary>
    /// Encrypts every new key with <paramref name="key"/> before it is persisted. Keys written before
    /// this was configured carry no encrypted secret and still load as they are.
    /// </summary>
    public static IDataProtectionBuilder ProtectKeysWithKeyRingEncryptionKey(
        this IDataProtectionBuilder builder, KeyRingEncryptionKey key)
    {
        // The decryptor is built by the framework and looks the key up here.
        builder.Services.AddSingleton(key);
        builder.Services.Configure<KeyManagementOptions>(options =>
            options.XmlEncryptor = new AesGcmXmlEncryptor(key));
        return builder;
    }
}
