using SQLite;

namespace SmartBudgetML.Models;

/// <summary>
/// Represents a financial transaction (Expense or Income).
/// Serves as the primary data point for ML categorization and forecasting.
/// </summary>
[Table("Expenses")]
public class Expense : IEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, MaxLength(255), Indexed]
    public string Description { get; set; } = string.Empty;

    [NotNull, Indexed]
    public decimal Amount { get; set; }

    [NotNull, Indexed]
    public DateTime Date { get; set; } = DateTime.Today;

    [Indexed]
    public int CategoryId { get; set; }

    public int? PredictedCategoryId { get; set; }

    public bool IsMlPredicted { get; set; } = false;

    public float? PredictionConfidence { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsIncome { get; set; } = false;

    /// <summary>
    /// Indicates whether this expense is a known fixed/calendar expense (e.g., monthly rent, school tuition)
    /// that should be accounted for at its actual amount rather than linearly extrapolated daily.
    /// </summary>
    public bool IsFixedOrRecurring { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Non-persisted UI helpers
    [Ignore]
    public string? CategoryName { get; set; }

    [Ignore]
    public string? CategoryColor { get; set; }

    [Ignore]
    public string? CategoryIcon { get; set; }
}
