using Moq;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Auth;
using SmartBudgetML.Services.Database;
using Xunit;

namespace SmartBudgetML.Tests.Auth;

public class LocalAuthServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly DatabaseService _databaseService;
    private readonly Mock<IBiometricService> _mockBiometrics;
    private readonly LocalAuthService _authService;

    public LocalAuthServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"auth_test_{Guid.NewGuid():N}.db3");
        _databaseService = new DatabaseService(_testDbPath);
        _mockBiometrics = new Mock<IBiometricService>();

        _authService = new LocalAuthService(_databaseService, _mockBiometrics.Object);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
            // Ignore file lock cleanup on test teardown
        }
    }

    [Fact]
    public async Task IsUserRegisteredAsync_ShouldReturnFalse_WhenNoProfileExists()
    {
        // Act
        var isRegistered = await _authService.IsUserRegisteredAsync();

        // Assert
        Assert.False(isRegistered);
    }

    [Fact]
    public async Task SetPinAsync_ShouldStoreSaltAndHash()
    {
        // Act
        var result = await _authService.SetPinAsync("1234");
        var settings = await _authService.GetSettingsAsync();

        // Assert
        Assert.True(result);
        Assert.NotNull(settings.PinHash);
        Assert.NotNull(settings.PinSalt);
        Assert.NotEqual("1234", settings.PinHash); // Must never store plaintext PIN
        Assert.True(settings.IsPinAuthEnabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("123")]
    public async Task SetPinAsync_ShouldThrow_WhenPinIsTooShort(string invalidPin)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _authService.SetPinAsync(invalidPin));
    }

    [Fact]
    public async Task VerifyPinAsync_ShouldReturnTrue_ForCorrectPin()
    {
        // Arrange
        await _authService.SetPinAsync("9876");

        // Act
        var isValid = await _authService.VerifyPinAsync("9876");

        // Assert
        Assert.True(isValid);
        Assert.True(_authService.IsAppUnlocked);
    }

    [Fact]
    public async Task VerifyPinAsync_ShouldReturnFalse_ForIncorrectPin()
    {
        // Arrange
        await _authService.SetPinAsync("9876");

        // Act
        var isValid = await _authService.VerifyPinAsync("0000");

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void LockApp_ShouldResetAppUnlockedFlag()
    {
        // Arrange
        _authService.MarkAppUnlocked();
        Assert.True(_authService.IsAppUnlocked);

        // Act
        _authService.LockApp();

        // Assert
        Assert.False(_authService.IsAppUnlocked);
    }

    [Fact]
    public async Task SetBiometricEnabledAsync_ShouldPersistSetting()
    {
        // Act
        var success = await _authService.SetBiometricEnabledAsync(true);
        var isEnabled = await _authService.IsBiometricEnabledAsync();

        // Assert
        Assert.True(success);
        Assert.True(isEnabled);
    }

    [Fact]
    public async Task AuthenticateWithBiometricsAsync_ShouldSucceed_WhenEnabledAndHardwareConfirms()
    {
        // Arrange
        await _authService.SetBiometricEnabledAsync(true);
        _mockBiometrics.Setup(b => b.IsAvailableAsync()).ReturnsAsync(true);
        _mockBiometrics.Setup(b => b.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        // Act
        var result = await _authService.AuthenticateWithBiometricsAsync();

        // Assert
        Assert.True(result);
        Assert.True(_authService.IsAppUnlocked);
    }

    [Fact]
    public async Task AuthenticateWithBiometricsAsync_ShouldFail_WhenDisabledInSettings()
    {
        // Arrange - biometrics disabled in settings
        await _authService.SetBiometricEnabledAsync(false);
        _mockBiometrics.Setup(b => b.IsAvailableAsync()).ReturnsAsync(true);

        // Act
        var result = await _authService.AuthenticateWithBiometricsAsync();

        // Assert
        Assert.False(result);
        _mockBiometrics.Verify(b => b.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CompleteOnboardingAsync_ShouldConfigureUserAndUnlockApp()
    {
        // Arrange
        _mockBiometrics.Setup(b => b.IsAvailableAsync()).ReturnsAsync(true);

        // Act
        var profile = await _authService.CompleteOnboardingAsync(
            fullName: "Sarah Connor",
            currencySymbol: "$",
            monthlyIncome: 5000m,
            pin: "4321",
            enableBiometrics: true,
            monthlySavingsGoal: 1000m);

        var isRegistered = await _authService.IsUserRegisteredAsync();
        var isBiometricEnabled = await _authService.IsBiometricEnabledAsync();
        var isPinValid = await _authService.VerifyPinAsync("4321");

        // Assert
        Assert.NotNull(profile);
        Assert.Equal("Sarah Connor", profile.FullName);
        Assert.Equal(5000m, profile.MonthlyIncome);
        Assert.Equal(1000m, profile.MonthlySavingsGoal);
        Assert.True(isRegistered);
        Assert.True(isBiometricEnabled);
        Assert.True(isPinValid);
        Assert.True(_authService.IsAppUnlocked);
    }
}
