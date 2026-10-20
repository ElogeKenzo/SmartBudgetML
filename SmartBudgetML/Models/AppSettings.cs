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

    /// <summary>
    /// Whether FaceID / TouchID biometric unlock is active.
    /// Configurable toggle in App Settings per system prompt.
    /// </summary>
    public bool IsBiometricEnabled { get; set; } = false;

    /// <summary>
    /// Whether PIN unlock is required on app launch.
    /// </summary>
    public bool IsPinAuthEnabled { get; set; } = true;

    /// <summary>
    /// PBKDF2/SHA256 salted hash of the user's PIN.
    /// </summary>
    [MaxLength(256)]
    public string? PinHash { get; set; }

    /// <summary>
    /// Cryptographic salt used for PIN hashing.
    /// </summary>
    [MaxLength(128)]
    public string? PinSalt { get; set; }

    /// <summary>
    /// UI Theme preference (Dark theme default: Deep Indigo / Cyan).
    /// </summary>
    public bool DarkMode { get; set; } = true;

    [MaxLength(10)]
    public string CurrencyCode { get; set; } = "USD";

    /// <summary>
    /// Seconds of inactivity before locking the app. 0 = immediately on background.
    /// </summary>
    public int AutoLockTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Minimum ML confidence threshold (0.0 to 1.0) required to auto-select a predicted category.
    /// </summary>
    public float MlConfidenceThreshold { get; set; } = 0.70f;

    /// <summary>
    /// Whether automatic ML category prediction is active when entering expenses.
    /// </summary>
    public bool AutoCategorizeEnabled { get; set; } = true;

    /// <summary>
    /// Whether over-budget alerts are enabled.
    /// </summary>
    public bool OverBudgetAlertsEnabled { get; set; } = true;

    /// <summary>
    /// Whether daily expense logging reminders are enabled.
    /// </summary>
    public bool DailyRemindersEnabled { get; set; } = false;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
