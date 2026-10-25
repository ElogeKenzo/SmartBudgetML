using SQLite;

namespace SmartBudgetML.Models;

/// <summary>
/// Persists user category corrections on-device so ML/hybrid categorization
/// retains user feedback across app restarts.
/// </summary>
[Table("CategoryFeedback")]
public class CategoryFeedback : IEntity
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed, MaxLength(250)]
    public string NormalizedDescription { get; set; } = string.Empty;

    [MaxLength(250)]
    public string RawDescription { get; set; } = string.Empty;

    [MaxLength(100)]
    public string CategoryName { get; set; } = string.Empty;

    public int Count { get; set; } = 1;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
