using System.Globalization;
using System.Text;
using SQLite;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Security;

namespace SmartBudgetML.Services.Database;

/// <summary>
/// Orchestrates the local SQLite/SQLCipher database lifecycle, schema migrations,
/// plaintext-to-encrypted database migration, real file size inspection,
/// atomic backup/restore, secure purge, and typed generic repositories.
/// </summary>
public class DatabaseService : IDatabaseService
{
    private static readonly byte[] SqliteMagicHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    private readonly string _databasePath;
    private readonly ISecureKeyStore? _keyStore;
    private SQLiteAsyncConnection? _connection;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;
    private bool _isEncrypted;

    public string DatabasePath => _databasePath;
    public bool IsEncrypted => _isEncrypted;

    public IRepository<Expense> Expenses { get; private set; } = null!;
    public IRepository<Category> Categories { get; private set; } = null!;
    public IRepository<UserProfile> UserProfiles { get; private set; } = null!;
    public IRepository<AppSettings> Settings { get; private set; } = null!;
    public IRepository<CategoryFeedback> CategoryFeedbacks { get; private set; } = null!;

    public DatabaseService(string? customDbPath = null, ISecureKeyStore? keyStore = null)
    {
        _databasePath = customDbPath ?? DatabaseConstants.DatabasePath;
        _keyStore = keyStore;
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

        string? masterKey = null;
        if (_keyStore != null)
        {
            masterKey = await _keyStore.GetOrCreateMasterKeyAsync();

            if (File.Exists(_databasePath) && IsPlaintextSqliteFile(_databasePath))
            {
                await MigratePlaintextToEncryptedAsync(_databasePath, masterKey);
            }
        }

        if (!string.IsNullOrWhiteSpace(masterKey))
        {
            var connStr = new SQLiteConnectionString(
                _databasePath,
                DatabaseConstants.Flags,
                storeDateTimeAsTicks: true,
                key: masterKey);
            _connection = new SQLiteAsyncConnection(connStr);
            _isEncrypted = true;
        }
        else
        {
            var connStr = new SQLiteConnectionString(
                _databasePath,
                DatabaseConstants.Flags,
                storeDateTimeAsTicks: true);
            _connection = new SQLiteAsyncConnection(connStr);
            _isEncrypted = false;
        }

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

    private static bool IsPlaintextSqliteFile(string filePath)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length < SqliteMagicHeader.Length)
        {
            return false;
        }

        byte[] header = new byte[SqliteMagicHeader.Length];
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int read = fs.Read(header, 0, header.Length);
        return read == SqliteMagicHeader.Length && header.SequenceEqual(SqliteMagicHeader);
    }

    private static async Task MigratePlaintextToEncryptedAsync(string dbPath, string masterKey)
    {
        var backupPath = dbPath + ".bak";
        var tempEncryptedPath = dbPath + ".enc_tmp";

        File.Copy(dbPath, backupPath, overwrite: true);
        if (File.Exists(tempEncryptedPath))
        {
            File.Delete(tempEncryptedPath);
        }

        try
        {
            // 1. Read all existing entities from the plaintext database
            var plainConnStr = new SQLiteConnectionString(dbPath, DatabaseConstants.Flags, storeDateTimeAsTicks: true);
            var plainConn = new SQLiteAsyncConnection(plainConnStr);

            await plainConn.CreateTableAsync<Category>();
            await plainConn.CreateTableAsync<Expense>();
            await plainConn.CreateTableAsync<UserProfile>();
            await plainConn.CreateTableAsync<AppSettings>();
            await plainConn.CreateTableAsync<CategoryFeedback>();

            var categories = await plainConn.Table<Category>().ToListAsync();
            var expenses = await plainConn.Table<Expense>().ToListAsync();
            var profiles = await plainConn.Table<UserProfile>().ToListAsync();
            var settings = await plainConn.Table<AppSettings>().ToListAsync();
            var feedbacks = await plainConn.Table<CategoryFeedback>().ToListAsync();

            await plainConn.CloseAsync();

            // 2. Create the encrypted SQLCipher database and copy all rows
            var encConnStr = new SQLiteConnectionString(
                tempEncryptedPath,
                DatabaseConstants.Flags,
                storeDateTimeAsTicks: true,
                key: masterKey);
            var encConn = new SQLiteAsyncConnection(encConnStr);

            await encConn.CreateTableAsync<Category>();
            await encConn.CreateTableAsync<Expense>();
            await encConn.CreateTableAsync<UserProfile>();
            await encConn.CreateTableAsync<AppSettings>();
            await encConn.CreateTableAsync<CategoryFeedback>();

            if (categories.Count > 0) await encConn.InsertAllAsync(categories);
            if (expenses.Count > 0) await encConn.InsertAllAsync(expenses);
            if (profiles.Count > 0) await encConn.InsertAllAsync(profiles);
            if (settings.Count > 0) await encConn.InsertAllAsync(settings);
            if (feedbacks.Count > 0) await encConn.InsertAllAsync(feedbacks);

            // 3. Verify 100% row count parity before replacing
            var encCatCount = await encConn.Table<Category>().CountAsync();
            var encExpCount = await encConn.Table<Expense>().CountAsync();
            var encProfCount = await encConn.Table<UserProfile>().CountAsync();
            var encSetCount = await encConn.Table<AppSettings>().CountAsync();
            var encFbCount = await encConn.Table<CategoryFeedback>().CountAsync();

            await encConn.CloseAsync();

            if (encCatCount != categories.Count ||
                encExpCount != expenses.Count ||
                encProfCount != profiles.Count ||
                encSetCount != settings.Count ||
                encFbCount != feedbacks.Count)
            {
                throw new InvalidOperationException("Row count mismatch during SQLCipher database migration.");
            }

            // 4. Atomically replace original plaintext file with encrypted file
            File.Copy(tempEncryptedPath, dbPath, overwrite: true);
            File.Delete(tempEncryptedPath);
        }
        catch
        {
            if (File.Exists(tempEncryptedPath))
            {
                File.Delete(tempEncryptedPath);
            }
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, dbPath, overwrite: true);
            }
            throw;
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
        var categoryCount = await Categories.CountAsync();
        if (categoryCount == 0)
        {
            var defaultCategories = CategoryCatalog.GetCameroonianDefaultCategories();
            await Categories.InsertAllAsync(defaultCategories);
        }
        else
        {
            await MigrateLegacyCategoriesAsync();
        }

        var settingsCount = await Settings.CountAsync();
        if (settingsCount == 0)
        {
            var defaultSettings = new AppSettings
            {
                IsBiometricEnabled = false,
                IsPinAuthEnabled = false,
                DarkMode = true,
                CurrencyCode = "XAF",
                Language = "fr",
                AutoLockTimeoutSeconds = 300,
                MlConfidenceThreshold = 0.70f,
                AutoCategorizeEnabled = true,
                OverBudgetAlertsEnabled = true,
                DailyRemindersEnabled = false,
                PinHashVersion = 2,
                PinLength = 6,
                IsScreenProtectionEnabled = true
            };
            await Settings.InsertAsync(defaultSettings);
        }
    }

    private async Task MigrateLegacyCategoriesAsync()
    {
        var existingCategories = await Categories.GetAllAsync();
        var existingKeys = new HashSet<string>(
            existingCategories.Where(c => !string.IsNullOrWhiteSpace(c.Key)).Select(c => c.Key),
            StringComparer.OrdinalIgnoreCase);

        foreach (var cat in existingCategories)
        {
            if (string.IsNullOrWhiteSpace(cat.Key))
            {
                var resolvedKey = CategoryCatalog.ResolveCategoryKey(cat.Name);
                if (!string.IsNullOrWhiteSpace(resolvedKey) && !existingKeys.Contains(resolvedKey))
                {
                    cat.Key = resolvedKey;
                    await Categories.UpdateAsync(cat);
                    existingKeys.Add(resolvedKey);
                }
            }
        }

        var cameroonianDefaults = CategoryCatalog.GetCameroonianDefaultCategories();
        var missingCategories = cameroonianDefaults
            .Where(d => !existingKeys.Contains(d.Key))
            .ToList();

        if (missingCategories.Count > 0)
        {
            await Categories.InsertAllAsync(missingCategories);
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

        string? key = _keyStore != null ? await _keyStore.GetOrCreateMasterKeyAsync() : null;
        ValidateDatabaseFile(destinationPath, key);
        return destinationPath;
    }

    public async Task RestoreDatabaseAsync(string backupFilePath)
    {
        if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
        {
            throw new FileNotFoundException("Backup file does not exist.", backupFilePath);
        }

        string? key = _keyStore != null ? await _keyStore.GetOrCreateMasterKeyAsync() : null;
        ValidateDatabaseFile(backupFilePath, key);

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

    public async Task PurgeDatabaseAndKeyAsync()
    {
        await _initLock.WaitAsync();
        try
        {
            if (_connection != null)
            {
                await _connection.CloseAsync();
                _connection = null;
            }

            _isInitialized = false;
            _isEncrypted = false;

            string[] suffixes = { "", "-wal", "-shm", ".bak", ".enc_tmp" };
            foreach (var suffix in suffixes)
            {
                var target = _databasePath + suffix;
                if (File.Exists(target))
                {
                    File.Delete(target);
                }
            }

            if (_keyStore != null)
            {
                await _keyStore.PurgeMasterKeyAsync();
            }
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static void ValidateDatabaseFile(string filePath, string? masterKey)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length < SqliteMagicHeader.Length)
        {
            throw new InvalidDataException($"File '{filePath}' is too small or invalid.");
        }

        if (string.IsNullOrWhiteSpace(masterKey))
        {
            byte[] header = new byte[SqliteMagicHeader.Length];
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            int read = fs.Read(header, 0, header.Length);
            if (read < header.Length || !header.SequenceEqual(SqliteMagicHeader))
            {
                throw new InvalidDataException($"File '{filePath}' does not have a valid SQLite format 3 header.");
            }
        }

        try
        {
            var connStr = new SQLiteConnectionString(filePath, SQLiteOpenFlags.ReadOnly, storeDateTimeAsTicks: true, key: masterKey);
            using var checkConn = new SQLiteConnection(connStr);
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
            throw new InvalidDataException($"Corrupt or undecryptable SQLite backup file '{filePath}': {ex.Message}", ex);
        }
    }
}
