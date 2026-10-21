using SmartBudgetML.Models;
using SmartBudgetML.Services.Budget;
using SmartBudgetML.Services.Sms;
using SmartBudgetML.Services.Tontines;
using Xunit;

namespace SmartBudgetML.Tests.LocalValue;

public class Phase6LocalValueTests
{
    // =========================================================================
    // 6.1 Mobile Money SMS Parser Architecture & Deduplication Tests
    // =========================================================================

    [Theory]
    [InlineData("1.500", 1500)]
    [InlineData("1 500", 1500)]
    [InlineData("1,500", 1500)]
    [InlineData("1500 FCFA", 1500)]
    [InlineData("25 000 XAF", 25000)]
    [InlineData("150.000 FCFA", 150000)]
    public void MobileMoneyAmountParser_ShouldParseVariousXafSeparators(string rawAmount, decimal expected)
    {
        var ok = MobileMoneyAmountParser.TryParseAmountXaf(rawAmount, out var parsed);
        Assert.True(ok);
        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void SmsParserRegistry_ShouldExtractFees_DeduplicateByReferenceOrTimeWindow_AndHandleInvalidSmsGracefully()
    {
        var registry = new SmsParserRegistry();
        registry.RegisterParser(new SyntheticTestSmsParser());

        // 1. Invalid / unrecognized SMS -> returns null without throwing
        Assert.Null(registry.Parse(""));
        Assert.Null(registry.Parse("Bonjour maman, tu vas bien ?"));

        // 2. Valid SMS with fee and reference
        var sms1 = "TEST_MOMO|SENT|Amount:15.000 FCFA|Fee:150 FCFA|Ref:TX998877|To:Jean";
        var parsed1 = registry.Parse(sms1);
        Assert.NotNull(parsed1);
        Assert.Equal("TestMoMo", parsed1!.Operator);
        Assert.Equal(MobileMoneyTransactionType.TransferSent, parsed1.Type);
        Assert.Equal(15000m, parsed1.Amount);
        Assert.Equal(150m, parsed1.Fee);
        Assert.Equal("TX998877", parsed1.Reference);
        Assert.False(parsed1.IsIncome);

        // 3. Batch parsing with deduplication by Reference
        var batch = string.Join("\n\n", new[]
        {
            sms1,
            sms1, // duplicate reference TX998877
            "TEST_MOMO|RECV|Amount:25 000 FCFA|Fee:0 FCFA|Ref:TX112233|From:Paul"
        });

        var batchResult = registry.ParseBatch(batch);
        Assert.Equal(2, batchResult.Count);
        Assert.Contains(batchResult, t => t.Reference == "TX998877" && !t.IsIncome);
        Assert.Contains(batchResult, t => t.Reference == "TX112233" && t.IsIncome);

        // 4. Deduplication against existing transactions (by Reference or Date ±2min + Amount + Operator)
        var duplicateByRef = new ParsedMobileMoneyTransaction
        {
            Operator = "TestMoMo",
            Amount = 999m,
            Reference = "TX998877",
            Date = DateTime.UtcNow.AddHours(5)
        };
        Assert.True(registry.IsDuplicate(duplicateByRef, batchResult));

        var duplicateByWindow = new ParsedMobileMoneyTransaction
        {
            Operator = "TestMoMo",
            Amount = 15000m,
            Reference = null,
            Date = parsed1.Date.AddSeconds(45)
        };
        Assert.True(registry.IsDuplicate(duplicateByWindow, batchResult));
    }

    [Fact(Skip = "En attente d'échantillons réels")]
    public void OrangeMoneyCameroonParser_RealSmsSamples_PendingUserInput()
    {
        // Conformément à CLAUDE.md §6.1 : ne jamais inventer les formats SMS opérateurs.
    }

    [Fact(Skip = "En attente d'échantillons réels")]
    public void MtnMomoCameroonParser_RealSmsSamples_PendingUserInput()
    {
        // Conformément à CLAUDE.md §6.1 : ne jamais inventer les formats SMS opérateurs.
    }

    // =========================================================================
    // 6.2 Tontines / Njangi Pure Business Logic Tests
    // =========================================================================

    [Fact]
    public void TontineCalculator_5Members5Rounds_FullCycle_AndNetUserBalance()
    {
        var tontine = new Tontine
        {
            Id = 1,
            Name = "Tontine Quartier Bastos",
            ContributionAmount = 50_000m,
            Frequency = TontineFrequency.Monthly,
            TotalMembers = 5,
            MyTurnOrder = 2,
            StartDate = new DateTime(2026, 1, 10)
        };

        var members = new List<TontineMember>
        {
            new() { Id = 1, TontineId = 1, Name = "Paul", TurnOrder = 1, IsCurrentUser = false },
            new() { Id = 2, TontineId = 1, Name = "Moi", TurnOrder = 2, IsCurrentUser = true },
            new() { Id = 3, TontineId = 1, Name = "Chantal", TurnOrder = 3, IsCurrentUser = false },
            new() { Id = 4, TontineId = 1, Name = "Eric", TurnOrder = 4, IsCurrentUser = false },
            new() { Id = 5, TontineId = 1, Name = "Nadine", TurnOrder = 5, IsCurrentUser = false }
        };

        var pot = TontineCalculator.CalculatePot(tontine, members);
        Assert.Equal(250_000m, pot);

        var rounds = TontineCalculator.GenerateSchedule(tontine, members);
        Assert.Equal(5, rounds.Count);
        Assert.Equal(2, rounds[1].BeneficiaryMemberId); // Round 2 belongs to "Moi"

        // Simulate Rounds 1 and 2 paid out; User contributed to Rounds 1, 2, 3
        rounds[0].IsPaidOut = true;
        rounds[1].IsPaidOut = true;

        var nextBeneficiary = TontineCalculator.GetNextBeneficiary(rounds, members);
        Assert.NotNull(nextBeneficiary);
        Assert.Equal("Chantal", nextBeneficiary!.Name);

        var contributions = new List<TontineContribution>
        {
            new() { TontineId = 1, RoundNumber = 1, MemberId = 2, Amount = 50_000m, IsPaid = true, Date = new DateTime(2026, 1, 10) },
            new() { TontineId = 1, RoundNumber = 2, MemberId = 2, Amount = 50_000m, IsPaid = true, Date = new DateTime(2026, 2, 10) },
            new() { TontineId = 1, RoundNumber = 3, MemberId = 2, Amount = 50_000m, IsPaid = true, Date = new DateTime(2026, 3, 10) }
        };

        var summary = TontineCalculator.CalculateUserSummary(tontine, rounds, members, contributions);
        Assert.Equal(250_000m, summary.PotAmount);
        Assert.Equal(150_000m, summary.TotalContributedByUser);
        Assert.Equal(250_000m, summary.TotalReceivedByUser);
        // Net balance = 250,000 received - 150,000 contributed = +100,000 FCFA
        Assert.Equal(100_000m, summary.NetUserBalance);
        Assert.Equal(2, summary.CompletedRounds);
        Assert.Equal(3, summary.RemainingRounds);
    }

    [Fact]
    public void TontineCalculator_WithdrawnMember_LateContributions_WeeklyFrequency_AndYearEndTransition()
    {
        // Year-end transition: starts in November 2026, runs 5 monthly rounds -> ends March 2027
        var tontine = new Tontine
        {
            Id = 2,
            Name = "Njangi Fin d'Année",
            ContributionAmount = 20_000m,
            Frequency = TontineFrequency.Monthly,
            TotalMembers = 5,
            MyTurnOrder = 1,
            StartDate = new DateTime(2026, 11, 15)
        };

        var members = new List<TontineMember>
        {
            new() { Id = 10, TontineId = 2, Name = "Moi", TurnOrder = 1, IsCurrentUser = true },
            new() { Id = 11, TontineId = 2, Name = "Membre 2", TurnOrder = 2 },
            new() { Id = 12, TontineId = 2, Name = "Membre Retiré", TurnOrder = 3, IsWithdrawn = true },
            new() { Id = 13, TontineId = 2, Name = "Membre 4", TurnOrder = 4 },
            new() { Id = 14, TontineId = 2, Name = "Membre 5", TurnOrder = 5 }
        };

        // Active members = 4 (since Member 3 withdrew) -> Pot = 4 * 20,000 = 80,000
        var activePot = TontineCalculator.CalculatePot(tontine, members);
        Assert.Equal(80_000m, activePot);

        var monthlyRounds = TontineCalculator.GenerateSchedule(tontine, members);
        Assert.Equal(4, monthlyRounds.Count); // Only active members get a round
        Assert.Equal(new DateTime(2026, 11, 15), monthlyRounds[0].ExpectedDate);
        Assert.Equal(new DateTime(2026, 12, 15), monthlyRounds[1].ExpectedDate);
        Assert.Equal(new DateTime(2027, 1, 15), monthlyRounds[2].ExpectedDate);
        Assert.Equal(new DateTime(2027, 2, 15), monthlyRounds[3].ExpectedDate);

        // Detect late contributions for Round 1 when reference date is 2026-11-20
        var recorded = new List<TontineContribution>
        {
            new() { TontineId = 2, RoundNumber = 1, MemberId = 10, Amount = 20_000m, IsPaid = true, Date = new DateTime(2026, 11, 14) }
        };

        var lateList = TontineCalculator.DetectLateContributions(monthlyRounds, members, recorded, new DateTime(2026, 11, 20));
        // 3 active members (11, 13, 14) have not paid Round 1 which was due 2026-11-15
        Assert.Equal(3, lateList.Count);
        Assert.All(lateList, c => Assert.True(c.IsLate && !c.IsPaid));

        // Weekly frequency check
        tontine.Frequency = TontineFrequency.Weekly;
        var weeklyRounds = TontineCalculator.GenerateSchedule(tontine, members);
        Assert.Equal(new DateTime(2026, 11, 22), weeklyRounds[1].ExpectedDate);
    }

    // =========================================================================
    // 6.3 Irregular Income, Zero-Income Month & Weekly Envelopes Tests
    // =========================================================================

    [Fact]
    public void IrregularIncomePlanner_ShouldComputeRollingAverage_HandleZeroIncomeMonth_AndSplitWeeklyEnvelopes()
    {
        // Fluctuating incomes over 4 months including a 0 FCFA month
        var incomes = new decimal[] { 200_000m, 100_000m, 0m, 180_000m };
        var rolling3 = IrregularIncomePlanner.ComputeRollingAverageIncome(incomes, windowMonths: 3);
        // Last 3 months: (100,000 + 0 + 180,000) / 3 = 93,333.33 -> rounded
        Assert.Equal(93_333m, Math.Round(rolling3, 0));

        // Zero-income month with reserve cushion: must not divide by zero
        var zeroIncomes = new decimal[] { 0m, 0m, 0m };
        var plan = IrregularIncomePlanner.BuildWeeklyEnvelopePlan(
            recentMonthlyIncomes: zeroIncomes,
            vitalMinimumSpend: 60_000m,
            reserveCushion: 100_000m,
            weeksInMonth: 4,
            spentPerWeek: new decimal[] { 5_000m, 12_000m, 0m, 0m });

        Assert.Equal(0m, plan.RollingMonthlyIncome);
        // Available pool uses reserveCushion (100,000) - vitalMinimum (60,000) = 40,000 across 4 weeks = 10,000/week
        Assert.Equal(40_000m, plan.DiscretionaryPool);
        Assert.Equal(4, plan.WeeklyEnvelopes.Count);
        Assert.Equal(10_000m, plan.WeeklyEnvelopes[0].AllocatedBudget);
        Assert.Equal(5_000m, plan.WeeklyEnvelopes[0].RemainingBudget);
        Assert.Equal(-2_000m, plan.WeeklyEnvelopes[1].RemainingBudget);

        // Family solidarity report
        var solidarity = IrregularIncomePlanner.AnalyzeFamilySolidarity(
            solidaritySpend: 30_000m,
            monthlyIncome: 150_000m,
            monthlyTarget: 25_000m);

        Assert.Equal(20.0, solidarity.PercentageOfMonthlyIncome);
        Assert.True(solidarity.IsOverTarget);
    }

    private sealed class SyntheticTestSmsParser : IMobileMoneySmsParser
    {
        public string OperatorName => "TestMoMo";

        public bool CanParse(string smsBody) =>
            !string.IsNullOrWhiteSpace(smsBody) && smsBody.StartsWith("TEST_MOMO|", StringComparison.Ordinal);

        public bool TryParse(string smsBody, out ParsedMobileMoneyTransaction? transaction)
        {
            transaction = null;
            if (!CanParse(smsBody)) return false;

            var parts = smsBody.Split('|');
            var type = parts[1] == "RECV"
                ? MobileMoneyTransactionType.TransferReceived
                : MobileMoneyTransactionType.TransferSent;

            decimal amount = 0m;
            decimal fee = 0m;
            string? reference = null;

            foreach (var p in parts.Skip(2))
            {
                if (p.StartsWith("Amount:", StringComparison.OrdinalIgnoreCase))
                    MobileMoneyAmountParser.TryParseAmountXaf(p["Amount:".Length..], out amount);
                else if (p.StartsWith("Fee:", StringComparison.OrdinalIgnoreCase))
                    MobileMoneyAmountParser.TryParseAmountXaf(p["Fee:".Length..], out fee);
                else if (p.StartsWith("Ref:", StringComparison.OrdinalIgnoreCase))
                    reference = p["Ref:".Length..].Trim();
            }

            transaction = new ParsedMobileMoneyTransaction
            {
                Operator = OperatorName,
                Type = type,
                Amount = amount,
                Fee = fee,
                Reference = reference,
                Date = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc),
                RawMessage = smsBody
            };
            return true;
        }
    }
}
