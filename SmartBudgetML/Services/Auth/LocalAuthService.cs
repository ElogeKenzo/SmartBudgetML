using System.Security.Cryptography;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.Security;

namespace SmartBudgetML.Services.Auth;

/// <summary>
/// Handles on-device local authentication, salted PBKDF2 PIN verification (OWASP 600,000 iterations),
/// persistent rate limiting with exponential backoff, auto-lock lifecycle, and biometric integration.
/// 100% Local-First with zero cloud communication.
/// </summary>
public class LocalAuthService : ILocalAuthService
{
    private readonly IDatabaseService _databaseService;
    private readonly IBiometricService _biometricService;
    private readonly TimeProvider _timeProvider;

    private const int SaltByteSize = 16;
    private const int HashByteSize = 32;
    public const int LegacyIterations = 10_000;
    public const int OwaspIterations = 600_000;
    public const int CurrentHashVersion = 2;

    private static readonly HashSet<string> TrivialPinBlacklist = new(StringComparer.Ordinal)
    {
        "0000", "1111", "2222", "3333", "4444", "5555", "6666", "7777", "8888", "9999",
        "1234", "2345", "3456", "4567", "5678", "6789", "7890", "0123",
        "4321", "5432", "6543", "7654", "8765", "9876", "3210",
        "1212", "1122", "2580", "0852",
        "000000", "111111", "222222", "333333", "444444", "555555", "666666", "777777", "888888", "999999",
        "123456", "234567", "345678", "456789", "567890", "012345",
        "654321", "543210", "987654", "121212", "112233", "123123"
    };

    public bool IsAppUnlocked { get; private set; } = false;
    public DateTime? LastBackgroundUtc { get; private set; }

    public LocalAuthService(
        IDatabaseService databaseService,
        IBiometricService biometricService,
        TimeProvider? timeProvider = null)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _biometricService = biometricService ?? throw new ArgumentNullException(nameof(biometricService));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void LockApp()
    {
        IsAppUnlocked = false;
    }

    public void MarkAppUnlocked()
    {
        IsAppUnlocked = true;
    }

    public void RecordBackgrounded(DateTime? utcNow = null)
    {
        LastBackgroundUtc = utcNow ?? _timeProvider.GetUtcNow().UtcDateTime;
    }

    public async Task<bool> EvaluateAutoLockOnResumeAsync(DateTime? utcNow = null)
    {
        if (!IsAppUnlocked)
        {
            return true;
        }

        var settings = await GetSettingsAsync();
        var now = utcNow ?? _timeProvider.GetUtcNow().UtcDateTime;

        if (AutoLockPolicy.ShouldLock(LastBackgroundUtc, now, settings.AutoLockTimeoutSeconds))
        {
            LockApp();
            return true;
        }

        return false;
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
        settings.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var result = await _databaseService.Settings.UpdateAsync(settings);
        return result > 0;
    }

    public bool ValidatePinComplexity(string pin, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(pin) || (pin.Length != 4 && pin.Length != 6) || !pin.All(char.IsDigit))
        {
            errorMessage = "PIN must be exactly 4 or 6 digits.";
            return false;
        }

        if (TrivialPinBlacklist.Contains(pin) || IsAllSameDigit(pin) || IsSequentialDigits(pin))
        {
            errorMessage = "This PIN is too easy to guess. Avoid repeated or sequential digits (e.g. 0000, 1234, 123456).";
            return false;
        }

        errorMessage = null;
        return true;
    }

    private static bool IsAllSameDigit(string pin)
    {
        for (int i = 1; i < pin.Length; i++)
        {
            if (pin[i] != pin[0]) return false;
        }
        return true;
    }

    private static bool IsSequentialDigits(string pin)
    {
        bool ascending = true;
        bool descending = true;
        for (int i = 1; i < pin.Length; i++)
        {
            if (pin[i] - pin[i - 1] != 1) ascending = false;
            if (pin[i - 1] - pin[i] != 1) descending = false;
        }
        return ascending || descending;
    }

    public Task<bool> SetPinAsync(string pin) =>
        SetPinAsync(pin, enforceComplexity: false);

    public async Task<bool> SetPinAsync(string pin, bool enforceComplexity)
    {
        if (string.IsNullOrWhiteSpace(pin) || pin.Length < 4)
        {
            throw new ArgumentException("PIN must be at least 4 digits.", nameof(pin));
        }

        if (enforceComplexity && !ValidatePinComplexity(pin, out var complexityError))
        {
            throw new ArgumentException(complexityError, nameof(pin));
        }

        var salt = GenerateSalt();
        var hash = HashPin(pin, salt, OwaspIterations);

        var settings = await GetSettingsAsync();
        settings.PinSalt = Convert.ToBase64String(salt);
        settings.PinHash = Convert.ToBase64String(hash);
        settings.PinHashVersion = CurrentHashVersion;
        settings.PinLength = pin.Length;
        settings.IsPinAuthEnabled = true;
        settings.FailedPinAttempts = 0;
        settings.LockoutUntilUtc = null;
        settings.LastFailedAttemptUtc = null;
        settings.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

        var result = await _databaseService.Settings.UpdateAsync(settings);
        return result > 0;
    }

    public async Task<bool> VerifyPinAsync(string pin)
    {
        var result = await VerifyPinWithStatusAsync(pin);
        return result.IsValid;
    }

    public async Task<PinVerificationResult> VerifyPinWithStatusAsync(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return new PinVerificationResult(false, false, TimeSpan.Zero, 0, "PIN is required.");
        }

        var settings = await GetSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.PinHash) || string.IsNullOrWhiteSpace(settings.PinSalt))
        {
            return new PinVerificationResult(false, false, TimeSpan.Zero, 0, "No PIN is configured.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        // 1. Detect clock rollback while locked or after failed attempts
        if (settings.LastFailedAttemptUtc.HasValue && nowUtc < settings.LastFailedAttemptUtc.Value)
        {
            var rollbackRemaining = settings.LockoutUntilUtc.HasValue && settings.LockoutUntilUtc.Value > settings.LastFailedAttemptUtc.Value
                ? settings.LockoutUntilUtc.Value - settings.LastFailedAttemptUtc.Value
                : TimeSpan.FromMinutes(15);

            return new PinVerificationResult(
                IsValid: false,
                IsLockedOut: true,
                RemainingLockout: rollbackRemaining,
                FailedAttempts: settings.FailedPinAttempts,
                ErrorMessage: "System clock rollback detected. Lockout remains active.");
        }

        // 2. Check active lockout window
        if (settings.LockoutUntilUtc.HasValue && nowUtc < settings.LockoutUntilUtc.Value)
        {
            var remaining = settings.LockoutUntilUtc.Value - nowUtc;
            return new PinVerificationResult(
                IsValid: false,
                IsLockedOut: true,
                RemainingLockout: remaining,
                FailedAttempts: settings.FailedPinAttempts,
                ErrorMessage: $"Too many failed attempts. Try again in {Math.Ceiling(remaining.TotalSeconds):0}s.");
        }

        // 3. Verify PBKDF2 hash using stored version's iteration count
        var salt = Convert.FromBase64String(settings.PinSalt);
        var expectedHash = Convert.FromBase64String(settings.PinHash);
        int iterations = settings.PinHashVersion <= 1 ? LegacyIterations : OwaspIterations;
        var actualHash = HashPin(pin, salt, iterations);

        var isValid = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        if (isValid)
        {
            settings.FailedPinAttempts = 0;
            settings.LockoutUntilUtc = null;
            settings.LastFailedAttemptUtc = null;

            // Transparent migration from v1 (10,000 iterations) to v2 (600,000 iterations)
            if (settings.PinHashVersion < CurrentHashVersion)
            {
                var newSalt = GenerateSalt();
                var newHash = HashPin(pin, newSalt, OwaspIterations);
                settings.PinSalt = Convert.ToBase64String(newSalt);
                settings.PinHash = Convert.ToBase64String(newHash);
                settings.PinHashVersion = CurrentHashVersion;
            }

            settings.UpdatedAt = nowUtc;
            await _databaseService.Settings.UpdateAsync(settings);

            IsAppUnlocked = true;
            return new PinVerificationResult(true, false, TimeSpan.Zero, 0);
        }

        // 4. Failed attempt: increment counter and apply exponential backoff if threshold reached
        settings.FailedPinAttempts += 1;
        settings.LastFailedAttemptUtc = nowUtc;

        var lockoutDuration = GetLockoutDuration(settings.FailedPinAttempts);
        if (lockoutDuration > TimeSpan.Zero)
        {
            settings.LockoutUntilUtc = nowUtc.Add(lockoutDuration);
        }
        else
        {
            settings.LockoutUntilUtc = null;
        }

        settings.UpdatedAt = nowUtc;
        await _databaseService.Settings.UpdateAsync(settings);

        bool isNowLocked = lockoutDuration > TimeSpan.Zero;
        string msg = isNowLocked
            ? $"Too many failed attempts. Locked for {Math.Ceiling(lockoutDuration.TotalSeconds):0}s."
            : "Incorrect PIN.";

        return new PinVerificationResult(
            IsValid: false,
            IsLockedOut: isNowLocked,
            RemainingLockout: lockoutDuration,
            FailedAttempts: settings.FailedPinAttempts,
            ErrorMessage: msg);
    }

    public async Task<TimeSpan?> GetRemainingLockoutTimeAsync()
    {
        var settings = await GetSettingsAsync();
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        if (settings.LastFailedAttemptUtc.HasValue && nowUtc < settings.LastFailedAttemptUtc.Value && settings.LockoutUntilUtc.HasValue)
        {
            return settings.LockoutUntilUtc.Value - settings.LastFailedAttemptUtc.Value;
        }

        if (settings.LockoutUntilUtc.HasValue && nowUtc < settings.LockoutUntilUtc.Value)
        {
            return settings.LockoutUntilUtc.Value - nowUtc;
        }

        return null;
    }

    public static TimeSpan GetLockoutDuration(int failedAttempts)
    {
        if (failedAttempts < 5) return TimeSpan.Zero;
        if (failedAttempts == 5) return TimeSpan.FromSeconds(30);
        if (failedAttempts == 6) return TimeSpan.FromMinutes(1);
        if (failedAttempts == 7) return TimeSpan.FromMinutes(5);
        return TimeSpan.FromMinutes(15);
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

        var existingProfile = (await _databaseService.UserProfiles.GetAllAsync()).FirstOrDefault();
        var profile = existingProfile ?? new UserProfile();

        profile.FullName = fullName.Trim();
        profile.CurrencySymbol = string.IsNullOrWhiteSpace(currencySymbol) ? "FCFA" : currencySymbol.Trim();
        profile.MonthlyIncome = monthlyIncome;
        profile.MonthlySavingsGoal = monthlySavingsGoal;
        profile.IsOnboardingCompleted = true;
        profile.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

        if (existingProfile == null)
        {
            await _databaseService.UserProfiles.InsertAsync(profile);
        }
        else
        {
            await _databaseService.UserProfiles.UpdateAsync(profile);
        }

        await SetPinAsync(pin, enforceComplexity: false);

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
        profile.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
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
                CurrencyCode = "XAF",
                Language = "fr",
                PinHashVersion = CurrentHashVersion,
                PinLength = 6,
                IsScreenProtectionEnabled = true
            };
            await _databaseService.Settings.InsertAsync(settings);
        }
        return settings;
    }

    private static byte[] GenerateSalt()
    {
        return RandomNumberGenerator.GetBytes(SaltByteSize);
    }

    private static byte[] HashPin(string pin, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashByteSize);
    }
}
