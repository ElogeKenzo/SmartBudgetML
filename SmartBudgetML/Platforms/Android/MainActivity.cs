using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Maui.ApplicationModel;

namespace SmartBudgetML;

/// <summary>
/// Main Android Activity. Applies WindowManagerFlags.Secure (FLAG_SECURE) by default
/// to protect financial screens in the recent-apps switcher and prevent screenshots.
/// Labeled NON-VÉRIFIÉ-APPAREIL for physical Android screen-capture hardware verification.
/// </summary>
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetScreenProtection(true);
    }

    public static void SetScreenProtection(bool enabled)
    {
        var activity = Platform.CurrentActivity;
        var window = activity?.Window;
        if (window == null)
        {
            return;
        }

        if (enabled)
        {
            window.AddFlags(WindowManagerFlags.Secure);
        }
        else
        {
            window.ClearFlags(WindowManagerFlags.Secure);
        }
    }
}
