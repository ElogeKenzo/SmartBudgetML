namespace SmartBudgetML.Services.Auth;

/// <summary>
/// Pure decision behind BiometricService.IsAvailableAsync (N16). Biometrics are offered only
/// where BiometricService.AuthenticateAsync can succeed: BiometricPrompt needs API 28, a
/// fingerprint sensor, and an enrolled fingerprint.
/// </summary>
public static class BiometricAvailabilityPolicy
{
    public const int MinimumApiLevel = 28;
    public const int BiometricManagerApiLevel = 29;

    /// <param name="apiLevel">Android API level of the device.</param>
    /// <param name="hasFingerprintFeature">PackageManager.FeatureFingerprint.</param>
    /// <param name="biometricManagerSuccess">API 29+: BiometricManager.CanAuthenticate() == Success; null when not queried.</param>
    /// <param name="hasEnrolledFingerprints">API 28: FingerprintManager.HasEnrolledFingerprints; null when unknown.</param>
    public static bool IsAvailable(int apiLevel, bool hasFingerprintFeature, bool? biometricManagerSuccess, bool? hasEnrolledFingerprints)
    {
        if (apiLevel < MinimumApiLevel || !hasFingerprintFeature)
        {
            return false;
        }

        return apiLevel >= BiometricManagerApiLevel
            ? biometricManagerSuccess == true
            : hasEnrolledFingerprints == true;
    }
}
