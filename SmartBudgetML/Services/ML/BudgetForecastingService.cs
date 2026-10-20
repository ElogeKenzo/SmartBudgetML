using System.Globalization;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Formatting;
using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Predicts end-of-month cumulative expenses using Linear Regression (Ordinary Least Squares).
/// Evaluates daily spending velocity and forecasts month-end budget health.
/// 100% on-device, local execution.
/// </summary>
public class BudgetForecastingService : IBudgetForecastingService
{
    private readonly ICurrencyFormatter _currencyFormatter;

    public BudgetForecastingService()
        : this(new CurrencyFormatter())
    {
    }

    public BudgetForecastingService(ICurrencyFormatter currencyFormatter)
    {
        _currencyFormatter = currencyFormatter ?? new CurrencyFormatter();
    }

    public BudgetForecastResult ForecastMonthEnd(
        IEnumerable<Expense> monthExpenses,
        decimal monthlyBudget,
        DateTime? asOfDate = null,
        string currencyCode = "XAF")
    {
        var targetDate = asOfDate ?? DateTime.Today;
        var year = targetDate.Year;
        var month = targetDate.Month;
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var currentDay = Math.Clamp(targetDate.Day, 1, daysInMonth);

        var validExpenses = (monthExpenses ?? Enumerable.Empty<Expense>())
            .Where(e => !e.IsIncome && e.Date.Year == year && e.Date.Month == month && e.Date.Day <= currentDay)
            .ToList();

        var currentTotalSpend = validExpenses.Sum(e => e.Amount);

        if (validExpenses.Count == 0 || currentTotalSpend <= 0m)
        {
            return new BudgetForecastResult
            {
                CurrentTotalSpend = 0m,
                DaysElapsed = currentDay,
                DaysInMonth = daysInMonth,
                AverageDailySpend = 0m,
                ProjectedEndMonthSpend = 0m,
                MonthlyBudget = monthlyBudget,
                Status = BudgetHealthStatus.OnTrack,
                StatusMessage = "No expenses recorded this month yet. Budget is intact.",
                DailyBurnRateSlope = 0m
            };
        }

        var dailyExpenses = new decimal[currentDay + 1];
        foreach (var exp in validExpenses)
        {
            var day = Math.Clamp(exp.Date.Day, 1, currentDay);
            dailyExpenses[day] += exp.Amount;
        }

        var cumulative = new double[currentDay];
        var days = new double[currentDay];
        double runningTotal = 0;

        for (int i = 0; i < currentDay; i++)
        {
            runningTotal += (double)dailyExpenses[i + 1];
            cumulative[i] = runningTotal;
            days[i] = i + 1;
        }

        var averageDailySpend = currentDay > 0 ? currentTotalSpend / currentDay : 0m;

        double slope = 0;
        double intercept = 0;
        decimal projectedSpend;

        if (currentDay == 1)
        {
            projectedSpend = (decimal)cumulative[0] * daysInMonth;
            slope = cumulative[0];
        }
        else
        {
            double meanX = days.Average();
            double meanY = cumulative.Average();

            double numerator = 0;
            double denominator = 0;

            for (int i = 0; i < currentDay; i++)
            {
                var diffX = days[i] - meanX;
                numerator += diffX * (cumulative[i] - meanY);
                denominator += diffX * diffX;
            }

            if (Math.Abs(denominator) > 1e-9)
            {
                slope = numerator / denominator;
                intercept = meanY - (slope * meanX);
            }
            else
            {
                slope = (double)averageDailySpend;
                intercept = 0;
            }

            if (slope <= 0)
            {
                slope = (double)averageDailySpend;
                intercept = 0;
            }

            var rawRegressionProjection = (decimal)((slope * daysInMonth) + intercept);
            var simpleRunRateProjection = averageDailySpend * daysInMonth;

            var regressionWeight = Math.Clamp((currentDay - 1.0) / 10.0, 0.2, 0.9);
            var blendedProjection = (decimal)(
                (regressionWeight * (double)rawRegressionProjection) +
                ((1.0 - regressionWeight) * (double)simpleRunRateProjection)
            );

            projectedSpend = Math.Max(currentTotalSpend, Math.Round(blendedProjection, 2));
        }

        BudgetHealthStatus status;
        string statusMessage;

        if (monthlyBudget <= 0m)
        {
            status = BudgetHealthStatus.Warning;
            statusMessage = "Monthly budget not configured yet.";
        }
        else if (projectedSpend > monthlyBudget)
        {
            status = BudgetHealthStatus.Exceeded;
            var overage = projectedSpend - monthlyBudget;
            var formattedOverage = _currencyFormatter.Format(overage, currencyCode);
            statusMessage = $"Alert: Projected to exceed budget by {formattedOverage} at month-end!";
        }
        else if (projectedSpend >= monthlyBudget * 0.90m)
        {
            status = BudgetHealthStatus.Warning;
            var cushion = monthlyBudget - projectedSpend;
            var formattedCushion = _currencyFormatter.Format(cushion, currencyCode);
            var ratioPct = ((double)(projectedSpend / monthlyBudget) * 100.0).ToString("0", CultureInfo.InvariantCulture);
            statusMessage = $"Caution: Projected to reach {ratioPct}% of your budget (only {formattedCushion} buffer remaining).";
        }
        else
        {
            status = BudgetHealthStatus.OnTrack;
            var savings = monthlyBudget - projectedSpend;
            var formattedSavings = _currencyFormatter.Format(savings, currencyCode);
            statusMessage = $"Great job! Projected to finish under budget with {formattedSavings} in savings.";
        }

        return new BudgetForecastResult
        {
            CurrentTotalSpend = Math.Round(currentTotalSpend, 2),
            DaysElapsed = currentDay,
            DaysInMonth = daysInMonth,
            AverageDailySpend = Math.Round(averageDailySpend, 2),
            ProjectedEndMonthSpend = Math.Round(projectedSpend, 2),
            MonthlyBudget = monthlyBudget,
            Status = status,
            StatusMessage = statusMessage,
            DailyBurnRateSlope = Math.Round((decimal)slope, 2)
        };
    }
}
