using System.Globalization;
using System.Text;
using SQLite;
using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Database;

/// <summary>
/// Orchestrates the local SQLite database lifecycle, schema migrations,
/// real file size inspection, atomic SQLite backup/restore, and typed generic repositories.
/// </summary>
public class DatabaseService : IDatabaseService
{
    private static readonly byte[] SqliteMagicHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    private readonly string _databasePath;
    private SQLiteAsyncConnection? _connection;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;

    public string DatabasePath => _databasePath;

    public IRepository<Expense> Expenses { get; private set; } = null!;
    public IRepository<Category> Categories { get; private set; } = null!;
    public IRepository<UserProfile> UserProfiles { get; private set; } = null!;
    public IRepository<AppSettings> Settings { get; private set; } = null!;
    public IRepository<CategoryFeedback> CategoryFeedbacks { get; private set; } = null!;

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

            await OpenAndProvisionTablesAsync();
            await SeedDefaultDataAsync();

            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task OpenAndProvisionTablesAsync()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connection = new SQLiteAsyncConnection(_databasePath, DatabaseConstants.Flags);

        await _connection.CreateTableAsync<Category>();
        await _connection.CreateTableAsync<Expense>();
        await _connection.CreateTableAsync<UserProfile>();
        await _connection.CreateTableAsync<AppSettings>();
        await _connection.CreateTableAsync<CategoryFeedback>();

        Categories = new SqliteRepository<Category>(_connection);
        Expenses = new SqliteRepository<Expense>(_connection);
        UserProfiles = new SqliteRepository<UserProfile>(_connection);
        Settings = new SqliteRepository<AppSettings>(_connection);
        CategoryFeedbacks = new SqliteRepository<CategoryFeedback>(_connection);
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

        var settingsCount = await Settings.CountAsync();
        if (settingsCount == 0)
        {
            var defaultSettings = new AppSettings
            {
                IsBiometricEnabled = false,
                IsPinAuthEnabled = false,
                DarkMode = true,
                CurrencyCode = "USD",
                AutoLockTimeoutSeconds = 300,
                MlConfidenceThreshold = 0.70f,
                AutoCategorizeEnabled = true,
                OverBudgetAlertsEnabled = true,
                DailyRemindersEnabled = false
            };
            await Settings.InsertAsync(defaultSettings);
        }
    }

    public async Task<long> GetDatabaseSizeBytesAsync()
    {
        await InitializeAsync();
        if (!File.Exists(_databasePath))
        {
            return 0L;
        }

        var info = new FileInfo(_databasePath);
        info.Refresh();
        return info.Length;
    }

    public async Task<string> GetFormattedDatabaseSizeAsync()
    {
        var bytes = await GetDatabaseSizeBytesAsync();
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        if (bytes < 1024 * 1024)
        {
            double kb = bytes / 1024.0;
            return $"{kb.ToString("0.0", CultureInfo.InvariantCulture)} KB";
        }

        double mb = bytes / (1024.0 * 1024.0);
        return $"{mb.ToString("0.00", CultureInfo.InvariantCulture)} MB";
    }

    public async Task<string> BackupDatabaseAsync(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Backup destination path cannot be empty.", nameof(destinationPath));
        }

        var conn = await GetConnectionAsync();
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        var escapedPath = destinationPath.Replace("'", "''");
        await conn.ExecuteAsync($"VACUUM INTO '{escapedPath}';");

        ValidateSqliteFile(destinationPath);
        return destinationPath;
    }

    public async Task RestoreDatabaseAsync(string backupFilePath)
    {
        if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
        {
            throw new FileNotFoundException("Backup file does not exist.", backupFilePath);
        }

        // 1. Validate magic header & SQLite integrity before touching active DB
        ValidateSqliteFile(backupFilePath);

        await _initLock.WaitAsync();
        try
        {
            if (_connection != null)
            {
                await _connection.CloseAsync();
                _connection = null;
            }

            File.Copy(backupFilePath, _databasePath, overwrite: true);

            await OpenAndProvisionTablesAsync();
            _isInitialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static void ValidateSqliteFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length < SqliteMagicHeader.Length)
        {
            throw new InvalidDataException($"File '{filePath}' is too small or missing a valid SQLite header.");
        }

        byte[] header = new byte[SqliteMagicHeader.Length];
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            int read = fs.Read(header, 0, header.Length);
            if (read < header.Length || !header.SequenceEqual(SqliteMagicHeader))
            {
                throw new InvalidDataException($"File '{filePath}' does not have a valid SQLite format 3 header.");
            }
        }

        try
        {
            using var checkConn = new SQLiteConnection(filePath, SQLiteOpenFlags.ReadOnly);
            var integrity = checkConn.ExecuteScalar<string>("PRAGMA integrity_check;");
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SQLite integrity check failed for '{filePath}': {integrity}");
            }
            checkConn.Close();
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"Corrupt SQLite backup file '{filePath}': {ex.Message}", ex);
        }
    }
}
