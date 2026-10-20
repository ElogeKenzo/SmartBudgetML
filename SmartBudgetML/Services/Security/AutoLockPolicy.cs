namespace SmartBudgetML.Services.Security;

/// <summary>
/// Pure, deterministic auto-lock decision policy decoupled from MAUI UI lifecycle.
/// </summary>
public static class AutoLockPolicy
{
    public static bool ShouldLock(DateTime? lastBackgroundUtc, DateTime nowUtc, int timeoutSeconds)
    {
        if (!lastBackgroundUtc.HasValue)
        {
            return false;
        }

        if (timeoutSeconds <= 0)
        {
            return true;
        }

        // Fail-safe: if system clock moved backward, lock immediately
        if (nowUtc < lastBackgroundUtc.Value)
        {
            return true;
        }

        var elapsedSeconds = (nowUtc - lastBackgroundUtc.Value).TotalSeconds;
        return elapsedSeconds >= timeoutSeconds;
    }
}
