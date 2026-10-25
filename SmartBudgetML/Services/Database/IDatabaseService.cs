using SQLite;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Security;

namespace SmartBudgetML.Services.Database;

/// <summary>
/// Interface for database lifecycle, table provisioning, entity repositories,
/// real file size inspection, password-encrypted backup/restore, and SQLCipher encryption/purge.
/// </summary>
public interface IDatabaseService
{
    Task InitializeAsync();
    IRepository<Expense> Expenses { get; }
    IRepository<Category> Categories { get; }
    IRepository<UserProfile> UserProfiles { get; }
    IRepository<AppSettings> Settings { get; }
    IRepository<CategoryFeedback> CategoryFeedbacks { get; }
    IRepository<Tontine> Tontines { get; }
    IRepository<TontineMember> TontineMembers { get; }
    IRepository<TontineRound> TontineRounds { get; }
    IRepository<TontineContribution> TontineContributions { get; }
    Task<SQLiteAsyncConnection> GetConnectionAsync();
    Task SeedDefaultDataAsync();

    string DatabasePath { get; }
    bool IsEncrypted { get; }

    /// <summary>Vault state after the last open attempt; InitializeAsync throws VaultUnavailableException when not Ready.</summary>
    VaultState VaultState { get; }
    Task<long> GetDatabaseSizeBytesAsync();
    Task<string> GetFormattedDatabaseSizeAsync();
    Task<string> CreatePasswordBackupAsync(string destinationPath, string password);
    Task<BackupValidationStatus> ValidatePasswordBackupAsync(string backupFilePath, string password);
    Task RestorePasswordBackupAsync(string backupFilePath, string password);
    Task PurgeDatabaseAndKeyAsync();
}
