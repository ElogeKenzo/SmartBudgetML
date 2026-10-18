using SmartBudgetML.Models;
using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Service contract for predicting end-of-month financial spending using
/// Linear Regression on daily cumulative expenditure trends.
/// </summary>
public interface IBudgetForecastingService
{
    /// <summary>
    /// Forecasts month-end total spending based on daily cumulative expenses.
    /// </summary>
    /// <param name="monthExpenses">All expenses recorded in the target month.</param>
    /// <param name="monthlyBudget">The user's monthly budget target.</param>
    /// <param name="asOfDate">Optional evaluation date (defaults to DateTime.Today).</param>
    /// <returns>Comprehensive forecast results including surplus/deficit and alert status.</returns>
    BudgetForecastResult ForecastMonthEnd(
        IEnumerable<Expense> monthExpenses,
        decimal monthlyBudget,
        DateTime? asOfDate = null);
}
