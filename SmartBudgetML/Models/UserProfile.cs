using SQLite;

namespace SmartBudgetML.Models;

/// <summary>
/// Represents the user's local profile and financial preferences.
/// Stored 100% locally on the device (Edge Computing).
/// </summary>
[Table("UserProfiles")]
public class UserProfile : IEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(10)]
    public string CurrencySymbol { get; set; } = "FCFA";

    public decimal MonthlyIncome { get; set; } = 0m;

    public decimal MonthlySavingsGoal { get; set; } = 0m;

    public double TargetSavingsRate { get; set; } = 20.0; // 20% default

    public bool IsOnboardingCompleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
