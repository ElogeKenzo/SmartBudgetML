using SmartBudgetML.Models;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.ML;
using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Services.Budget;

public sealed record WeeklyEnvelopeStatus(
    DateTime WeekStart,
    DateTime WeekEnd,
    decimal Allocated,
    decimal Spent)
{
    public decimal Remaining => Allocated - Spent;
}

/// <summary>What the home and analytics screens show, for the income type chosen in the profile.</summary>
public sealed record BudgetOverview(
    IncomeProfileType IncomeProfile,
    BudgetPeriodMode PeriodMode,
    BudgetForecastResult Forecast,
    IrregularBudgetPlan? Plan,
    WeeklyEnvelopeStatus? CurrentWeek,
    bool HasIncomeHistory,
    FamilySolidarityReport? Solidarity);

/// <summary>Checks the amounts typed in the income profile: zero or more, whole XAF (rule 8).</summary>
public static class IncomeProfileInput
{
    public static bool IsValidAmount(decimal amount) => amount >= 0m && decimal.Truncate(amount) == amount;
}

public interface IBudgetPlanService
{
    Task<BudgetOverview> GetOverviewAsync(DateTime today, string currencyCode);
}

/// <summary>
/// Lot 8E (N14): turns the profile's income type into a budget period. Fixed income keeps the
/// monthly budget (income minus savings goal). Irregular income gets a weekly envelope from
/// <see cref="IrregularIncomePlanner"/>, built on the income recorded over the last complete months
/// minus the vital minimum. Mixed income uses the monthly envelope. One-off income (a tontine pot)
/// is left out of the average.
/// </summary>
public class BudgetPlanService : IBudgetPlanService
{
    public const int IncomeWindowMonths = 3;
    public const int WeeksPerMonth = 4;

    private readonly IDatabaseService _database;
    private readonly IBudgetForecastingService _forecaster;

    public BudgetPlanService(IDatabaseService database, IBudgetForecastingService forecaster)
    {
        _database = database;
        _forecaster = forecaster;
    }

    public async Task<BudgetOverview> GetOverviewAsync(DateTime today, string currencyCode)
    {
        await _database.InitializeAsync();
        today = today.Date;
        var profile = (await _database.UserProfiles.GetAllAsync()).FirstOrDefault() ?? new UserProfile();
        var expenses = await _database.Expenses.GetAllAsync();

        if (profile.IncomeProfile == IncomeProfileType.Fixed)
        {
            var monthlyBudget = profile.MonthlyIncome > 0m
                ? Math.Max(0m, profile.MonthlyIncome - profile.MonthlySavingsGoal)
                : 0m;
            var monthly = _forecaster.ForecastPeriod(expenses, monthlyBudget, BudgetPeriodMode.Monthly, today, currencyCode);
            return new BudgetOverview(
                IncomeProfileType.Fixed,
                BudgetPeriodMode.Monthly,
                monthly,
                Plan: null,
                CurrentWeek: null,
                HasIncomeHistory: profile.MonthlyIncome > 0m,
                Solidarity: await BuildSolidarityAsync(profile, expenses, profile.MonthlyIncome, today));
        }

        var (incomes, hasHistory) = RecentMonthlyIncomes(expenses, today);
        if (!hasHistory && profile.MonthlyIncome > 0m)
        {
            // No income recorded yet: fall back on the amount the user typed in the profile.
            incomes = new List<decimal> { profile.MonthlyIncome };
        }

        var plan = IrregularIncomePlanner.BuildWeeklyEnvelopePlan(
            incomes,
            profile.VitalMinimumSpend,
            reserveCushion: 0m,
            weeksInMonth: WeeksPerMonth);

        BudgetForecastResult forecast;
        WeeklyEnvelopeStatus? week = null;
        BudgetPeriodMode mode;
        if (profile.IncomeProfile == IncomeProfileType.Irregular)
        {
            mode = BudgetPeriodMode.Weekly;
            var weekly = plan.WeeklyEnvelopes[0].AllocatedBudget;
            forecast = _forecaster.ForecastPeriod(expenses, weekly, mode, today, currencyCode);
            int diffToMonday = ((int)today.DayOfWeek + 6) % 7;
            var weekStart = today.AddDays(-diffToMonday);
            week = new WeeklyEnvelopeStatus(weekStart, weekStart.AddDays(6), weekly, forecast.CurrentTotalSpend);
        }
        else
        {
            mode = BudgetPeriodMode.Envelope;
            var budget = Math.Max(0m, plan.RollingMonthlyIncome - profile.MonthlySavingsGoal);
            forecast = _forecaster.ForecastPeriod(expenses, budget, mode, today, currencyCode);
        }

        return new BudgetOverview(
            profile.IncomeProfile,
            mode,
            forecast,
            plan,
            week,
            hasHistory,
            await BuildSolidarityAsync(profile, expenses, plan.RollingMonthlyIncome, today));
    }

    /// <summary>
    /// Income per complete month, oldest first, for at most <see cref="IncomeWindowMonths"/> months
    /// and never before the first month with a record (months before the app was used are not zero income).
    /// </summary>
    public static (List<decimal> Incomes, bool HasHistory) RecentMonthlyIncomes(IReadOnlyList<Expense> expenses, DateTime today)
    {
        var currentMonth = new DateTime(today.Year, today.Month, 1);
        if (expenses.Count == 0)
        {
            return (new List<decimal>(), false);
        }

        var firstRecord = expenses.Min(e => e.Date.Date);
        var firstMonth = new DateTime(firstRecord.Year, firstRecord.Month, 1);
        var incomes = new List<decimal>();
        for (int back = IncomeWindowMonths; back >= 1; back--)
        {
            var month = currentMonth.AddMonths(-back);
            if (month < firstMonth)
            {
                continue;
            }

            incomes.Add(expenses
                .Where(e => e.IsIncome && !e.IsExceptional && e.Date.Year == month.Year && e.Date.Month == month.Month)
                .Sum(e => e.Amount));
        }

        return (incomes, incomes.Any(i => i > 0m));
    }

    private async Task<FamilySolidarityReport?> BuildSolidarityAsync(
        UserProfile profile,
        IReadOnlyList<Expense> expenses,
        decimal monthlyIncome,
        DateTime today)
    {
        if (profile.MonthlySolidarityTarget <= 0m)
        {
            return null;
        }

        var category = await _database.Categories.FirstOrDefaultAsync(c => c.Key == "family_solidarity");
        var spend = category is null
            ? 0m
            : expenses
                .Where(e => !e.IsIncome && e.CategoryId == category.Id && e.Date.Year == today.Year && e.Date.Month == today.Month)
                .Sum(e => e.Amount);
        return IrregularIncomePlanner.AnalyzeFamilySolidarity(spend, monthlyIncome, profile.MonthlySolidarityTarget);
    }
}
