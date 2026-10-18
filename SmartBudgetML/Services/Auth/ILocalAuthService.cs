using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Auth;

/// <summary>
/// Service contract for local on-device authentication, PIN management,
/// biometric unlock, and user profile onboarding.
/// </summary>
public interface ILocalAuthService
{
    bool IsAppUnlocked { get; }
    void LockApp();
    void MarkAppUnlocked();

    Task<bool> IsUserRegisteredAsync();
    Task<bool> IsPinSetAsync();
    Task<bool> IsBiometricAvailableAsync();
    Task<bool> IsBiometricEnabledAsync();
    Task<bool> SetBiometricEnabledAsync(bool enabled);

    Task<bool> SetPinAsync(string pin);
    Task<bool> VerifyPinAsync(string pin);
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
