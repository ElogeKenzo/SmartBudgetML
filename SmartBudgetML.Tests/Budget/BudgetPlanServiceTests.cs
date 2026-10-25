using SmartBudgetML.Models;
using SmartBudgetML.Services.Budget;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.ML;
using SmartBudgetML.Services.ML.Models;
using SmartBudgetML.Services.Security;
using Xunit;

namespace SmartBudgetML.Tests.Budget;

/// <summary>
/// Lot 8E (N14): the income type in the profile drives the budget period, on a real temporary database.
/// </summary>
public class BudgetPlanServiceTests : IDisposable
{
    // Wednesday: the ISO week runs from Monday 2 to Sunday 8 November 2026.
    private static readonly DateTime Today = new(2026, 11, 4);

    private readonly string _tempDir;

    public BudgetPlanServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SmartBudgetML_Budget_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch (IOException)
        {
            // A pooled SQLite handle can keep a file open a little longer on Windows.
        }
    }

    private async Task<(DatabaseService Db, BudgetPlanService Service)> OpenAsync(UserProfile profile)
    {
        var db = new DatabaseService(Path.Combine(_tempDir, "budget.db3"), new InMemorySecureKeyStore());
        await db.InitializeAsync();
        await db.UserProfiles.InsertAsync(profile);
        return (db, new BudgetPlanService(db, new BudgetForecastingService()));
    }

    private static async Task<int> CategoryIdAsync(DatabaseService db, string key) =>
        (await db.Categories.FirstOrDefaultAsync(c => c.Key == key))!.Id;

    private static Expense Income(decimal amount, DateTime date, int categoryId, bool exceptional = false) =>
        new() { Description = "Revenu", Amount = amount, Date = date, CategoryId = categoryId, IsIncome = true, IsExceptional = exceptional };

    [Fact]
    public async Task FixedProfile_UsesMonthlyBudget_IncomeMinusSavings()
    {
        var (db, service) = await OpenAsync(new UserProfile { FullName = "Test", MonthlyIncome = 200_000m, MonthlySavingsGoal = 50_000m });
        await db.Expenses.InsertAsync(new Expense { Description = "Marché", Amount = 10_000m, Date = Today, CategoryId = await CategoryIdAsync(db, "food_market") });

        var overview = await service.GetOverviewAsync(Today, "XAF");

        Assert.Equal(BudgetPeriodMode.Monthly, overview.PeriodMode);
        Assert.Equal(150_000m, overview.Forecast.MonthlyBudget);
        Assert.Equal(10_000m, overview.Forecast.CurrentTotalSpend);
        Assert.Null(overview.Plan);
        Assert.Null(overview.CurrentWeek);
    }

    [Fact]
    public async Task FixedProfile_WithoutIncome_HasNoInventedBudget()
    {
        var (_, service) = await OpenAsync(new UserProfile { FullName = "Test" });

        var overview = await service.GetOverviewAsync(Today, "XAF");

        Assert.Equal(0m, overview.Forecast.MonthlyBudget);
        Assert.False(overview.HasIncomeHistory);
    }

    [Fact]
    public async Task IrregularProfile_BuildsWeeklyEnvelopeFromRecordedIncomeMinusVitalMinimum()
    {
        var (db, service) = await OpenAsync(new UserProfile
        {
            FullName = "Test",
            IncomeProfile = IncomeProfileType.Irregular,
            VitalMinimumSpend = 60_000m
        });
        var business = await CategoryIdAsync(db, "business_hustle");
        var transfers = await CategoryIdAsync(db, "transfers_received");
        var food = await CategoryIdAsync(db, "food_market");
        await db.Expenses.InsertAsync(Income(100_000m, new DateTime(2026, 8, 10), business));
        await db.Expenses.InsertAsync(Income(160_000m, new DateTime(2026, 9, 12), business));
        await db.Expenses.InsertAsync(Income(40_000m, new DateTime(2026, 10, 3), business));
        // A tontine pot must not inflate the average.
        await db.Expenses.InsertAsync(Income(500_000m, new DateTime(2026, 10, 20), transfers, exceptional: true));
        await db.Expenses.InsertAsync(new Expense { Description = "Taxi", Amount = 3_000m, Date = new DateTime(2026, 11, 2), CategoryId = food });
        await db.Expenses.InsertAsync(new Expense { Description = "Riz", Amount = 7_000m, Date = Today, CategoryId = food });
        // Last week: outside the current envelope.
        await db.Expenses.InsertAsync(new Expense { Description = "Pain", Amount = 9_999m, Date = new DateTime(2026, 11, 1), CategoryId = food });

        var overview = await service.GetOverviewAsync(Today, "XAF");

        Assert.Equal(BudgetPeriodMode.Weekly, overview.PeriodMode);
        Assert.True(overview.HasIncomeHistory);
        Assert.Equal(100_000m, overview.Plan!.RollingMonthlyIncome);
        Assert.Equal(40_000m, overview.Plan.DiscretionaryPool);
        Assert.Equal(10_000m, overview.CurrentWeek!.Allocated);
        Assert.Equal(10_000m, overview.CurrentWeek.Spent);
        Assert.Equal(0m, overview.CurrentWeek.Remaining);
        Assert.Equal(new DateTime(2026, 11, 2), overview.CurrentWeek.WeekStart);
        Assert.Equal(10_000m, overview.Forecast.MonthlyBudget);
        Assert.Equal(7, overview.Forecast.DaysInMonth);
        Assert.Equal(ForecastReliability.High, overview.Forecast.Reliability);
    }

    [Fact]
    public async Task IrregularProfile_IgnoresMonthsBeforeFirstRecord()
    {
        var (db, service) = await OpenAsync(new UserProfile { FullName = "Test", IncomeProfile = IncomeProfileType.Irregular });
        await db.Expenses.InsertAsync(Income(80_000m, new DateTime(2026, 10, 5), await CategoryIdAsync(db, "salary")));

        var overview = await service.GetOverviewAsync(Today, "XAF");

        Assert.Equal(80_000m, overview.Plan!.RollingMonthlyIncome);
        Assert.Equal(20_000m, overview.CurrentWeek!.Allocated);
    }

    [Fact]
    public async Task IrregularProfile_WithoutRecordedIncome_FallsBackOnProfileIncome()
    {
        var (_, service) = await OpenAsync(new UserProfile
        {
            FullName = "Test",
            IncomeProfile = IncomeProfileType.Irregular,
            MonthlyIncome = 120_000m,
            VitalMinimumSpend = 40_000m
        });

        var overview = await service.GetOverviewAsync(Today, "XAF");

        Assert.False(overview.HasIncomeHistory);
        Assert.Equal(80_000m, overview.Plan!.DiscretionaryPool);
        Assert.Equal(20_000m, overview.CurrentWeek!.Allocated);
    }

    [Fact]
    public async Task MixedProfile_UsesMonthlyEnvelopeAndReportsSolidarity()
    {
        var (db, service) = await OpenAsync(new UserProfile
        {
            FullName = "Test",
            IncomeProfile = IncomeProfileType.Mixed,
            MonthlyIncome = 90_000m,
            MonthlySavingsGoal = 10_000m,
            MonthlySolidarityTarget = 5_000m
        });
        await db.Expenses.InsertAsync(new Expense
        {
            Description = "Aide maman",
            Amount = 8_000m,
            Date = Today,
            CategoryId = await CategoryIdAsync(db, "family_solidarity")
        });

        var overview = await service.GetOverviewAsync(Today, "XAF");

        Assert.Equal(BudgetPeriodMode.Envelope, overview.PeriodMode);
        Assert.Equal(80_000m, overview.Forecast.MonthlyBudget);
        Assert.Null(overview.CurrentWeek);
        Assert.NotNull(overview.Solidarity);
        Assert.Equal(8_000m, overview.Solidarity!.TotalSolidaritySpend);
        Assert.True(overview.Solidarity.IsOverTarget);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(45000, true)]
    [InlineData(-1, false)]
    [InlineData(1500.5, false)]
    public void IncomeProfileInput_AcceptsOnlyWholeAmountsFromZero(double amount, bool expected)
    {
        Assert.Equal(expected, IncomeProfileInput.IsValidAmount((decimal)amount));
    }

    [Fact]
    public async Task ProfileSwitchedToIrregular_IsReadBackFromDatabase()
    {
        var (db, service) = await OpenAsync(new UserProfile { FullName = "Test", MonthlyIncome = 100_000m });
        var profile = (await db.UserProfiles.GetAllAsync()).Single();
        profile.IncomeProfile = IncomeProfileType.Irregular;
        profile.VitalMinimumSpend = 20_000m;
        await db.UserProfiles.UpdateAsync(profile);

        var overview = await service.GetOverviewAsync(Today, "XAF");

        Assert.Equal(IncomeProfileType.Irregular, overview.IncomeProfile);
        Assert.Equal(20_000m, overview.CurrentWeek!.Allocated);
    }
}
