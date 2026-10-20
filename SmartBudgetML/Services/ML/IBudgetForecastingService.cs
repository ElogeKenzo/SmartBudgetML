using SmartBudgetML.Models;
using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Service contract for predicting end-of-month financial spending using
/// Linear Regression on daily cumulative expenditure trends.
/// </summary>
public interface IBudgetForecastingService
{
    BudgetForecastResult ForecastMonthEnd(
        IEnumerable<Expense> monthExpenses,
        decimal monthlyBudget,
        DateTime? asOfDate = null,
        string currencyCode = "XAF");
}
