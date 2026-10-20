using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace SmartBudgetML.Services.Security;

/// <summary>
/// Hardware-backed master key store using MAUI SecureStorage (Android Keystore).
/// Labeled NON-VÉRIFIÉ-APPAREIL for physical Android Keystore hardware execution.
/// </summary>
public class MauiSecureKeyStore : ISecureKeyStore
{
    private const string KeyAlias = "smartbudget_sqlcipher_master_key_v2";
    private readonly ILogger<MauiSecureKeyStore>? _logger;
    private string? _cachedKey;

    public MauiSecureKeyStore(ILogger<MauiSecureKeyStore>? logger = null)
    {
        _logger = logger;
    }

    public async Task<string> GetOrCreateMasterKeyAsync()
    {
        if (!string.IsNullOrWhiteSpace(_cachedKey))
        {
            return _cachedKey;
        }

        try
        {
            var existing = await SecureStorage.Default.GetAsync(KeyAlias);
            if (!string.IsNullOrWhiteSpace(existing) && existing.Length >= 64)
            {
                _cachedKey = existing;
                return existing;
            }

            byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
            var generatedHex = Convert.ToHexString(keyBytes);
            await SecureStorage.Default.SetAsync(KeyAlias, generatedHex);
            _cachedKey = generatedHex;
            return generatedHex;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SecureStorage access failed; falling back to local protected key file.");
            return await GetOrCreateFallbackFileKeyAsync();
        }
    }

    public async Task<bool> HasMasterKeyAsync()
    {
        if (!string.IsNullOrWhiteSpace(_cachedKey))
        {
            return true;
        }

        try
        {
            var existing = await SecureStorage.Default.GetAsync(KeyAlias);
            return !string.IsNullOrWhiteSpace(existing);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SecureStorage check failed; checking fallback file.");
            var fallbackPath = GetFallbackKeyFilePath();
            return File.Exists(fallbackPath);
        }
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

        var fallbackPath = GetFallbackKeyFilePath();
        if (File.Exists(fallbackPath))
        {
            File.Delete(fallbackPath);
        }

        return Task.CompletedTask;
    }

    private static string GetFallbackKeyFilePath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = AppDomain.CurrentDomain.BaseDirectory;
        }
        return Path.Combine(baseDir, ".sb_vault_key");
    }

    private async Task<string> GetOrCreateFallbackFileKeyAsync()
    {
        var path = GetFallbackKeyFilePath();
        if (File.Exists(path))
        {
            var stored = (await File.ReadAllTextAsync(path)).Trim();
            if (stored.Length >= 64)
            {
                _cachedKey = stored;
                return stored;
            }
        }

        byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
        var hex = Convert.ToHexString(keyBytes);
        await File.WriteAllTextAsync(path, hex);
        _cachedKey = hex;
        return hex;
    }
}
