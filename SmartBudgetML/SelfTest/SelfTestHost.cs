#if SB_SELFTEST
using SmartBudgetML.Services.Auth;
using SmartBudgetML.Services.Security;
using SmartBudgetML.Services.SelfTest;

namespace SmartBudgetML.SelfTest;

/// <summary>
/// Starts <see cref="SelfTestRunner"/> once per process, in the background, on the CI
/// emulator build (EnableSelfTest=true). Output goes to logcat with the SB_SELFTEST tag.
/// </summary>
public static class SelfTestHost
{
    private static int _started;

    public static void StartOnce()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        _ = Task.Run(RunAsync);
    }

    private static async Task RunAsync()
    {
        try
        {
            var workDirectory = Path.Combine(FileSystem.AppDataDirectory, "selftest");
            var runner = new SelfTestRunner(new MauiSecureKeyStore(), new NoBiometrics(), workDirectory, Write);
            await runner.RunAsync();
        }
        catch (Exception ex)
        {
            Write($"{SelfTestRunner.Tag}|FATAL|{ex.GetType().Name}");
            Write($"{SelfTestRunner.Tag}|DONE|0/1");
        }
    }

    private static void Write(string line)
    {
#if ANDROID
        Android.Util.Log.Info(SelfTestRunner.Tag, line);
#else
        System.Diagnostics.Debug.WriteLine(line);
#endif
    }

    // The self-test never shows a biometric prompt.
    private sealed class NoBiometrics : IBiometricService
    {
        public Task<bool> IsAvailableAsync() => Task.FromResult(false);
        public Task<bool> AuthenticateAsync(string title, string reason) => Task.FromResult(false);
    }
}
#endif
