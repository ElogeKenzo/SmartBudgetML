using SmartBudgetML.Models;
using SmartBudgetML.Services.ML;
using SmartBudgetML.Services.ML.Models;
using Xunit;

namespace SmartBudgetML.Tests.ML;

public class BudgetForecastingServiceTests
{
    private readonly BudgetForecastingService _forecaster;

    public BudgetForecastingServiceTests()
    {
        _forecaster = new BudgetForecastingService();
    }

    [Fact]
    public void ForecastMonthEnd_ShouldReturnOnTrack_WhenNoExpensesExist()
    {
        // Arrange
        var expenses = new List<Expense>();
        var budget = 2500m;
        var date = new DateTime(2026, 10, 15);

        // Act
        var result = _forecaster.ForecastMonthEnd(expenses, budget, date);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0m, result.CurrentTotalSpend);
        Assert.Equal(0m, result.ProjectedEndMonthSpend);
        Assert.Equal(BudgetHealthStatus.OnTrack, result.Status);
        Assert.Equal(15, result.DaysElapsed);
        Assert.Equal(31, result.DaysInMonth);
        Assert.Equal(16, result.DaysRemaining);
    }

    [Fact]
    public void ForecastMonthEnd_ShouldPredictUnderBudget_WhenSpendingIsPacedWell()
    {
        // Arrange: 10 days of $50/day on a $3,000 budget (31 days)
        // Current: $500, Expected projection: ~ $1,550 -> OnTrack
        var date = new DateTime(2026, 10, 10);
        var expenses = new List<Expense>();
        for (int day = 1; day <= 10; day++)
        {
            expenses.Add(new Expense
            {
                Amount = 50m,
                Date = new DateTime(2026, 10, day),
                Description = $"Daily Coffee Day {day}"
            });
        }

        // Act
        var result = _forecaster.ForecastMonthEnd(expenses, 3000m, date);

        // Assert
        Assert.Equal(500m, result.CurrentTotalSpend);
        Assert.Equal(50m, result.AverageDailySpend);
        Assert.InRange(result.ProjectedEndMonthSpend, 1400m, 1700m);
        Assert.Equal(BudgetHealthStatus.OnTrack, result.Status);
        Assert.True(result.ProjectedSurplusOrDeficit > 0m, "Should have projected surplus.");
        Assert.True(result.RecommendedDailyLimit > 50m, "Allowance should be generous since under budget.");
    }

    [Fact]
    public void ForecastMonthEnd_ShouldPredictExceeded_WhenSpendingVelocityIsExcessive()
    {
        // Arrange: 15 days of $200/day on a $3,000 budget (31 days in October)
        // Current: $3,000 at day 15! Projected: ~$6,000 -> Exceeded
        var date = new DateTime(2026, 10, 15);
        var expenses = new List<Expense>();
        for (int day = 1; day <= 15; day++)
        {
            expenses.Add(new Expense
            {
                Amount = 200m,
                Date = new DateTime(2026, 10, day),
                Description = $"High expense day {day}"
            });
        }

        // Act
        var result = _forecaster.ForecastMonthEnd(expenses, 3000m, date);

        // Assert
        Assert.Equal(3000m, result.CurrentTotalSpend);
        Assert.Equal(BudgetHealthStatus.Exceeded, result.Status);
        Assert.True(result.ProjectedEndMonthSpend > 3000m);
        Assert.True(result.ProjectedSurplusOrDeficit < 0m, "Should be in deficit.");
        Assert.Equal(0m, result.RecommendedDailyLimit); // No budget left
    }

    [Fact]
    public void ForecastMonthEnd_ShouldHandleSingleDay_Gracefully()
    {
        // Arrange: Only Day 1 with $100 spent on a $3,000 budget (30-day month)
        var date = new DateTime(2026, 9, 1);
        var expenses = new List<Expense>
        {
            new() { Amount = 100m, Date = new DateTime(2026, 9, 1), Description = "Day 1 Dinner" }
        };

        // Act
        var result = _forecaster.ForecastMonthEnd(expenses, 3000m, date);

        // Assert
        Assert.Equal(100m, result.CurrentTotalSpend);
        Assert.Equal(3000m, result.ProjectedEndMonthSpend); // 100 * 30 days
        Assert.Equal(30, result.DaysInMonth);
        Assert.Equal(1, result.DaysElapsed);
        Assert.Equal(29, result.DaysRemaining);
    }

    [Fact]
    public void ForecastMonthEnd_ShouldIgnoreIncomeEntries()
    {
        // Arrange: Expense of $100 and Salary Income of $5000
        var date = new DateTime(2026, 10, 5);
        var expenses = new List<Expense>
        {
            new() { Amount = 100m, Date = new DateTime(2026, 10, 2), Description = "Groceries", IsIncome = false },
            new() { Amount = 5000m, Date = new DateTime(2026, 10, 1), Description = "Salary", IsIncome = true }
        };

        // Act
        var result = _forecaster.ForecastMonthEnd(expenses, 2000m, date);

        // Assert: Only $100 expense is counted, income is excluded from expenditure total
        Assert.Equal(100m, result.CurrentTotalSpend);
    }
}
