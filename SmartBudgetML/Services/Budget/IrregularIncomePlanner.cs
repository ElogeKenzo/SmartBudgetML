namespace SmartBudgetML.Services.Budget;

public sealed record WeeklyEnvelopeAllocation(
    int WeekNumber,
    decimal AllocatedBudget,
    decimal SpentSoFar,
    decimal RemainingBudget);

public sealed record IrregularBudgetPlan(
    decimal RollingMonthlyIncome,
    decimal VitalMinimumSpend,
    decimal DiscretionaryPool,
    IReadOnlyList<WeeklyEnvelopeAllocation> WeeklyEnvelopes);

public sealed record FamilySolidarityReport(
    decimal TotalSolidaritySpend,
    decimal MonthlySolidarityTarget,
    double PercentageOfMonthlyIncome,
    bool IsOverTarget);

/// <summary>
/// Provides rolling income smoothing (3-to-6 month window), zero-income reserve amortization,
/// weekly discretionary envelope splitting, and Family & Solidarity (`family_solidarity`) tracking.
/// </summary>
public static class IrregularIncomePlanner
{
    public static decimal ComputeRollingAverageIncome(IReadOnlyList<decimal> monthlyIncomes, int windowMonths = 3)
    {
        if (monthlyIncomes == null || monthlyIncomes.Count == 0 || windowMonths <= 0)
        {
            return 0m;
        }

        var slice = monthlyIncomes
            .Skip(Math.Max(0, monthlyIncomes.Count - windowMonths))
            .Select(x => Math.Max(0m, x))
            .ToList();

        return slice.Count == 0 ? 0m : slice.Average();
    }

    public static IrregularBudgetPlan BuildWeeklyEnvelopePlan(
        IReadOnlyList<decimal> recentMonthlyIncomes,
        decimal vitalMinimumSpend,
        decimal reserveCushion,
        int weeksInMonth = 4,
        IReadOnlyList<decimal>? spentPerWeek = null)
    {
        int weeks = Math.Clamp(weeksInMonth, 1, 5);
        decimal rollingIncome = ComputeRollingAverageIncome(recentMonthlyIncomes, windowMonths: 3);

        // If rolling income is 0 (or below vital minimum), draw from reserveCushion safely without division by zero
        decimal effectiveFunds = rollingIncome > 0m ? rollingIncome : Math.Max(0m, reserveCushion);
        decimal vital = Math.Max(0m, vitalMinimumSpend);
        decimal discretionaryPool = Math.Max(0m, effectiveFunds - vital);
        decimal weeklyAllocation = Math.Round(discretionaryPool / weeks, 0);

        var envelopes = new List<WeeklyEnvelopeAllocation>(weeks);
        for (int i = 0; i < weeks; i++)
        {
            decimal spent = spentPerWeek != null && i < spentPerWeek.Count
                ? Math.Max(0m, spentPerWeek[i])
                : 0m;

            envelopes.Add(new WeeklyEnvelopeAllocation(
                WeekNumber: i + 1,
                AllocatedBudget: weeklyAllocation,
                SpentSoFar: spent,
                RemainingBudget: weeklyAllocation - spent));
        }

        return new IrregularBudgetPlan(
            RollingMonthlyIncome: rollingIncome,
            VitalMinimumSpend: vital,
            DiscretionaryPool: discretionaryPool,
            WeeklyEnvelopes: envelopes);
    }

    public static FamilySolidarityReport AnalyzeFamilySolidarity(
        decimal solidaritySpend,
        decimal monthlyIncome,
        decimal monthlyTarget)
    {
        decimal safeSpend = Math.Max(0m, solidaritySpend);
        decimal safeTarget = Math.Max(0m, monthlyTarget);
        double pct = monthlyIncome > 0m
            ? Math.Round((double)(safeSpend / monthlyIncome) * 100.0, 1)
            : 0.0;

        bool isOver = safeTarget > 0m && safeSpend > safeTarget;
        return new FamilySolidarityReport(
            TotalSolidaritySpend: safeSpend,
            MonthlySolidarityTarget: safeTarget,
            PercentageOfMonthlyIncome: pct,
            IsOverTarget: isOver);
    }
}
