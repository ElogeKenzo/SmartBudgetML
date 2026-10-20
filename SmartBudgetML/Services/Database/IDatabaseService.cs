using SQLite;
using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Database;

/// <summary>
/// Interface for database lifecycle, table provisioning, entity repositories,
/// real file size inspection, and atomic SQLite backup/restore.
/// </summary>
public interface IDatabaseService
{
    Task InitializeAsync();
    IRepository<Expense> Expenses { get; }
    IRepository<Category> Categories { get; }
    IRepository<UserProfile> UserProfiles { get; }
    IRepository<AppSettings> Settings { get; }
    IRepository<CategoryFeedback> CategoryFeedbacks { get; }
    Task<SQLiteAsyncConnection> GetConnectionAsync();
    Task SeedDefaultDataAsync();

    string DatabasePath { get; }
    Task<long> GetDatabaseSizeBytesAsync();
    Task<string> GetFormattedDatabaseSizeAsync();
    Task<string> BackupDatabaseAsync(string destinationPath);
    Task RestoreDatabaseAsync(string backupFilePath);
}
