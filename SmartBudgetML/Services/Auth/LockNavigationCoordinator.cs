namespace SmartBudgetML.Services.Auth;

/// <summary>
/// Pure decision used by MainLayout when the lock state changes: returns the route to
/// navigate to, or null when no navigation is needed (already unlocked, or already on a
/// page that must stay reachable while locked).
/// </summary>
public static class LockNavigationCoordinator
{
    public const string UnlockRoute = "unlock";

    private static readonly string[] RoutesAllowedWhileLocked = { "unlock", "onboarding", "vault-error" };

    public static string? GetRedirect(string relativePath, bool isAppUnlocked)
    {
        if (isAppUnlocked)
        {
            return null;
        }

        var route = (relativePath ?? string.Empty).Split('?', '#')[0].Trim('/');
        foreach (var allowed in RoutesAllowedWhileLocked)
        {
            if (string.Equals(route, allowed, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return UnlockRoute;
    }
}
