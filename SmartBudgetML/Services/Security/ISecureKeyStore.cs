namespace SmartBudgetML.Services.Security;

/// <summary>
/// Abstraction over hardware-backed key storage (Android Keystore via MAUI SecureStorage on device,
/// or isolated in-memory/file store in xUnit host tests).
/// Never derives the database key from the user's PIN.
/// </summary>
public interface ISecureKeyStore
{
    Task<string> GetOrCreateMasterKeyAsync();
    Task<bool> HasMasterKeyAsync();
    Task PurgeMasterKeyAsync();
}
