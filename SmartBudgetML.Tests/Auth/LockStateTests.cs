using Moq;
using SmartBudgetML.Services.Auth;
using SmartBudgetML.Services.Database;
using Xunit;

namespace SmartBudgetML.Tests.Auth;

/// <summary>
/// N3 (lot 8C): locking must reach the screen. The service raises LockStateChanged,
/// and LockNavigationCoordinator decides whether MainLayout navigates to /unlock.
/// </summary>
public class LockStateTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"lockstate_test_{Guid.NewGuid():N}.db3");
    private DatabaseService _db = null!;

    public Task InitializeAsync()
    {
        _db = new DatabaseService(_dbPath);
        return _db.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        var conn = await _db.GetConnectionAsync();
        await conn.CloseAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            File.Delete(_dbPath + suffix);
        }
        Assert.False(File.Exists(_dbPath));
    }

    [Fact]
    public void LockApp_RaisesLockStateChanged()
    {
        var auth = new LocalAuthService(_db, Mock.Of<IBiometricService>());
        auth.MarkAppUnlocked();
        var raised = 0;
        auth.LockStateChanged += (_, _) => raised++;

        auth.LockApp();

        Assert.Equal(1, raised);
        Assert.False(auth.IsAppUnlocked);
    }

    [Fact]
    public async Task EvaluateAutoLockOnResume_RaisesLockStateChanged_WhenPolicyLocks()
    {
        var settings = (await _db.Settings.GetAllAsync()).First();
        settings.AutoLockTimeoutSeconds = 60;
        await _db.Settings.UpdateAsync(settings);

        var auth = new LocalAuthService(_db, Mock.Of<IBiometricService>());
        auth.MarkAppUnlocked();
        var t0 = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc);
        auth.RecordBackgrounded(t0);
        var raised = 0;
        auth.LockStateChanged += (_, _) => raised++;

        var notYet = await auth.EvaluateAutoLockOnResumeAsync(t0.AddSeconds(30));
        Assert.False(notYet);
        Assert.Equal(0, raised);

        var locked = await auth.EvaluateAutoLockOnResumeAsync(t0.AddSeconds(61));
        Assert.True(locked);
        Assert.Equal(1, raised);
    }

    [Theory]
    [InlineData("", false, "unlock")]
    [InlineData("expenses", false, "unlock")]
    [InlineData("unlock", false, null)]
    [InlineData("UNLOCK", false, null)]
    [InlineData("onboarding", false, null)]
    [InlineData("vault-error", false, null)]
    [InlineData("expenses", true, null)]
    public void LockNavigationCoordinator_RedirectsOnlyWhenLockedAndNotAlreadyThere(
        string relativePath, bool isUnlocked, string? expected)
    {
        Assert.Equal(expected, LockNavigationCoordinator.GetRedirect(relativePath, isUnlocked));
    }
}
