using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace SmartBudgetML.Services.Security;

/// <summary>
/// Master key store using MAUI SecureStorage, protected by the Android Keystore when the device allows it.
/// There is no fallback: if SecureStorage fails, the exception reaches DatabaseService, which reports
/// <see cref="VaultState.KeyUnavailable"/>. The key is never written anywhere else.
/// Labeled NON-VÉRIFIÉ-APPAREIL for physical Android Keystore execution.
/// </summary>
public class MauiSecureKeyStore : ISecureKeyStore
{
    private const string KeyAlias = "smartbudget_sqlcipher_master_key_v2";
    private const int KeyHexLength = 64;
    private readonly ILogger<MauiSecureKeyStore>? _logger;
    private string? _cachedKey;

    public MauiSecureKeyStore(ILogger<MauiSecureKeyStore>? logger = null)
    {
        _logger = logger;
    }

    public async Task<string?> GetMasterKeyAsync()
    {
        if (!string.IsNullOrWhiteSpace(_cachedKey))
        {
            return _cachedKey;
        }

        var existing = await SecureStorage.Default.GetAsync(KeyAlias);
        if (string.IsNullOrWhiteSpace(existing) || existing.Length < KeyHexLength)
        {
            return null;
        }

        _cachedKey = existing;
        return existing;
    }

    public async Task<string> CreateMasterKeyAsync()
    {
        var generatedHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await SecureStorage.Default.SetAsync(KeyAlias, generatedHex);
        _cachedKey = generatedHex;
        return generatedHex;
    }

    public async Task<bool> HasMasterKeyAsync()
    {
        return await GetMasterKeyAsync() != null;
    }

    public Task PurgeMasterKeyAsync()
    {
        _cachedKey = null;
        try
        {
            SecureStorage.Default.Remove(KeyAlias);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SecureStorage remove failed during key purge.");
        }

        return Task.CompletedTask;
    }
}
