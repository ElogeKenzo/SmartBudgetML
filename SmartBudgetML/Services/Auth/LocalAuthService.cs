using System.Security.Cryptography;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Database;

namespace SmartBudgetML.Services.Auth;

/// <summary>
/// Handles on-device local authentication, salted PIN verification,
/// biometric integration, and user onboarding profile management.
/// 100% Local-First with zero cloud communication.
/// </summary>
public class LocalAuthService : ILocalAuthService
{
    private readonly IDatabaseService _databaseService;
    private readonly IBiometricService _biometricService;

    private const int SaltByteSize = 16;
    private const int HashByteSize = 32;
    private const int Iterations = 10_000;

    public bool IsAppUnlocked { get; private set; } = false;

    public LocalAuthService(
        IDatabaseService databaseService,
        IBiometricService biometricService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _biometricService = biometricService ?? throw new ArgumentNullException(nameof(biometricService));
    }

    public void LockApp()
    {
        IsAppUnlocked = false;
    }

    public void MarkAppUnlocked()
    {
        IsAppUnlocked = true;
    }

    public async Task<bool> IsUserRegisteredAsync()
    {
        await _databaseService.InitializeAsync();
        var profile = await _databaseService.UserProfiles.FirstOrDefaultAsync(u => u.IsOnboardingCompleted);
        return profile != null;
    }

    public async Task<bool> IsPinSetAsync()
    {
        var settings = await GetSettingsAsync();
        return !string.IsNullOrWhiteSpace(settings.PinHash) && !string.IsNullOrWhiteSpace(settings.PinSalt);
    }

    public async Task<bool> IsBiometricAvailableAsync()
    {
        return await _biometricService.IsAvailableAsync();
    }

    public async Task<bool> IsBiometricEnabledAsync()
    {
        var settings = await GetSettingsAsync();
        return settings.IsBiometricEnabled;
    }

    public async Task<bool> SetBiometricEnabledAsync(bool enabled)
    {
        var settings = await GetSettingsAsync();
        settings.IsBiometricEnabled = enabled;
        settings.UpdatedAt = DateTime.UtcNow;
        var result = await _databaseService.Settings.UpdateAsync(settings);
        return result > 0;
    }

    public async Task<bool> SetPinAsync(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4)
        {
            throw new ArgumentException("PIN must be at least 4 digits.", nameof(pin));
        }

        var salt = GenerateSalt();
        var hash = HashPin(pin, salt);

        var settings = await GetSettingsAsync();
        settings.PinSalt = Convert.ToBase64String(salt);
        settings.PinHash = Convert.ToBase64String(hash);
        settings.IsPinAuthEnabled = true;
        settings.UpdatedAt = DateTime.UtcNow;

        var result = await _databaseService.Settings.UpdateAsync(settings);
        return result > 0;
    }

    public async Task<bool> VerifyPinAsync(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return false;

        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.PinHash) || string.IsNullOrWhiteSpace(settings.PinSalt))
        {
            return false;
        }

        var salt = Convert.FromBase64String(settings.PinSalt);
        var expectedHash = Convert.FromBase64String(settings.PinHash);
        var actualHash = HashPin(pin, salt);

        // Constant-time comparison to prevent timing attacks
        var isValid = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        if (isValid)
        {
            IsAppUnlocked = true;
        }

        return isValid;
    }

    public async Task<bool> AuthenticateWithBiometricsAsync(string reason = "Unlock SmartBudget ML")
    {
        var isEnabled = await IsBiometricEnabledAsync();
        if (!isEnabled) return false;

        var isAvailable = await _biometricService.IsAvailableAsync();
        if (!isAvailable) return false;

        var authenticated = await _biometricService.AuthenticateAsync("SmartBudget ML", reason);
        if (authenticated)
        {
            IsAppUnlocked = true;
        }

        return authenticated;
    }

    public async Task<UserProfile> CompleteOnboardingAsync(
        string fullName,
        string currencySymbol,
        decimal monthlyIncome,
        string pin,
        bool enableBiometrics,
        decimal monthlySavingsGoal = 0m)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Full name is required.", nameof(fullName));
        }

        await _databaseService.InitializeAsync();

        // 1. Save or update UserProfile
        var existingProfile = (await _databaseService.UserProfiles.GetAllAsync()).FirstOrDefault();
        var profile = existingProfile ?? new UserProfile();

        profile.FullName = fullName.Trim();
        profile.CurrencySymbol = string.IsNullOrWhiteSpace(currencySymbol) ? "$" : currencySymbol.Trim();
        profile.MonthlyIncome = monthlyIncome;
        profile.MonthlySavingsGoal = monthlySavingsGoal;
        profile.IsOnboardingCompleted = true;
        profile.UpdatedAt = DateTime.UtcNow;

        if (existingProfile == null)
        {
            await _databaseService.UserProfiles.InsertAsync(profile);
        }
        else
        {
            await _databaseService.UserProfiles.UpdateAsync(profile);
        }

        // 2. Set PIN
        await SetPinAsync(pin);

        // 3. Configure Biometrics
        if (enableBiometrics && await _biometricService.IsAvailableAsync())
        {
            await SetBiometricEnabledAsync(true);
        }
        else
        {
            await SetBiometricEnabledAsync(false);
        }

        IsAppUnlocked = true;
        return profile;
    }

    public async Task<UserProfile?> GetUserProfileAsync()
    {
        await _databaseService.InitializeAsync();
        return (await _databaseService.UserProfiles.GetAllAsync()).FirstOrDefault();
    }

    public async Task<bool> UpdateUserProfileAsync(UserProfile profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        await _databaseService.InitializeAsync();
        profile.UpdatedAt = DateTime.UtcNow;
        var result = await _databaseService.UserProfiles.UpdateAsync(profile);
        return result > 0;
    }

    public async Task<AppSettings> GetSettingsAsync()
    {
        await _databaseService.InitializeAsync();
        var settings = (await _databaseService.Settings.GetAllAsync()).FirstOrDefault();
        if (settings == null)
        {
            settings = new AppSettings
            {
                IsBiometricEnabled = false,
                IsPinAuthEnabled = false,
                DarkMode = true,
                CurrencyCode = "USD"
            };
            await _databaseService.Settings.InsertAsync(settings);
        }
        return settings;
    }

    #region Security Helpers

    private static byte[] GenerateSalt()
    {
        var salt = new byte[SaltByteSize];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        return salt;
    }

    private static byte[] HashPin(string pin, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashByteSize);
    }

    #endregion
}
