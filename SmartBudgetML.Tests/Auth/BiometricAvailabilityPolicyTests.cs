using SmartBudgetML.Services.Auth;
using Xunit;

namespace SmartBudgetML.Tests.Auth;

/// <summary>
/// N16 (lot 8C): biometrics must not be offered where AuthenticateAsync cannot succeed:
/// below Android 9 (API 28, no BiometricPrompt), without a fingerprint sensor, or without
/// an enrolled fingerprint.
/// </summary>
public class BiometricAvailabilityPolicyTests
{
    [Theory]
    [InlineData(24, true, null, true, false)]   // Android 7: no BiometricPrompt
    [InlineData(26, true, null, true, false)]   // Android 8
    [InlineData(27, true, null, true, false)]   // Android 8.1
    [InlineData(28, true, null, true, true)]    // Android 9, fingerprint enrolled
    [InlineData(28, true, null, false, false)]  // Android 9, nothing enrolled
    [InlineData(28, true, null, null, false)]   // Android 9, enrollment unknown
    [InlineData(28, false, null, true, false)]  // no sensor
    [InlineData(29, true, true, null, true)]    // Android 10+, BiometricManager says Success
    [InlineData(34, true, false, null, false)]  // Android 14, nothing enrolled
    [InlineData(34, true, null, null, false)]   // Android 14, manager unavailable
    public void IsAvailable_FollowsApiLevelSensorAndEnrollment(
        int apiLevel, bool hasFingerprintFeature, bool? biometricManagerSuccess, bool? hasEnrolledFingerprints, bool expected)
    {
        Assert.Equal(expected, BiometricAvailabilityPolicy.IsAvailable(apiLevel, hasFingerprintFeature, biometricManagerSuccess, hasEnrolledFingerprints));
    }
}
