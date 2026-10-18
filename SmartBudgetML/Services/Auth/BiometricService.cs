using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;

namespace SmartBudgetML.Services.Auth;

/// <summary>
/// Hardware biometric authentication service utilizing Plugin.Fingerprint.
/// Supports FaceID and TouchID on Android and iOS.
/// </summary>
public class BiometricService : IBiometricService
{
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            var status = await CrossFingerprint.Current.GetAvailabilityAsync();
            return status == FingerprintAvailability.Available;
        }
        catch
        {
            // Graceful fallback on non-supported platforms / emulators
            return false;
        }
    }

    public async Task<bool> AuthenticateAsync(string title, string reason)
    {
        try
        {
            var request = new AuthenticationRequestConfiguration(title, reason)
            {
                CancelTitle = "Cancel",
                FallbackTitle = "Use PIN"
            };

            var result = await CrossFingerprint.Current.AuthenticateAsync(request);
            return result.Authenticated;
        }
        catch
        {
            return false;
        }
    }
}
