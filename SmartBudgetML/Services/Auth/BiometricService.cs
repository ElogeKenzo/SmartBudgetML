using Microsoft.Extensions.Logging;
#if ANDROID
using Android.Content.PM;
using Android.Hardware.Biometrics;
using Android.OS;
using Microsoft.Maui.ApplicationModel;
#endif

namespace SmartBudgetML.Services.Auth;

/// <summary>
/// Hardware biometric authentication service using native Android BiometricPrompt APIs
/// with zero beta dependencies.
/// </summary>
public class BiometricService : IBiometricService
{
    private readonly ILogger<BiometricService>? _logger;

    public BiometricService(ILogger<BiometricService>? logger = null)
    {
        _logger = logger;
    }

    public Task<bool> IsAvailableAsync()
    {
        try
        {
#if ANDROID
            var context = Platform.AppContext;
            if (context?.PackageManager == null)
            {
                return Task.FromResult(false);
            }

            var apiLevel = (int)Build.VERSION.SdkInt;
            bool hasFingerprintFeature = context.PackageManager.HasSystemFeature(PackageManager.FeatureFingerprint);
            bool? biometricManagerSuccess = null;
            bool? hasEnrolledFingerprints = null;

            if (hasFingerprintFeature && OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                var bm = context.GetSystemService(Android.Content.Context.BiometricService) as BiometricManager;
                if (bm != null)
                {
#pragma warning disable CA1422
                    biometricManagerSuccess = bm.CanAuthenticate() == BiometricCode.Success;
#pragma warning restore CA1422
                }
            }
            else if (hasFingerprintFeature && OperatingSystem.IsAndroidVersionAtLeast(28))
            {
#pragma warning disable CA1422, CS0618
                var fm = context.GetSystemService(Android.Content.Context.FingerprintService) as Android.Hardware.Fingerprints.FingerprintManager;
                hasEnrolledFingerprints = fm?.IsHardwareDetected == true && fm.HasEnrolledFingerprints;
#pragma warning restore CA1422, CS0618
            }

            return Task.FromResult(BiometricAvailabilityPolicy.IsAvailable(
                apiLevel, hasFingerprintFeature, biometricManagerSuccess, hasEnrolledFingerprints));
#else
            return Task.FromResult(false);
#endif
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Biometric availability check failed; falling back to unavailable.");
            return Task.FromResult(false);
        }
    }

    public async Task<bool> AuthenticateAsync(string title, string reason)
    {
        try
        {
#if ANDROID
            if (!OperatingSystem.IsAndroidVersionAtLeast(28))
            {
                return false;
            }

            var activity = Platform.CurrentActivity;
            var executor = activity?.MainExecutor;
            if (activity == null || executor == null)
            {
                return false;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellationSignal = new CancellationSignal();

            var promptBuilder = new BiometricPrompt.Builder(activity)
                .SetTitle(string.IsNullOrWhiteSpace(title) ? "SmartBudget ML" : title)
                .SetDescription(string.IsNullOrWhiteSpace(reason) ? "Authenticate" : reason);

            promptBuilder.SetNegativeButton(
                "Use PIN",
                executor,
                new DialogClickListener(() => tcs.TrySetResult(false)));

            var prompt = promptBuilder.Build();
            var callback = new BiometricAuthCallback(tcs);
            prompt.Authenticate(cancellationSignal, executor, callback);

            return await tcs.Task;
#else
            await Task.CompletedTask;
            return false;
#endif
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Biometric authentication failed.");
            return false;
        }
    }

#if ANDROID
    [System.Runtime.Versioning.SupportedOSPlatform("android28.0")]
    private sealed class BiometricAuthCallback : BiometricPrompt.AuthenticationCallback
    {
        private readonly TaskCompletionSource<bool> _tcs;

        public BiometricAuthCallback(TaskCompletionSource<bool> tcs)
        {
            _tcs = tcs;
        }

        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result)
        {
            base.OnAuthenticationSucceeded(result);
            _tcs.TrySetResult(true);
        }

        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString)
        {
            base.OnAuthenticationError(errorCode, errString);
            _tcs.TrySetResult(false);
        }

        public override void OnAuthenticationFailed()
        {
            base.OnAuthenticationFailed();
        }
    }

    private sealed class DialogClickListener : Java.Lang.Object, Android.Content.IDialogInterfaceOnClickListener
    {
        private readonly Action _onClick;

        public DialogClickListener(Action onClick)
        {
            _onClick = onClick;
        }

        public void OnClick(Android.Content.IDialogInterface? dialog, int which)
        {
            _onClick();
        }
    }
#endif
}
