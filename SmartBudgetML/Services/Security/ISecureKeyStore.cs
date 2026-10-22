namespace SmartBudgetML.Services.Security;

/// <summary>
/// Abstraction over hardware-backed key storage (Android Keystore via MAUI SecureStorage on device,
/// or isolated in-memory store in xUnit host tests).
/// Never derives the database key from the user's PIN, and never stores the key anywhere else.
/// </summary>
public interface ISecureKeyStore
{
    /// <summary>Returns the stored master key, or null when none is stored. Throws when the secure storage cannot be read.</summary>
    Task<string?> GetMasterKeyAsync();

    /// <summary>Creates and stores a new master key. Throws when the secure storage cannot be written.</summary>
    Task<string> CreateMasterKeyAsync();

    Task<bool> HasMasterKeyAsync();
    Task PurgeMasterKeyAsync();
}
