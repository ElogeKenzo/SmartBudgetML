namespace SmartBudgetML.Models;

/// <summary>
/// Common entity interface for SQLite database records.
/// Enables Generic Repository pattern implementations.
/// </summary>
public interface IEntity
{
    int Id { get; set; }
}
