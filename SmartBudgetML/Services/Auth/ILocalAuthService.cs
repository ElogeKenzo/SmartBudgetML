using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Auth;

public record PinVerificationResult(
    bool IsValid,
    bool IsLockedOut,
    TimeSpan RemainingLockout,
    int FailedAttempts,
    string? ErrorMessage = null);

/// <summary>
/// Service contract for local on-device authentication, PIN management,
/// rate limiting, biometric unlock, and user profile onboarding.
/// </summary>
public interface ILocalAuthService
{
    bool IsAppUnlocked { get; }
    DateTime? LastBackgroundUtc { get; }

    /// <summary>Raised when the app becomes locked (manual lock or auto-lock on resume).</summary>
    event EventHandler? LockStateChanged;

    void LockApp();
    void MarkAppUnlocked();
    void RecordBackgrounded(DateTime? utcNow = null);
    Task<bool> EvaluateAutoLockOnResumeAsync(DateTime? utcNow = null);

    Task<bool> IsUserRegisteredAsync();
    Task<bool> IsPinSetAsync();
    Task<bool> IsBiometricAvailableAsync();
    Task<bool> IsBiometricEnabledAsync();
    Task<bool> SetBiometricEnabledAsync(bool enabled);

    bool ValidatePinComplexity(string pin, out string? errorMessage);
    Task<bool> SetPinAsync(string pin);
    Task<bool> SetPinAsync(string pin, bool enforceComplexity);
    Task<bool> VerifyPinAsync(string pin);
    Task<PinVerificationResult> VerifyPinWithStatusAsync(string pin);
    Task<TimeSpan?> GetRemainingLockoutTimeAsync();
    Task<bool> AuthenticateWithBiometricsAsync(string reason = "Unlock SmartBudget ML");

    Task<UserProfile> CompleteOnboardingAsync(
        string fullName,
        string currencySymbol,
        decimal monthlyIncome,
        string pin,
        bool enableBiometrics,
        decimal monthlySavingsGoal = 0m);

    Task<UserProfile?> GetUserProfileAsync();
    Task<bool> UpdateUserProfileAsync(UserProfile profile);
    Task<AppSettings> GetSettingsAsync();
}
