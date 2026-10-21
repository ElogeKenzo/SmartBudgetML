using System.Globalization;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Formatting;
using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.ML;

/// <summary>
/// Predicts end-of-period cumulative expenses using Linear Regression (Ordinary Least Squares)
/// on variable spending plus calendar-committed fixed expenses (rent, tuition, recurring bills).
/// Supports Monthly, Weekly, and Envelope modes for regular and irregular incomes.
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
        return ForecastPeriod(
            monthExpenses,
            monthlyBudget,
            BudgetPeriodMode.Monthly,
            asOfDate,
            currencyCode,
            upcomingFixedExpenses: null);
    }

    public BudgetForecastResult ForecastPeriod(
        IEnumerable<Expense> expenses,
        decimal periodBudget,
        BudgetPeriodMode periodMode = BudgetPeriodMode.Monthly,
        DateTime? asOfDate = null,
        string currencyCode = "XAF",
        IEnumerable<Expense>? upcomingFixedExpenses = null)
    {
        var targetDate = (asOfDate ?? DateTime.Today).Date;
        DateTime periodStart;
        DateTime periodEnd;
        int totalDaysInPeriod;
        int currentDayIndex;

        if (periodMode == BudgetPeriodMode.Weekly)
        {
            // ISO week Monday..Sunday
            int diffToMonday = ((int)targetDate.DayOfWeek + 6) % 7;
            periodStart = targetDate.AddDays(-diffToMonday);
            periodEnd = periodStart.AddDays(6);
            totalDaysInPeriod = 7;
            currentDayIndex = Math.Clamp(diffToMonday + 1, 1, 7);
        }
        else
        {
            // Monthly or Envelope (anchored to current calendar month)
            int year = targetDate.Year;
            int month = targetDate.Month;
            totalDaysInPeriod = DateTime.DaysInMonth(year, month);
            currentDayIndex = Math.Clamp(targetDate.Day, 1, totalDaysInPeriod);
            periodStart = new DateTime(year, month, 1);
            periodEnd = new DateTime(year, month, totalDaysInPeriod);
        }

        var (reliability, reliabilityMessage) = EvaluateReliability(currentDayIndex, periodMode);

        var validExpenses = (expenses ?? Enumerable.Empty<Expense>())
            .Where(e => !e.IsIncome && e.Date.Date >= periodStart && e.Date.Date <= targetDate)
            .ToList();

        var futureScheduledFixed = (upcomingFixedExpenses ?? Enumerable.Empty<Expense>())
            .Where(e => !e.IsIncome && e.Date.Date > targetDate && e.Date.Date <= periodEnd)
            .ToList();

        var paidFixedSpend = validExpenses.Where(e => e.IsFixedOrRecurring).Sum(e => e.Amount);
        var scheduledFixedSpend = futureScheduledFixed.Sum(e => e.Amount);
        var totalFixedCommitted = paidFixedSpend + scheduledFixedSpend;

        var variableExpenses = validExpenses.Where(e => !e.IsFixedOrRecurring).ToList();
        var variableSpend = variableExpenses.Sum(e => e.Amount);
        var currentTotalSpend = paidFixedSpend + variableSpend;

        if ((validExpenses.Count == 0 || currentTotalSpend <= 0m) && scheduledFixedSpend <= 0m)
        {
            var emptyStatus = periodBudget <= 0m ? BudgetHealthStatus.Warning : BudgetHealthStatus.OnTrack;
            var emptyMsg = periodBudget <= 0m
                ? "Monthly budget not configured yet."
                : "No expenses recorded this month yet. Budget is intact.";

            return new BudgetForecastResult
            {
                CurrentTotalSpend = 0m,
                FixedCommittedSpend = 0m,
                VariableSpend = 0m,
                DaysElapsed = currentDayIndex,
                DaysInMonth = totalDaysInPeriod,
                AverageDailySpend = 0m,
                ProjectedEndMonthSpend = 0m,
                MonthlyBudget = periodBudget,
                PeriodMode = periodMode,
                Reliability = reliability,
                ReliabilityMessage = reliabilityMessage,
                Status = emptyStatus,
                StatusMessage = emptyMsg,
                DailyBurnRateSlope = 0m
            };
        }

        var dailyVariable = new decimal[currentDayIndex + 1];
        foreach (var exp in variableExpenses)
        {
            int dayIndex = Math.Clamp((exp.Date.Date - periodStart).Days + 1, 1, currentDayIndex);
            dailyVariable[dayIndex] += exp.Amount;
        }

        var cumulative = new double[currentDayIndex];
        var days = new double[currentDayIndex];
        double runningTotal = 0;

        for (int i = 0; i < currentDayIndex; i++)
        {
            runningTotal += (double)dailyVariable[i + 1];
            cumulative[i] = runningTotal;
            days[i] = i + 1;
        }

        var averageDailyVariableSpend = currentDayIndex > 0 ? variableSpend / currentDayIndex : 0m;
        var averageDailyTotalSpend = currentDayIndex > 0 ? currentTotalSpend / currentDayIndex : 0m;

        double slope = 0;
        double intercept = 0;
        decimal projectedVariableSpend;

        if (variableSpend <= 0m)
        {
            projectedVariableSpend = 0m;
            slope = 0;
        }
        else if (currentDayIndex == 1)
        {
            projectedVariableSpend = (decimal)cumulative[0] * totalDaysInPeriod;
            slope = cumulative[0];
        }
        else
        {
            double meanX = days.Average();
            double meanY = cumulative.Average();

            double numerator = 0;
            double denominator = 0;

            for (int i = 0; i < currentDayIndex; i++)
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
                slope = (double)averageDailyVariableSpend;
                intercept = 0;
            }

            if (slope <= 0)
            {
                slope = (double)averageDailyVariableSpend;
                intercept = 0;
            }

            var rawRegressionProjection = (decimal)((slope * totalDaysInPeriod) + intercept);
            var simpleRunRateProjection = averageDailyVariableSpend * totalDaysInPeriod;

            var regressionWeight = Math.Clamp((currentDayIndex - 1.0) / 10.0, 0.2, 0.9);
            var blendedVariableProjection = (decimal)(
                (regressionWeight * (double)rawRegressionProjection) +
                ((1.0 - regressionWeight) * (double)simpleRunRateProjection)
            );

            projectedVariableSpend = Math.Max(variableSpend, Math.Round(blendedVariableProjection, 2));
        }

        // Fixed expenses are added at their exact calendar amount rather than linearly extrapolated
        var projectedSpend = Math.Max(currentTotalSpend, Math.Round(totalFixedCommitted + projectedVariableSpend, 2));

        BudgetHealthStatus status;
        string statusMessage;

        if (periodBudget <= 0m)
        {
            status = BudgetHealthStatus.Warning;
            statusMessage = "Monthly budget not configured yet.";
        }
        else if (projectedSpend > periodBudget)
        {
            status = BudgetHealthStatus.Exceeded;
            var overage = projectedSpend - periodBudget;
            var formattedOverage = _currencyFormatter.Format(overage, currencyCode);
            statusMessage = $"Alert: Projected to exceed budget by {formattedOverage} at month-end!";
        }
        else if (projectedSpend >= periodBudget * 0.90m)
        {
            status = BudgetHealthStatus.Warning;
            var cushion = periodBudget - projectedSpend;
            var formattedCushion = _currencyFormatter.Format(cushion, currencyCode);
            var ratioPct = ((double)(projectedSpend / periodBudget) * 100.0).ToString("0", CultureInfo.InvariantCulture);
            statusMessage = $"Caution: Projected to reach {ratioPct}% of your budget (only {formattedCushion} buffer remaining).";
        }
        else
        {
            status = BudgetHealthStatus.OnTrack;
            var savings = periodBudget - projectedSpend;
            var formattedSavings = _currencyFormatter.Format(savings, currencyCode);
            statusMessage = $"Great job! Projected to finish under budget with {formattedSavings} in savings.";
        }

        return new BudgetForecastResult
        {
            CurrentTotalSpend = Math.Round(currentTotalSpend, 2),
            FixedCommittedSpend = Math.Round(totalFixedCommitted, 2),
            VariableSpend = Math.Round(variableSpend, 2),
            DaysElapsed = currentDayIndex,
            DaysInMonth = totalDaysInPeriod,
            AverageDailySpend = Math.Round(averageDailyTotalSpend, 2),
            ProjectedEndMonthSpend = Math.Round(projectedSpend, 2),
            MonthlyBudget = periodBudget,
            PeriodMode = periodMode,
            Reliability = reliability,
            ReliabilityMessage = reliabilityMessage,
            Status = status,
            StatusMessage = statusMessage,
            DailyBurnRateSlope = Math.Round((decimal)slope, 2)
        };
    }

    private static (ForecastReliability Reliability, string Message) EvaluateReliability(
        int daysElapsed,
        BudgetPeriodMode periodMode)
    {
        if (periodMode == BudgetPeriodMode.Weekly)
        {
            return daysElapsed < 3
                ? (ForecastReliability.Low, "Peu de données (< 3 jours sur la semaine) : projection indicative.")
                : (ForecastReliability.High, "Fiabilité élevée sur la période hebdomadaire.");
        }

        if (daysElapsed < 7)
        {
            return (ForecastReliability.Low, "Peu de données (< 7 jours) : projection linéaire peu fiable en début de période.");
        }

        if (daysElapsed < 14)
        {
            return (ForecastReliability.Medium, "Fiabilité modérée (1 à 2 semaines d'historique).");
        }

        return (ForecastReliability.High, "Fiabilité élevée (tendance consolidée sur plus de 14 jours).");
    }
}
