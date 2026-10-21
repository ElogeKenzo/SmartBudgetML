using SQLite;

namespace SmartBudgetML.Models;

/// <summary>
/// Represents an expense or income category identified by a language-independent stable Key.
/// </summary>
[Table("Categories")]
public class Category : IEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Language-independent stable category identifier (e.g. "food_market", "transport", "tontine").
    /// </summary>
    [MaxLength(50), Indexed]
    public string Key { get; set; } = string.Empty;

    [NotNull, MaxLength(100), Indexed]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Icon { get; set; } = "tag";

    [MaxLength(16)]
    public string ColorHex { get; set; } = "#6366F1";

    public decimal BudgetLimit { get; set; } = 0m;

    public bool IsIncome { get; set; } = false;

    public bool IsDefault { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
