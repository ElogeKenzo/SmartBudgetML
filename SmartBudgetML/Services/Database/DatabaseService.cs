using SQLite;
using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Database;

/// <summary>
/// Orchestrates the local SQLite database lifecycle, schema migrations,
/// and provides typed generic repositories for all entities.
/// </summary>
public class DatabaseService : IDatabaseService
{
    private readonly string _databasePath;
    private SQLiteAsyncConnection? _connection;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public IRepository<Expense> Expenses { get; private set; } = null!;
    public IRepository<Category> Categories { get; private set; } = null!;
    public IRepository<UserProfile> UserProfiles { get; private set; } = null!;
    public IRepository<AppSettings> Settings { get; private set; } = null!;

    public DatabaseService(string? customDbPath = null)
    {
        _databasePath = customDbPath ?? DatabaseConstants.DatabasePath;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            // Ensure parent directory exists
            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connection = new SQLiteAsyncConnection(_databasePath, DatabaseConstants.Flags);

            // Provision schemas
            await _connection.CreateTableAsync<Category>();
            await _connection.CreateTableAsync<Expense>();
            await _connection.CreateTableAsync<UserProfile>();
            await _connection.CreateTableAsync<AppSettings>();

            // Initialize typed generic repositories
            Categories = new SqliteRepository<Category>(_connection);
            Expenses = new SqliteRepository<Expense>(_connection);
            UserProfiles = new SqliteRepository<UserProfile>(_connection);
            Settings = new SqliteRepository<AppSettings>(_connection);

            // Seed default master data
            await SeedDefaultDataAsync();

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<SQLiteAsyncConnection> GetConnectionAsync()
    {
        if (!_isInitialized)
        {
            await InitializeAsync();
        }
        return _connection!;
    }

    public async Task SeedDefaultDataAsync()
    {
        // 1. Seed Categories if empty
        var categoryCount = await Categories.CountAsync();
        if (categoryCount == 0)
        {
            var defaultCategories = new List<Category>
            {
                new() { Name = "Food & Dining", Icon = "utensils", ColorHex = "#F59E0B", BudgetLimit = 350m, IsDefault = true, IsIncome = false },
                new() { Name = "Transportation", Icon = "car", ColorHex = "#3B82F6", BudgetLimit = 150m, IsDefault = true, IsIncome = false },
                new() { Name = "Housing & Rent", Icon = "home", ColorHex = "#8B5CF6", BudgetLimit = 800m, IsDefault = true, IsIncome = false },
                new() { Name = "Utilities", Icon = "zap", ColorHex = "#EAB308", BudgetLimit = 120m, IsDefault = true, IsIncome = false },
                new() { Name = "Groceries", Icon = "shopping-cart", ColorHex = "#10B981", BudgetLimit = 400m, IsDefault = true, IsIncome = false },
                new() { Name = "Healthcare", Icon = "heart", ColorHex = "#EF4444", BudgetLimit = 100m, IsDefault = true, IsIncome = false },
                new() { Name = "Entertainment", Icon = "film", ColorHex = "#EC4899", BudgetLimit = 120m, IsDefault = true, IsIncome = false },
                new() { Name = "Shopping", Icon = "shopping-bag", ColorHex = "#14B8A6", BudgetLimit = 200m, IsDefault = true, IsIncome = false },
                new() { Name = "Salary / Income", Icon = "wallet", ColorHex = "#10B981", BudgetLimit = 0m, IsDefault = true, IsIncome = true },
                new() { Name = "Investments", Icon = "trending-up", ColorHex = "#6366F1", BudgetLimit = 250m, IsDefault = true, IsIncome = false },
                new() { Name = "Other", Icon = "more-horizontal", ColorHex = "#6B7280", BudgetLimit = 100m, IsDefault = true, IsIncome = false }
            };

            await Categories.InsertAllAsync(defaultCategories);
        }

        // 2. Seed Default AppSettings if empty
        var settingsCount = await Settings.CountAsync();
        if (settingsCount == 0)
        {
            var defaultSettings = new AppSettings
            {
                IsBiometricEnabled = false,
                IsPinAuthEnabled = false, // starts false until user completes onboarding
                DarkMode = true,          // Dark theme by default per guidelines
                CurrencyCode = "USD",
                AutoLockTimeoutSeconds = 300
            };
            await Settings.InsertAsync(defaultSettings);
        }
    }
}
