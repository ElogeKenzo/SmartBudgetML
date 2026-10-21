namespace SmartBudgetML.Services.ML.Models;

/// <summary>
/// Budget health status classification based on forecasted end-of-month spend.
/// </summary>
public enum BudgetHealthStatus
{
    /// <summary>
    /// Projected spending is comfortably within budget (&lt; 90%).
    /// </summary>
    OnTrack,

    /// <summary>
    /// Projected spending is approaching the limit (90% - 100%).
    /// </summary>
    Warning,

    /// <summary>
    /// Projected spending will exceed the monthly budget (&gt; 100%).
    /// </summary>
    Exceeded
}

/// <summary>
/// Supported budgeting period modes for regular and irregular incomes.
/// </summary>
public enum BudgetPeriodMode
{
    Monthly = 0,
    Weekly = 1,
    Envelope = 2
}

/// <summary>
/// Reliability level of the statistical forecast based on elapsed days and data density.
/// </summary>
public enum ForecastReliability
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>
/// Encapsulates the results of on-device linear regression budget forecasting.
/// </summary>
public class BudgetForecastResult
{
    public decimal CurrentTotalSpend { get; set; }
    public decimal FixedCommittedSpend { get; set; }
    public decimal VariableSpend { get; set; }
    public int DaysElapsed { get; set; }
    public int DaysInMonth { get; set; }
    public int DaysRemaining => Math.Max(0, DaysInMonth - DaysElapsed);

    public decimal AverageDailySpend { get; set; }
    public decimal ProjectedEndMonthSpend { get; set; }
    public decimal MonthlyBudget { get; set; }

    public BudgetPeriodMode PeriodMode { get; set; } = BudgetPeriodMode.Monthly;
    public ForecastReliability Reliability { get; set; } = ForecastReliability.High;
    public string ReliabilityMessage { get; set; } = string.Empty;

    /// <summary>
    /// Projected surplus (positive) or deficit (negative) at period-end.
    /// </summary>
    public decimal ProjectedSurplusOrDeficit => MonthlyBudget - ProjectedEndMonthSpend;

    /// <summary>
    /// Safe daily spending allowance for the remaining days of the period to stay within budget.
    /// </summary>
    public decimal RecommendedDailyLimit
    {
        get
        {
            if (DaysRemaining <= 0) return 0m;
            var remainingBudget = MonthlyBudget - CurrentTotalSpend;
            return remainingBudget > 0 ? Math.Round(remainingBudget / DaysRemaining, 2) : 0m;
        }
    }

    /// <summary>
    /// Percentage of budget projected to be consumed by period-end (e.g. 85.5%).
    /// </summary>
    public double ProjectedBudgetUtilizationPercentage
    {
        get
        {
            if (MonthlyBudget <= 0) return 0.0;
            return Math.Round((double)(ProjectedEndMonthSpend / MonthlyBudget) * 100.0, 1);
        }
    }

    public BudgetHealthStatus Status { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public decimal DailyBurnRateSlope { get; set; }
}
