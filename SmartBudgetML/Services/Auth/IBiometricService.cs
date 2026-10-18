namespace SmartBudgetML.Services.Auth;

/// <summary>
/// Abstraction over hardware biometric authentication (Fingerprint / FaceID).
/// Decouples Plugin.Fingerprint from business logic to enable unit testing with Moq.
/// </summary>
public interface IBiometricService
{
    Task<bool> IsAvailableAsync();
    Task<bool> AuthenticateAsync(string title, string reason);
}
