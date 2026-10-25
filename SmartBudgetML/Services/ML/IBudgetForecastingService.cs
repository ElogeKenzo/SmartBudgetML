using SmartBudgetML.Models;
using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Service contract for predicting end-of-period financial spending using
/// Linear Regression on daily variable expenditure trends plus known fixed/calendar expenses.
/// </summary>
public interface IBudgetForecastingService
{
    BudgetForecastResult ForecastMonthEnd(
        IEnumerable<Expense> monthExpenses,
        decimal monthlyBudget,
        DateTime? asOfDate = null,
        string currencyCode = "XAF");

    BudgetForecastResult ForecastPeriod(
        IEnumerable<Expense> expenses,
        decimal periodBudget,
        BudgetPeriodMode periodMode = BudgetPeriodMode.Monthly,
        DateTime? asOfDate = null,
        string currencyCode = "XAF",
        IEnumerable<Expense>? upcomingFixedExpenses = null);
}
