using System.Security.Cryptography;
using Moq;
using SQLite;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Auth;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.Security;

namespace SmartBudgetML.Tests.Security;

public class Phase3SecurityTests : IDisposable
{
    private readonly string _tempDir;

    public Phase3SecurityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"smartbudget_p3_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore file lock cleanup on test teardown
        }
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset initialUtc)
        {
            _utcNow = initialUtc;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);

        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    // =========================================================================
    // 3.1 (A1) SQLCipher Encryption, Key Isolation, Migration & Purge
    // =========================================================================

    [Fact]
    public async Task EncryptedDatabase_CreatedWithKeyA_CannotBeOpenedWithKeyBOrWithoutKey()
    {
        var dbPath = Path.Combine(_tempDir, "encrypted_vault.db3");
        var keyStoreA = new InMemorySecureKeyStore();
        var keyA = await keyStoreA.CreateMasterKeyAsync();
        Assert.Equal(64, keyA.Length); // 32 bytes = 64 hex chars

        var dbA = new DatabaseService(dbPath, keyStoreA);
        await dbA.InitializeAsync();
        Assert.True(dbA.IsEncrypted);

        await dbA.Expenses.InsertAsync(new Expense
        {
            Description = "Secret Encrypted Transaction",
            Amount = 75000m,
            CategoryId = 1,
            Date = new DateTime(2026, 3, 20)
        });

        var connA = await dbA.GetConnectionAsync();
        await connA.CloseAsync();

        // 1. Verify raw file on disk does NOT start with plaintext "SQLite format 3\0"
        var rawHeader = new byte[16];
        using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            int read = await fs.ReadAsync(rawHeader, 0, 16);
            Assert.Equal(16, read);
        }
        var plaintextMagic = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");
        Assert.False(rawHeader.SequenceEqual(plaintextMagic), "Encrypted DB must not have a plaintext SQLite header.");

        // 2. Attempting to read with a different Key B must fail with SQLiteException
        var keyStoreB = new InMemorySecureKeyStore();
        var keyB = await keyStoreB.CreateMasterKeyAsync();
        Assert.NotEqual(keyA, keyB);

        var connWithKeyB = new SQLiteAsyncConnection(
            new SQLiteConnectionString(dbPath, DatabaseConstants.Flags, storeDateTimeAsTicks: true, key: keyB));
        await Assert.ThrowsAnyAsync<Exception>(() => connWithKeyB.Table<Expense>().ToListAsync());
        await connWithKeyB.CloseAsync();

        // 3. Attempting to read with NO key must fail
        var connWithoutKey = new SQLiteAsyncConnection(
            new SQLiteConnectionString(dbPath, DatabaseConstants.Flags, storeDateTimeAsTicks: true, key: null));
        await Assert.ThrowsAnyAsync<Exception>(() => connWithoutKey.Table<Expense>().ToListAsync());
        await connWithoutKey.CloseAsync();
    }

    [Fact]
    public async Task UnencryptedDatabase_AutomaticallyMigratesToEncrypted_PreservingAllRows_AndCreatesBakFile()
    {
        var dbPath = Path.Combine(_tempDir, "legacy_unencrypted.db3");

        // Step 1: Create a legacy plaintext SQLite database (no keyStore)
        var legacyDb = new DatabaseService(dbPath, keyStore: null);
        await legacyDb.InitializeAsync();
        Assert.False(legacyDb.IsEncrypted);

        await legacyDb.Expenses.InsertAsync(new Expense
        {
            Description = "Legacy Plaintext Expense 1",
            Amount = 15000m,
            CategoryId = 1,
            Date = new DateTime(2026, 3, 1)
        });
        await legacyDb.Expenses.InsertAsync(new Expense
        {
            Description = "Legacy Plaintext Expense 2",
            Amount = 25000m,
            CategoryId = 2,
            Date = new DateTime(2026, 3, 2)
        });
        var originalCategoryCount = await legacyDb.Categories.CountAsync();
        var conn = await legacyDb.GetConnectionAsync();
        await conn.CloseAsync();

        // Step 2: Re-open the same dbPath with an ISecureKeyStore -> triggers automatic migration
        var keyStore = new InMemorySecureKeyStore();
        var encryptedDb = new DatabaseService(dbPath, keyStore);
        await encryptedDb.InitializeAsync();

        Assert.True(encryptedDb.IsEncrypted);
        Assert.True(File.Exists(dbPath + ".bak"), "Migration must create a .bak safety backup.");

        // Verify 100% of rows preserved
        var migratedExpenses = await encryptedDb.Expenses.GetAllAsync();
        var migratedCategories = await encryptedDb.Categories.CountAsync();
        Assert.Equal(2, migratedExpenses.Count);
        Assert.Equal(originalCategoryCount, migratedCategories);
        Assert.Contains(migratedExpenses, e => e.Description == "Legacy Plaintext Expense 1" && e.Amount == 15000m);
        Assert.Contains(migratedExpenses, e => e.Description == "Legacy Plaintext Expense 2" && e.Amount == 25000m);

        // Verify the migrated file is now encrypted on disk (not plaintext header)
        var rawHeader = new byte[16];
        using (var fs = new FileStream(dbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            _ = await fs.ReadAsync(rawHeader, 0, 16);
        }
        var plaintextMagic = System.Text.Encoding.ASCII.GetBytes("SQLite format 3\0");
        Assert.False(rawHeader.SequenceEqual(plaintextMagic));
    }

    [Fact]
    public async Task PurgeDatabaseAndKeyAsync_DeletesDatabaseFilesAndKeyFromKeyStore()
    {
        var dbPath = Path.Combine(_tempDir, "purge_vault.db3");
        var keyStore = new InMemorySecureKeyStore();
        var db = new DatabaseService(dbPath, keyStore);
        await db.InitializeAsync();

        Assert.True(File.Exists(dbPath));
        Assert.True(await keyStore.HasMasterKeyAsync());

        await db.PurgeDatabaseAndKeyAsync();

        Assert.False(File.Exists(dbPath));
        Assert.False(await keyStore.HasMasterKeyAsync());
    }

    // =========================================================================
    // 3.2 (A9) PIN Rate Limiting, Exponential Backoff, Complexity & PBKDF2 600k
    // =========================================================================

    [Fact]
    public async Task PinRateLimiting_EnforcesExponentialBackoff_PersistsAcrossRestarts_AndResetsOnSuccess()
    {
        var dbPath = Path.Combine(_tempDir, "pin_backoff.db3");
        var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        var biometricMock = new Mock<IBiometricService>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero));

        var auth1 = new LocalAuthService(db, biometricMock.Object, clock);
        await auth1.SetPinAsync("739284", enforceComplexity: true);

        // Attempts 1..4 fail without lockout
        for (int i = 1; i <= 4; i++)
        {
            var res = await auth1.VerifyPinWithStatusAsync("000000");
            Assert.False(res.IsValid);
            Assert.False(res.IsLockedOut);
            Assert.Equal(i, res.FailedAttempts);
        }

        // Attempt 5 triggers 30s lockout
        var res5 = await auth1.VerifyPinWithStatusAsync("000000");
        Assert.False(res5.IsValid);
        Assert.True(res5.IsLockedOut);
        Assert.Equal(5, res5.FailedAttempts);
        Assert.Equal(TimeSpan.FromSeconds(30), res5.RemainingLockout);

        // New instance of LocalAuthService on same DB (simulating app restart) must still be locked out!
        var auth2 = new LocalAuthService(db, biometricMock.Object, clock);
        var whileLocked = await auth2.VerifyPinWithStatusAsync("739284"); // even with correct PIN!
        Assert.False(whileLocked.IsValid);
        Assert.True(whileLocked.IsLockedOut);

        // Advance clock by 31 seconds -> lockout expires
        clock.Advance(TimeSpan.FromSeconds(31));

        // Attempt 6 fails -> 60s (1 min) lockout
        var res6 = await auth2.VerifyPinWithStatusAsync("000000");
        Assert.True(res6.IsLockedOut);
        Assert.Equal(6, res6.FailedAttempts);
        Assert.Equal(TimeSpan.FromMinutes(1), res6.RemainingLockout);

        // Advance clock by 61 seconds -> Attempt 7 fails -> 5 min lockout
        clock.Advance(TimeSpan.FromSeconds(61));
        var res7 = await auth2.VerifyPinWithStatusAsync("000000");
        Assert.True(res7.IsLockedOut);
        Assert.Equal(7, res7.FailedAttempts);
        Assert.Equal(TimeSpan.FromMinutes(5), res7.RemainingLockout);

        // Advance clock by 5m01s -> Attempt 8 fails -> 15 min lockout
        clock.Advance(TimeSpan.FromSeconds(301));
        var res8 = await auth2.VerifyPinWithStatusAsync("000000");
        Assert.True(res8.IsLockedOut);
        Assert.Equal(8, res8.FailedAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), res8.RemainingLockout);

        // Advance clock by 15m01s -> Valid PIN succeeds and resets counter + lockout
        clock.Advance(TimeSpan.FromSeconds(901));
        var success = await auth2.VerifyPinWithStatusAsync("739284");
        Assert.True(success.IsValid);
        Assert.False(success.IsLockedOut);
        Assert.Equal(0, success.FailedAttempts);

        var settingsAfter = await auth2.GetSettingsAsync();
        Assert.Equal(0, settingsAfter.FailedPinAttempts);
        Assert.Null(settingsAfter.LockoutUntilUtc);
    }

    [Fact]
    public async Task PinRateLimiting_ClockRollback_MaintainsLockout()
    {
        var dbPath = Path.Combine(_tempDir, "pin_rollback.db3");
        var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        var biometricMock = new Mock<IBiometricService>();
        var initialTime = new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(initialTime);

        var auth = new LocalAuthService(db, biometricMock.Object, clock);
        await auth.SetPinAsync("849205", enforceComplexity: true);

        for (int i = 0; i < 5; i++)
        {
            await auth.VerifyPinWithStatusAsync("999999");
        }

        // User rolls system clock backward by 1 hour to try to bypass lockout
        clock.SetUtcNow(initialTime.AddHours(-1));

        var attemptDuringRollback = await auth.VerifyPinWithStatusAsync("849205");
        Assert.False(attemptDuringRollback.IsValid);
        Assert.True(attemptDuringRollback.IsLockedOut);
    }

    [Theory]
    [InlineData("0000", false)]
    [InlineData("1111", false)]
    [InlineData("1234", false)]
    [InlineData("4321", false)]
    [InlineData("123456", false)]
    [InlineData("654321", false)]
    [InlineData("000000", false)]
    [InlineData("12345", false)]  // must be 4 or 6 digits
    [InlineData("12ab", false)]   // digits only
    [InlineData("7392", true)]
    [InlineData("849205", true)]
    public void ValidatePinComplexity_RejectsTrivialPins_AndAcceptsStrong4And6DigitPins(string pin, bool expectedValid)
    {
        var db = new DatabaseService(Path.Combine(_tempDir, "complexity.db3"));
        var biometricMock = new Mock<IBiometricService>();
        var auth = new LocalAuthService(db, biometricMock.Object);

        var isValid = auth.ValidatePinComplexity(pin, out var error);
        Assert.Equal(expectedValid, isValid);
        if (!expectedValid)
        {
            Assert.False(string.IsNullOrWhiteSpace(error));
        }
    }

    [Fact]
    public async Task PinHashMigration_V1Legacy10kIterations_TransparentlyUpgradesToV2Owasp600kOnValidLogin()
    {
        var dbPath = Path.Combine(_tempDir, "pin_v1_v2.db3");
        var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        // Simulate a legacy v1 PIN stored with 10,000 iterations
        const string legacyPin = "7392";
        var legacySalt = RandomNumberGenerator.GetBytes(16);
        var legacyHash = Rfc2898DeriveBytes.Pbkdf2(
            legacyPin,
            legacySalt,
            LocalAuthService.LegacyIterations,
            HashAlgorithmName.SHA256,
            32);

        var settings = await db.Settings.FirstOrDefaultAsync(_ => true) ?? new AppSettings();
        settings.PinSalt = Convert.ToBase64String(legacySalt);
        settings.PinHash = Convert.ToBase64String(legacyHash);
        settings.PinHashVersion = 1;
        settings.PinLength = 4;
        settings.IsPinAuthEnabled = true;
        await db.Settings.UpdateAsync(settings);

        var biometricMock = new Mock<IBiometricService>();
        var auth = new LocalAuthService(db, biometricMock.Object);

        // Verify login with v1 PIN succeeds AND upgrades hash to v2 (600,000 iterations)
        var verified = await auth.VerifyPinAsync(legacyPin);
        Assert.True(verified);

        var upgradedSettings = await auth.GetSettingsAsync();
        Assert.Equal(2, upgradedSettings.PinHashVersion);
        Assert.NotEqual(Convert.ToBase64String(legacyHash), upgradedSettings.PinHash);

        // Verify subsequent login with v2 hash works
        auth.LockApp();
        Assert.True(await auth.VerifyPinAsync(legacyPin));
    }

    // =========================================================================
    // 3.3 (A8) AutoLockPolicy & Lifecycle Auto-Lock
    // =========================================================================

    [Theory]
    [InlineData(0, 1, true)]     // Immediate lock (0s) locks after 1s
    [InlineData(30, 15, false)]  // 30s timeout does not lock after 15s
    [InlineData(30, 30, true)]   // 30s timeout locks at 30s
    [InlineData(300, 120, false)]
    [InlineData(300, 305, true)]
    [InlineData(300, -10, true)] // Clock rollback locks immediately
    public void AutoLockPolicy_ShouldLock_RespectsTimeoutAndClockRollback(
        int timeoutSeconds,
        int elapsedSeconds,
        bool expectedLock)
    {
        var backgroundAt = new DateTime(2026, 3, 20, 10, 0, 0, DateTimeKind.Utc);
        var nowUtc = backgroundAt.AddSeconds(elapsedSeconds);

        var actual = AutoLockPolicy.ShouldLock(backgroundAt, nowUtc, timeoutSeconds);
        Assert.Equal(expectedLock, actual);
    }

    [Fact]
    public async Task LocalAuthService_EvaluateAutoLockOnResumeAsync_LocksAppWhenElapsedExceedsTimeout()
    {
        var dbPath = Path.Combine(_tempDir, "autolock.db3");
        var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        var settings = (await db.Settings.GetAllAsync()).First();
        settings.AutoLockTimeoutSeconds = 60;
        await db.Settings.UpdateAsync(settings);

        var biometricMock = new Mock<IBiometricService>();
        var t0 = new DateTimeOffset(2026, 3, 20, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(t0);

        var auth = new LocalAuthService(db, biometricMock.Object, clock);
        await auth.SetPinAsync("849205", enforceComplexity: true);
        auth.MarkAppUnlocked();
        Assert.True(auth.IsAppUnlocked);

        auth.RecordBackgrounded();

        // Resume after 20s (< 60s) -> remains unlocked
        clock.Advance(TimeSpan.FromSeconds(20));
        var lockedEarly = await auth.EvaluateAutoLockOnResumeAsync();
        Assert.False(lockedEarly);
        Assert.True(auth.IsAppUnlocked);

        // Background again and resume after 65s (>= 60s) -> locks app
        auth.RecordBackgrounded();
        clock.Advance(TimeSpan.FromSeconds(65));
        var lockedAfterTimeout = await auth.EvaluateAutoLockOnResumeAsync();
        Assert.True(lockedAfterTimeout);
        Assert.False(auth.IsAppUnlocked);
    }
}
