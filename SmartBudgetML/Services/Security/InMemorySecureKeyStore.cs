using System.Security.Cryptography;

namespace SmartBudgetML.Services.Security;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="ISecureKeyStore"/> for unit tests
/// and fallback environments. Generates a 32-byte (256-bit) CSPRNG key encoded as 64 hex characters.
/// </summary>
public class InMemorySecureKeyStore : ISecureKeyStore
{
    private string? _masterKeyHex;
    private readonly object _lock = new();

    public InMemorySecureKeyStore(string? initialKeyHex = null)
    {
        _masterKeyHex = initialKeyHex;
    }

    public Task<string> GetOrCreateMasterKeyAsync()
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(_masterKeyHex))
            {
                byte[] keyBytes = RandomNumberGenerator.GetBytes(32);
                _masterKeyHex = Convert.ToHexString(keyBytes);
            }
            return Task.FromResult(_masterKeyHex);
        }
    }

    public Task<bool> HasMasterKeyAsync()
    {
        lock (_lock)
        {
            return Task.FromResult(!string.IsNullOrWhiteSpace(_masterKeyHex));
        }
    }

    public Task PurgeMasterKeyAsync()
    {
        lock (_lock)
        {
            _masterKeyHex = null;
            return Task.CompletedTask;
        }
    }
}
