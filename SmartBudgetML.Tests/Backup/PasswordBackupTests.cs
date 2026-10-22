using System.Security.Cryptography;
using SQLite;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Data;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.Security;
using Xunit;

namespace SmartBudgetML.Tests.Backup;

/// <summary>
/// Lot 8D (N5, A5): the backup is encrypted with a password chosen by the user, not with the
/// phone's master key, so it can be restored on another phone. A failed or cancelled restore
/// must leave the current database untouched.
/// </summary>
public class PasswordBackupTests : IDisposable
{
    // Contains quotes on purpose: the password goes into ATTACH ... KEY.
    private const string Password = "Mot'de\"passe-2026";

    private readonly string _tempDir;

    public PasswordBackupTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SmartBudgetML_Backup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch (IOException)
        {
            // A pooled SQLite handle can keep a file open a little longer on Windows.
        }
    }

    [Fact]
    public async Task Backup_RestoredIntoDatabaseWithAnotherMasterKey_GivesIdenticalData()
    {
        var (source, _) = await CreateDatabaseAsync("phone_a.db3");
        var transport = (await source.Categories.GetAllAsync()).Single(c => c.Key == "transport");
        await source.Expenses.InsertAsync(new Expense { Description = "Moto Akwa", Amount = 500m, CategoryId = transport.Id, Date = new DateTime(2026, 10, 1) });
        await source.Expenses.InsertAsync(new Expense { Description = "Salaire", Amount = 250_000m, CategoryId = transport.Id, IsIncome = true, Date = new DateTime(2026, 10, 2) });
        await source.Tontines.InsertAsync(new Tontine { Name = "Njangi quartier" });
        var expected = await source.Expenses.GetAllAsync();

        var backupPath = await source.CreatePasswordBackupAsync(Path.Combine(_tempDir, "smartbudget-20261022.sbbackup"), Password);

        var (target, targetKeys) = await CreateDatabaseAsync("phone_b.db3");
        await target.Expenses.InsertAsync(new Expense { Description = "Donnée du téléphone B", Amount = 1m, CategoryId = transport.Id, Date = DateTime.Today });

        await target.RestorePasswordBackupAsync(backupPath, Password);

        var restored = await target.Expenses.GetAllAsync();
        Assert.Equal(
            expected.Select(e => (e.Id, e.Description, e.Amount, e.CategoryId, e.IsIncome, e.Date)),
            restored.Select(e => (e.Id, e.Description, e.Amount, e.CategoryId, e.IsIncome, e.Date)));
        Assert.Single(await target.Tontines.GetAllAsync());

        // The restored file is re-encrypted with phone B's own key: it reopens normally.
        await CloseAsync(target);
        var reopened = new DatabaseService(target.DatabasePath, targetKeys);
        await reopened.InitializeAsync();
        Assert.True(reopened.IsEncrypted);
        Assert.Equal(2, await reopened.Expenses.CountAsync());
    }

    [Fact]
    public async Task Backup_IsEncryptedWithThePassword_NotWithThePhoneKey()
    {
        var (source, keys) = await CreateDatabaseAsync("phone.db3");
        var backupPath = await source.CreatePasswordBackupAsync(Path.Combine(_tempDir, "b.sbbackup"), Password);
        var phoneKey = await keys.GetMasterKeyAsync();

        Assert.False(CanOpen(backupPath, null));
        Assert.False(CanOpen(backupPath, phoneKey));
        Assert.True(CanOpen(backupPath, Password));
        Assert.Equal(BackupValidationStatus.Valid, await source.ValidatePasswordBackupAsync(backupPath, Password));
    }

    [Fact]
    public async Task Backup_RecordsTheSchemaVersion()
    {
        var (source, _) = await CreateDatabaseAsync("phone.db3");
        var backupPath = await source.CreatePasswordBackupAsync(Path.Combine(_tempDir, "b.sbbackup"), Password);

        using var conn = new SQLiteConnection(new SQLiteConnectionString(backupPath, SQLiteOpenFlags.ReadOnly, true, key: Password));
        Assert.Equal(DatabaseConstants.SchemaVersion, conn.ExecuteScalar<int>("PRAGMA user_version;"));
    }

    [Fact]
    public async Task Restore_WithWrongPassword_FailsAndLeavesDatabaseUnchanged()
    {
        var (source, _) = await CreateDatabaseAsync("phone_a.db3");
        var backupPath = await source.CreatePasswordBackupAsync(Path.Combine(_tempDir, "b.sbbackup"), Password);
        var (target, _) = await CreateTargetWithOneExpenseAsync();

        Assert.Equal(BackupValidationStatus.Unreadable, await target.ValidatePasswordBackupAsync(backupPath, "mauvais-mot-de-passe"));
        var ex = await Assert.ThrowsAsync<BackupRestoreException>(() => target.RestorePasswordBackupAsync(backupPath, "mauvais-mot-de-passe"));
        Assert.Equal(BackupValidationStatus.Unreadable, ex.Status);

        await AssertTargetUnchangedAsync(target);
    }

    [Fact]
    public async Task Restore_CorruptFile_FailsAndLeavesDatabaseUnchanged()
    {
        var corruptPath = Path.Combine(_tempDir, "corrupt.sbbackup");
        await File.WriteAllBytesAsync(corruptPath, RandomNumberGenerator.GetBytes(8192));
        var (target, _) = await CreateTargetWithOneExpenseAsync();

        var ex = await Assert.ThrowsAsync<BackupRestoreException>(() => target.RestorePasswordBackupAsync(corruptPath, Password));
        Assert.Equal(BackupValidationStatus.Unreadable, ex.Status);

        await AssertTargetUnchangedAsync(target);
    }

    [Fact]
    public async Task Restore_DatabaseWithoutAppTables_IsRejectedAsNotABackup()
    {
        var foreignPath = Path.Combine(_tempDir, "foreign.sbbackup");
        using (var foreign = new SQLiteConnection(new SQLiteConnectionString(foreignPath, DatabaseConstants.Flags, true, key: Password)))
        {
            foreign.Execute("CREATE TABLE Other (Id INTEGER PRIMARY KEY);");
        }
        var (target, _) = await CreateTargetWithOneExpenseAsync();

        Assert.Equal(BackupValidationStatus.NotABackup, await target.ValidatePasswordBackupAsync(foreignPath, Password));
        await Assert.ThrowsAsync<BackupRestoreException>(() => target.RestorePasswordBackupAsync(foreignPath, Password));

        await AssertTargetUnchangedAsync(target);
    }

    [Fact]
    public async Task Restore_BackupFromNewerAppVersion_IsRejected()
    {
        var (source, _) = await CreateDatabaseAsync("phone_a.db3");
        var backupPath = await source.CreatePasswordBackupAsync(Path.Combine(_tempDir, "b.sbbackup"), Password);
        using (var conn = new SQLiteConnection(new SQLiteConnectionString(backupPath, SQLiteOpenFlags.ReadWrite, true, key: Password)))
        {
            conn.Execute($"PRAGMA user_version = {DatabaseConstants.SchemaVersion + 1};");
        }
        var (target, _) = await CreateTargetWithOneExpenseAsync();

        Assert.Equal(BackupValidationStatus.TooNew, await target.ValidatePasswordBackupAsync(backupPath, Password));
        var ex = await Assert.ThrowsAsync<BackupRestoreException>(() => target.RestorePasswordBackupAsync(backupPath, Password));
        Assert.Equal(BackupValidationStatus.TooNew, ex.Status);

        await AssertTargetUnchangedAsync(target);
    }

    [Fact]
    public async Task ValidatingThenCancelling_DoesNotModifyAnything()
    {
        var (source, _) = await CreateDatabaseAsync("phone_a.db3");
        var backupPath = await source.CreatePasswordBackupAsync(Path.Combine(_tempDir, "b.sbbackup"), Password);
        var (target, _) = await CreateTargetWithOneExpenseAsync();
        var hashBefore = await HashFileAsync(target.DatabasePath);

        Assert.Equal(BackupValidationStatus.Valid, await target.ValidatePasswordBackupAsync(backupPath, Password));
        var confirmation = new VaultResetConfirmation();
        confirmation.Advance();
        confirmation.Cancel();

        Assert.Equal(hashBefore, await HashFileAsync(target.DatabasePath));
        await AssertTargetUnchangedAsync(target);
    }

    [Fact]
    public async Task Restore_KeepsAPreRestoreCopyOfThePreviousData()
    {
        var (source, _) = await CreateDatabaseAsync("phone_a.db3");
        var backupPath = await source.CreatePasswordBackupAsync(Path.Combine(_tempDir, "b.sbbackup"), Password);
        var (target, targetKeys) = await CreateTargetWithOneExpenseAsync();

        await target.RestorePasswordBackupAsync(backupPath, Password);

        Assert.Equal(0, await target.Expenses.CountAsync());
        var preRestorePath = target.DatabasePath + DatabaseService.PreRestoreSuffix;
        Assert.True(File.Exists(preRestorePath));
        using var copy = new SQLiteConnection(new SQLiteConnectionString(preRestorePath, SQLiteOpenFlags.ReadOnly, true, key: await targetKeys.GetMasterKeyAsync()));
        Assert.Equal("Donnée du téléphone B", copy.ExecuteScalar<string>("SELECT Description FROM Expenses;"));
    }

    [Fact]
    public async Task Purge_AlsoDeletesThePreRestoreCopy()
    {
        var (source, _) = await CreateDatabaseAsync("phone_a.db3");
        var backupPath = await source.CreatePasswordBackupAsync(Path.Combine(_tempDir, "b.sbbackup"), Password);
        var (target, _) = await CreateTargetWithOneExpenseAsync();
        await target.RestorePasswordBackupAsync(backupPath, Password);

        await target.PurgeDatabaseAndKeyAsync();

        Assert.False(File.Exists(target.DatabasePath + DatabaseService.PreRestoreSuffix));
    }

    [Theory]
    [InlineData("", "", BackupPasswordCheck.TooShort)]
    [InlineData("1234567", "1234567", BackupPasswordCheck.TooShort)]
    [InlineData("12345678", "12345679", BackupPasswordCheck.Mismatch)]
    [InlineData("12345678", "12345678", BackupPasswordCheck.Ok)]
    public void PasswordPolicy_RequiresEightCharactersAndConfirmation(string password, string confirmation, BackupPasswordCheck expected)
    {
        Assert.Equal(expected, BackupPasswordPolicy.Validate(password, confirmation));
    }

    [Fact]
    public void BackupFileName_UsesTheDateAndTheSbbackupExtension()
    {
        Assert.Equal("smartbudget-20261022.sbbackup", BackupFileNames.ForDate(new DateTime(2026, 10, 22, 23, 59, 0)));
    }

    [Fact]
    public async Task ExportDirectory_KeepsOnlyTheLatestFile()
    {
        var exports = new LocalFileExportService(Path.Combine(_tempDir, "exports"));
        var first = await exports.WriteExportFileAsync("a.csv", "x");
        var second = await exports.WriteExportFileAsync("b.csv", "y");

        Assert.False(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.Equal(Path.GetDirectoryName(second), exports.ExportDirectory);
    }

    private async Task<(DatabaseService Db, InMemorySecureKeyStore Keys)> CreateDatabaseAsync(string fileName)
    {
        var keys = new InMemorySecureKeyStore();
        var db = new DatabaseService(Path.Combine(_tempDir, fileName), keys);
        await db.InitializeAsync();
        return (db, keys);
    }

    private async Task<(DatabaseService Db, InMemorySecureKeyStore Keys)> CreateTargetWithOneExpenseAsync()
    {
        var (db, keys) = await CreateDatabaseAsync("phone_b.db3");
        await db.Expenses.InsertAsync(new Expense { Description = "Donnée du téléphone B", Amount = 1500m, CategoryId = 1, Date = new DateTime(2026, 10, 5) });
        return (db, keys);
    }

    private static async Task AssertTargetUnchangedAsync(DatabaseService target)
    {
        var expenses = await target.Expenses.GetAllAsync();
        Assert.Single(expenses);
        Assert.Equal("Donnée du téléphone B", expenses[0].Description);
        Assert.False(File.Exists(target.DatabasePath + DatabaseService.PreRestoreSuffix));
    }

    private static bool CanOpen(string path, string? key)
    {
        try
        {
            using var conn = new SQLiteConnection(new SQLiteConnectionString(path, SQLiteOpenFlags.ReadOnly, true, key: key));
            conn.ExecuteScalar<int>("SELECT count(*) FROM sqlite_master;");
            return true;
        }
        catch (SQLiteException)
        {
            return false;
        }
    }

    private static async Task CloseAsync(DatabaseService db)
    {
        var connection = await db.GetConnectionAsync();
        await connection.CloseAsync();
    }

    private static async Task<string> HashFileAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }
}
