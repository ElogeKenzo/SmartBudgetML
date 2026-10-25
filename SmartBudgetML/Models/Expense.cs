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

    /// <summary>
    /// One-off income (a tontine pot, for example) left out of the income average so it does not
    /// inflate the irregular-income envelope.
    /// </summary>
    public bool IsExceptional { get; set; } = false;

    /// <summary>
    /// What created this row when the user did not type it (see <see cref="ExpenseSources"/>).
    /// Unique with <see cref="SourceId"/>, so the same tontine contribution cannot be booked twice.
    /// </summary>
    [MaxLength(40), Indexed(Name = "UX_Expenses_Source", Order = 1, Unique = true)]
    public string? SourceType { get; set; }

    [Indexed(Name = "UX_Expenses_Source", Order = 2, Unique = true)]
    public int? SourceId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Non-persisted UI helpers
    [Ignore]
    public string? CategoryName { get; set; }

    [Ignore]
    public string? CategoryColor { get; set; }

    [Ignore]
    public string? CategoryIcon { get; set; }
}

/// <summary>Stable values of <see cref="Expense.SourceType"/>.</summary>
public static class ExpenseSources
{
    /// <summary>SourceId = TontineContribution.Id.</summary>
    public const string TontineContribution = "tontine_contribution";

    /// <summary>SourceId = TontineRound.Id.</summary>
    public const string TontinePayout = "tontine_payout";
}
