using SQLite;

namespace SmartBudgetML.Models;

/// <summary>
/// Represents an expense or income category.
/// </summary>
[Table("Categories")]
public class Category : IEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

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
