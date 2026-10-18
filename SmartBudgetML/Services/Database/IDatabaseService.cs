using SQLite;
using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Database;

/// <summary>
/// Interface for database lifecycle, table provisioning, and entity repositories.
/// </summary>
public interface IDatabaseService
{
    Task InitializeAsync();
    IRepository<Expense> Expenses { get; }
    IRepository<Category> Categories { get; }
    IRepository<UserProfile> UserProfiles { get; }
    IRepository<AppSettings> Settings { get; }
    Task<SQLiteAsyncConnection> GetConnectionAsync();
    Task SeedDefaultDataAsync();
}
