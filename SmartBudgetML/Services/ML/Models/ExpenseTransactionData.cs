using Microsoft.ML.Data;

namespace SmartBudgetML.Services.ML.Models;

/// <summary>
/// ML.NET input schema for NLP transaction categorization.
/// </summary>
public class ExpenseTransactionData
{
    [LoadColumn(0)]
    public string Description { get; set; } = string.Empty;

    [LoadColumn(1)]
    public string CategoryName { get; set; } = string.Empty;
}
