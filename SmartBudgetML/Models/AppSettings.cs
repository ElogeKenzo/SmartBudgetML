using SQLite;

namespace SmartBudgetML.Models;

/// <summary>
/// Persists app security settings, authentication state, and UI preferences.
/// </summary>
[Table("AppSettings")]
public class AppSettings : IEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public bool IsBiometricEnabled { get; set; } = false;

    public bool IsPinAuthEnabled { get; set; } = true;

    [MaxLength(256)]
    public string? PinHash { get; set; }

    [MaxLength(128)]
    public string? PinSalt { get; set; }

    public int PinHashVersion { get; set; } = 1;

    public int FailedPinAttempts { get; set; } = 0;

    public DateTime? LockoutUntilUtc { get; set; }

    public DateTime? LastFailedAttemptUtc { get; set; }

    public int PinLength { get; set; } = 6;

    public bool IsScreenProtectionEnabled { get; set; } = true;

    public bool DarkMode { get; set; } = true;

    [MaxLength(10)]
    public string CurrencyCode { get; set; } = "XAF";

    [MaxLength(10)]
    public string Language { get; set; } = "fr";

    public int AutoLockTimeoutSeconds { get; set; } = 300;

    public float MlConfidenceThreshold { get; set; } = 0.70f;

    public bool AutoCategorizeEnabled { get; set; } = true;

    public bool OverBudgetAlertsEnabled { get; set; } = true;

    public bool DailyRemindersEnabled { get; set; } = false;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
