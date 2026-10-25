using System.Security.Cryptography;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.Security;
using Xunit;

namespace SmartBudgetML.Tests.Security;

/// <summary>
/// N6 (lot 8C): no plaintext key fallback. The vault reports Ready, KeyUnavailable or KeyLost,
/// never writes a key outside the secure store, and never touches an existing database it
/// cannot open.
/// </summary>
public class VaultStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"vault_state_{Guid.NewGuid():N}");
    private readonly string _dbPath;

    public VaultStateTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "vault.db3");
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task A_SecureStorageFailure_GivesKeyUnavailable_AndWritesNoKeyFile()
    {
        var store = new ToggleKeyStore { Fail = true };
        var db = new DatabaseService(_dbPath, store);

        var ex = await Assert.ThrowsAsync<VaultUnavailableException>(() => db.InitializeAsync());

        Assert.Equal(VaultState.KeyUnavailable, ex.State);
        Assert.Equal(VaultState.KeyUnavailable, db.VaultState);
        Assert.False(File.Exists(_dbPath), "No database may be created without a key.");
        Assert.Empty(Directory.GetFiles(_dir, ".sb_vault_key", SearchOption.AllDirectories));
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.False(File.Exists(Path.Combine(localAppData, ".sb_vault_key")));
        Assert.Equal(0, store.CreateCalls);
    }

    [Fact]
    public async Task A_Retry_AfterStorageRecovers_GivesReady()
    {
        var store = new ToggleKeyStore { Fail = true };
        var db = new DatabaseService(_dbPath, store);
        await Assert.ThrowsAsync<VaultUnavailableException>(() => db.InitializeAsync());

        store.Fail = false;
        await db.InitializeAsync();

        Assert.Equal(VaultState.Ready, db.VaultState);
        Assert.True(db.IsEncrypted);
        await CloseAsync(db);
    }

    [Fact]
    public async Task B_ExistingEncryptedDatabase_WithoutKey_GivesKeyLost_AndLeavesFileUntouched()
    {
        await CreateEncryptedDatabaseAsync(new ToggleKeyStore());
        var hashBefore = HashFile(_dbPath);

        var emptyStore = new ToggleKeyStore();
        var db = new DatabaseService(_dbPath, emptyStore);
        var ex = await Assert.ThrowsAsync<VaultUnavailableException>(() => db.InitializeAsync());

        Assert.Equal(VaultState.KeyLost, ex.State);
        Assert.Equal(VaultState.KeyLost, db.VaultState);
        Assert.Equal(hashBefore, HashFile(_dbPath));
        Assert.Equal(0, emptyStore.CreateCalls);
        Assert.False(await emptyStore.HasMasterKeyAsync(), "No new key may be created over an existing database.");
    }

    [Fact]
    public async Task B_ExistingEncryptedDatabase_WithWrongKey_GivesKeyLost_AndLeavesFileUntouched()
    {
        await CreateEncryptedDatabaseAsync(new ToggleKeyStore());
        var hashBefore = HashFile(_dbPath);

        var otherStore = new ToggleKeyStore();
        await otherStore.CreateMasterKeyAsync();
        var db = new DatabaseService(_dbPath, otherStore);
        var ex = await Assert.ThrowsAsync<VaultUnavailableException>(() => db.InitializeAsync());

        Assert.Equal(VaultState.KeyLost, ex.State);
        Assert.Equal(hashBefore, HashFile(_dbPath));
    }

    [Fact]
    public async Task C_FreshInstall_CreatesKey_AndIsReady()
    {
        var store = new ToggleKeyStore();
        var db = new DatabaseService(_dbPath, store);

        await db.InitializeAsync();

        Assert.Equal(VaultState.Ready, db.VaultState);
        Assert.True(db.IsEncrypted);
        Assert.Equal(1, store.CreateCalls);
        Assert.True(await store.HasMasterKeyAsync());
        await CloseAsync(db);
    }

    [Fact]
    public async Task C_PlaintextDatabase_WithoutKey_CreatesKeyAndMigrates()
    {
        var plain = new DatabaseService(_dbPath, keyStore: null);
        await plain.InitializeAsync();
        await plain.Expenses.InsertAsync(new Expense { Description = "plain", Amount = 1000m, CategoryId = 1, Date = new DateTime(2026, 10, 1) });
        await CloseAsync(plain);

        var store = new ToggleKeyStore();
        var db = new DatabaseService(_dbPath, store);
        await db.InitializeAsync();

        Assert.Equal(VaultState.Ready, db.VaultState);
        Assert.True(db.IsEncrypted);
        Assert.Equal(1, store.CreateCalls);
        Assert.Single(await db.Expenses.GetAllAsync());
        await CloseAsync(db);
    }

    private async Task CreateEncryptedDatabaseAsync(ISecureKeyStore store)
    {
        var db = new DatabaseService(_dbPath, store);
        await db.InitializeAsync();
        await db.Expenses.InsertAsync(new Expense { Description = "secret", Amount = 5000m, CategoryId = 1, Date = new DateTime(2026, 10, 1) });
        await CloseAsync(db);
    }

    private static async Task CloseAsync(DatabaseService db)
    {
        var conn = await db.GetConnectionAsync();
        await conn.CloseAsync();
    }

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class ToggleKeyStore : ISecureKeyStore
    {
        private readonly InMemorySecureKeyStore _inner = new();

        public bool Fail { get; set; }
        public int CreateCalls { get; private set; }

        public Task<string?> GetMasterKeyAsync()
        {
            if (Fail) throw new InvalidOperationException("secure storage unavailable");
            return _inner.GetMasterKeyAsync();
        }

        public Task<string> CreateMasterKeyAsync()
        {
            if (Fail) throw new InvalidOperationException("secure storage unavailable");
            CreateCalls++;
            return _inner.CreateMasterKeyAsync();
        }

        public Task<bool> HasMasterKeyAsync() => _inner.HasMasterKeyAsync();
        public Task PurgeMasterKeyAsync() => _inner.PurgeMasterKeyAsync();
    }
}
