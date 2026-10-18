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

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
