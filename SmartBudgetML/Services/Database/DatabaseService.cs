using System.Globalization;
using System.Reflection;
using System.Text;
using SQLite;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Security;

namespace SmartBudgetML.Services.Database;

/// <summary>
/// Orchestrates the local SQLite/SQLCipher database lifecycle, schema migrations,
/// plaintext-to-encrypted database migration, real file size inspection,
/// password-encrypted backup and restore, secure purge, and typed generic repositories.
/// </summary>
public class DatabaseService : IDatabaseService
{
    /// <summary>Copy of the database taken just before a restore replaces it.</summary>
    public const string PreRestoreSuffix = ".pre-restore";
    private const string RestoreTempSuffix = ".restore_tmp";

    private static readonly byte[] SqliteMagicHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    // Every table a backup must contain, read from the models' [Table] names.
    private static readonly string[] RequiredTables = new[]
    {
        typeof(Category), typeof(Expense), typeof(UserProfile), typeof(AppSettings), typeof(CategoryFeedback),
        typeof(Tontine), typeof(TontineMember), typeof(TontineRound), typeof(TontineContribution)
    }.Select(t => t.GetCustomAttribute<TableAttribute>()?.Name ?? t.Name).ToArray();

    private readonly string _databasePath;
    private readonly ISecureKeyStore? _keyStore;
    private SQLiteAsyncConnection? _connection;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _isInitialized;
    private bool _isEncrypted;
    private string? _masterKey;

    public string DatabasePath => _databasePath;
    public bool IsEncrypted => _isEncrypted;
    public VaultState VaultState { get; private set; } = VaultState.Ready;

    public IRepository<Expense> Expenses { get; private set; } = null!;
    public IRepository<Category> Categories { get; private set; } = null!;
    public IRepository<UserProfile> UserProfiles { get; private set; } = null!;
    public IRepository<AppSettings> Settings { get; private set; } = null!;
    public IRepository<CategoryFeedback> CategoryFeedbacks { get; private set; } = null!;
    public IRepository<Tontine> Tontines { get; private set; } = null!;
    public IRepository<TontineMember> TontineMembers { get; private set; } = null!;
    public IRepository<TontineRound> TontineRounds { get; private set; } = null!;
    public IRepository<TontineContribution> TontineContributions { get; private set; } = null!;

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
            masterKey = await ResolveMasterKeyAsync();

            if (File.Exists(_databasePath) && IsPlaintextSqliteFile(_databasePath))
            {
                await MigratePlaintextToEncryptedAsync(_databasePath, masterKey);
            }
        }

        if (!string.IsNullOrWhiteSpace(masterKey))
        {
            var existedBeforeOpen = File.Exists(_databasePath);
            var connStr = new SQLiteConnectionString(
                _databasePath,
                DatabaseConstants.Flags,
                storeDateTimeAsTicks: true,
                key: masterKey);
            _connection = new SQLiteAsyncConnection(connStr);

            if (existedBeforeOpen)
            {
                try
                {
                    // A wrong key only shows up on the first read: "file is not a database".
                    await _connection.ExecuteScalarAsync<int>("SELECT count(*) FROM sqlite_master;");
                }
                catch (SQLiteException ex)
                {
                    await _connection.CloseAsync();
                    _connection = null;
                    VaultState = VaultState.KeyLost;
                    throw new VaultUnavailableException(VaultState.KeyLost, ex);
                }
            }

            _masterKey = masterKey;
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
        await _connection.CreateTableAsync<Tontine>();
        await _connection.CreateTableAsync<TontineMember>();
        await _connection.CreateTableAsync<TontineRound>();
        await _connection.CreateTableAsync<TontineContribution>();

        Categories = new SqliteRepository<Category>(_connection);
        Expenses = new SqliteRepository<Expense>(_connection);
        UserProfiles = new SqliteRepository<UserProfile>(_connection);
        Settings = new SqliteRepository<AppSettings>(_connection);
        CategoryFeedbacks = new SqliteRepository<CategoryFeedback>(_connection);
        Tontines = new SqliteRepository<Tontine>(_connection);
        TontineMembers = new SqliteRepository<TontineMember>(_connection);
        TontineRounds = new SqliteRepository<TontineRound>(_connection);
        TontineContributions = new SqliteRepository<TontineContribution>(_connection);
        VaultState = VaultState.Ready;
    }

    /// <summary>
    /// Returns the stored key, or creates one only when there is no database or the database is
    /// plaintext (migration). Never creates a key over an existing encrypted database (N6).
    /// </summary>
    private async Task<string> ResolveMasterKeyAsync()
    {
        string? key;
        try
        {
            key = await _keyStore!.GetMasterKeyAsync();
        }
        catch (Exception ex)
        {
            VaultState = VaultState.KeyUnavailable;
            throw new VaultUnavailableException(VaultState.KeyUnavailable, ex);
        }

        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        var file = new FileInfo(_databasePath);
        if (file.Exists && file.Length > 0 && !IsPlaintextSqliteFile(_databasePath))
        {
            VaultState = VaultState.KeyLost;
            throw new VaultUnavailableException(VaultState.KeyLost);
        }

        try
        {
            return await _keyStore.CreateMasterKeyAsync();
        }
        catch (Exception ex)
        {
            VaultState = VaultState.KeyUnavailable;
            throw new VaultUnavailableException(VaultState.KeyUnavailable, ex);
        }
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
            // 1. Export the whole plaintext database (every table, ids included) into a new
            //    SQLCipher file. Row-by-row InsertAll would renumber AutoIncrement ids and break
            //    Expense.CategoryId and the tontine links.
            var plainConnStr = new SQLiteConnectionString(dbPath, DatabaseConstants.Flags, storeDateTimeAsTicks: true);
            var plainConn = new SQLiteAsyncConnection(plainConnStr);
            try
            {
                var escapedTarget = tempEncryptedPath.Replace("'", "''");
                var escapedKey = masterKey.Replace("'", "''");
                await plainConn.ExecuteAsync($"ATTACH DATABASE '{escapedTarget}' AS encrypted KEY '{escapedKey}';");
                await plainConn.ExecuteScalarAsync<string>("SELECT sqlcipher_export('encrypted');");

                // 2. Verify row count parity for every table before replacing
                var tables = await plainConn.QueryScalarsAsync<string>(
                    "SELECT name FROM main.sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';");
                foreach (var table in tables)
                {
                    var quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
                    var plainCount = await plainConn.ExecuteScalarAsync<long>($"SELECT count(*) FROM main.{quoted};");
                    var encCount = await plainConn.ExecuteScalarAsync<long>($"SELECT count(*) FROM encrypted.{quoted};");
                    if (plainCount != encCount)
                    {
                        throw new InvalidOperationException($"Row count mismatch for table {table} during SQLCipher database migration.");
                    }
                }

                await plainConn.ExecuteAsync("DETACH DATABASE encrypted;");
            }
            finally
            {
                await plainConn.CloseAsync();
            }

            // 3. Atomically replace original plaintext file with encrypted file
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

    /// <summary>
    /// Writes a copy of the whole database encrypted with <paramref name="password"/>, not with the
    /// phone's master key, so it can be restored on another phone (lot 8D). The file is validated
    /// before it is returned. The password is never stored or logged.
    /// </summary>
    public async Task<string> CreatePasswordBackupAsync(string destinationPath, string password)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Backup destination path cannot be empty.", nameof(destinationPath));
        }
        if (BackupPasswordPolicy.Validate(password, password) != BackupPasswordCheck.Ok)
        {
            throw new ArgumentException("Backup password is too short.", nameof(password));
        }

        var conn = await GetConnectionAsync();
        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = destinationPath + ".tmp";
        DeleteIfExists(tempPath);
        try
        {
            // Bound parameters: no quoting of the path or the password is needed.
            await conn.ExecuteAsync("ATTACH DATABASE ? AS backup KEY ?;", tempPath, password);
            try
            {
                await conn.ExecuteScalarAsync<string>("SELECT sqlcipher_export('backup');");
                await conn.ExecuteAsync($"PRAGMA backup.user_version = {DatabaseConstants.SchemaVersion};");
            }
            finally
            {
                await conn.ExecuteAsync("DETACH DATABASE backup;");
            }

            var status = await ValidatePasswordBackupAsync(tempPath, password);
            if (status != BackupValidationStatus.Valid)
            {
                throw new InvalidDataException($"The new backup failed validation: {status}.");
            }

            File.Move(tempPath, destinationPath, overwrite: true);
            return destinationPath;
        }
        catch
        {
            DeleteIfExists(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Opens the backup with the password and checks integrity, tables and schema version.
    /// Reads only: the current database is never touched. Runs off the calling thread because
    /// SQLCipher derives the key from the password with PBKDF2.
    /// </summary>
    public Task<BackupValidationStatus> ValidatePasswordBackupAsync(string backupFilePath, string password) =>
        Task.Run(() => ValidateBackupFile(backupFilePath, password));

    /// <summary>
    /// Replaces all current data with the backup, re-encrypted with this phone's master key.
    /// Throws <see cref="BackupRestoreException"/> without changing anything when the backup is
    /// refused. The previous database is kept as <see cref="PreRestoreSuffix"/>.
    /// </summary>
    public async Task RestorePasswordBackupAsync(string backupFilePath, string password)
    {
        var status = await ValidatePasswordBackupAsync(backupFilePath, password);
        if (status != BackupValidationStatus.Valid)
        {
            throw new BackupRestoreException(status);
        }

        await InitializeAsync();
        var importPath = _databasePath + RestoreTempSuffix;
        var targetKey = _isEncrypted ? _masterKey : null;
        DeleteIfExists(importPath);

        await _initLock.WaitAsync();
        try
        {
            try
            {
                await Task.Run(() => ImportBackupInto(backupFilePath, password, importPath, targetKey));
            }
            catch (Exception ex)
            {
                throw new BackupRestoreException(BackupValidationStatus.NotABackup, ex);
            }

            var preRestorePath = _databasePath + PreRestoreSuffix;
            await CloseConnectionAsync();
            File.Copy(_databasePath, preRestorePath, overwrite: true);
            try
            {
                File.Copy(importPath, _databasePath, overwrite: true);
                DeleteJournalFiles();
                await OpenAndProvisionTablesAsync();
                await SeedDefaultDataAsync();
            }
            catch
            {
                await CloseConnectionAsync();
                File.Copy(preRestorePath, _databasePath, overwrite: true);
                File.Delete(preRestorePath);
                DeleteJournalFiles();
                await OpenAndProvisionTablesAsync();
                throw;
            }

            _isInitialized = true;
        }
        finally
        {
            DeleteIfExists(importPath);
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
            _masterKey = null;

            string[] suffixes = { "", "-wal", "-shm", ".bak", ".enc_tmp", PreRestoreSuffix, RestoreTempSuffix };
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

    private static BackupValidationStatus ValidateBackupFile(string filePath, string password)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || string.IsNullOrEmpty(password))
        {
            return BackupValidationStatus.Unreadable;
        }

        SQLiteConnection conn;
        try
        {
            conn = new SQLiteConnection(new SQLiteConnectionString(filePath, SQLiteOpenFlags.ReadOnly, storeDateTimeAsTicks: true, key: password));
        }
        catch (SQLiteException)
        {
            return BackupValidationStatus.Unreadable;
        }

        using (conn)
        {
            try
            {
                // A wrong password or a damaged file only fails on the first read.
                conn.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master;");
            }
            catch (SQLiteException)
            {
                return BackupValidationStatus.Unreadable;
            }

            try
            {
                var integrity = conn.ExecuteScalar<string>("PRAGMA integrity_check;");
                if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                {
                    return BackupValidationStatus.NotABackup;
                }

                if (conn.ExecuteScalar<int>("PRAGMA user_version;") > DatabaseConstants.SchemaVersion)
                {
                    return BackupValidationStatus.TooNew;
                }

                var tables = conn.QueryScalars<string>("SELECT name FROM sqlite_master WHERE type = 'table';");
                return RequiredTables.All(t => tables.Contains(t, StringComparer.OrdinalIgnoreCase))
                    ? BackupValidationStatus.Valid
                    : BackupValidationStatus.NotABackup;
            }
            catch (SQLiteException)
            {
                return BackupValidationStatus.NotABackup;
            }
        }
    }

    /// <summary>Copies every table of the backup into a new file encrypted with <paramref name="targetKey"/>.</summary>
    private static void ImportBackupInto(string backupFilePath, string password, string importPath, string? targetKey)
    {
        // ReadWrite | Create: the attached import file inherits the open flags of the main connection.
        using var source = new SQLiteConnection(new SQLiteConnectionString(
            backupFilePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create, storeDateTimeAsTicks: true, key: password));
        source.Execute("ATTACH DATABASE ? AS restored KEY ?;", importPath, targetKey ?? string.Empty);
        try
        {
            source.ExecuteScalar<string>("SELECT sqlcipher_export('restored');");
            source.Execute($"PRAGMA restored.user_version = {DatabaseConstants.SchemaVersion};");

            foreach (var table in RequiredTables)
            {
                var quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
                var backupCount = source.ExecuteScalar<long>($"SELECT count(*) FROM main.{quoted};");
                var importedCount = source.ExecuteScalar<long>($"SELECT count(*) FROM restored.{quoted};");
                if (backupCount != importedCount)
                {
                    throw new InvalidOperationException($"Row count mismatch for table {table} during restore.");
                }
            }
        }
        finally
        {
            source.Execute("DETACH DATABASE restored;");
        }
    }

    private async Task CloseConnectionAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            _connection = null;
        }
    }

    private void DeleteJournalFiles()
    {
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
        {
            DeleteIfExists(_databasePath + suffix);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
