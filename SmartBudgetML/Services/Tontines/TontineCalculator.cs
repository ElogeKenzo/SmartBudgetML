using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Tontines;

public sealed record TontineUserSummary(
    decimal PotAmount,
    decimal TotalContributedByUser,
    decimal TotalReceivedByUser,
    decimal NetUserBalance,
    int CompletedRounds,
    int RemainingRounds);

/// <summary>
/// Pure business logic for Cameroonian Tontines / Njangi:
/// pot calculation, schedule generation (weekly/biweekly/monthly across year boundaries),
/// member withdrawal handling, late contribution detection, and net user cash vs. budget separation.
/// </summary>
public static class TontineCalculator
{
    public static decimal CalculatePot(Tontine tontine, IReadOnlyList<TontineMember>? members = null)
    {
        if (tontine == null || tontine.ContributionAmount <= 0m)
        {
            return 0m;
        }

        int activeCount = members != null && members.Count > 0
            ? members.Count(m => !m.IsWithdrawn)
            : Math.Max(0, tontine.TotalMembers);

        return tontine.ContributionAmount * activeCount;
    }

    public static List<TontineRound> GenerateSchedule(Tontine tontine, IReadOnlyList<TontineMember> members)
    {
        var activeMembers = members
            .Where(m => !m.IsWithdrawn)
            .OrderBy(m => m.TurnOrder)
            .ThenBy(m => m.Id)
            .ToList();

        var pot = CalculatePot(tontine, activeMembers);
        var rounds = new List<TontineRound>(activeMembers.Count);

        for (int i = 0; i < activeMembers.Count; i++)
        {
            var expectedDate = tontine.Frequency switch
            {
                TontineFrequency.Weekly => tontine.StartDate.Date.AddDays(7 * i),
                TontineFrequency.BiWeekly => tontine.StartDate.Date.AddDays(14 * i),
                _ => tontine.StartDate.Date.AddMonths(i)
            };

            rounds.Add(new TontineRound
            {
                TontineId = tontine.Id,
                RoundNumber = i + 1,
                BeneficiaryMemberId = activeMembers[i].Id,
                ExpectedDate = expectedDate,
                ExpectedAmount = pot,
                IsPaidOut = false
            });
        }

        return rounds;
    }

    public static TontineMember? GetNextBeneficiary(IReadOnlyList<TontineRound> rounds, IReadOnlyList<TontineMember> members)
    {
        var nextRound = rounds
            .Where(r => !r.IsPaidOut)
            .OrderBy(r => r.RoundNumber)
            .FirstOrDefault();

        if (nextRound == null)
        {
            return null;
        }

        return members.FirstOrDefault(m => m.Id == nextRound.BeneficiaryMemberId && !m.IsWithdrawn);
    }

    public static List<TontineContribution> DetectLateContributions(
        IReadOnlyList<TontineRound> rounds,
        IReadOnlyList<TontineMember> members,
        IReadOnlyList<TontineContribution> recordedContributions,
        DateTime referenceDate)
    {
        var late = new List<TontineContribution>();
        var activeMembers = members.Where(m => !m.IsWithdrawn).ToList();

        foreach (var round in rounds.Where(r => r.ExpectedDate.Date < referenceDate.Date))
        {
            foreach (var member in activeMembers)
            {
                var existing = recordedContributions.FirstOrDefault(c =>
                    c.TontineId == round.TontineId &&
                    c.RoundNumber == round.RoundNumber &&
                    c.MemberId == member.Id);

                if (existing == null || !existing.IsPaid)
                {
                    late.Add(new TontineContribution
                    {
                        TontineId = round.TontineId,
                        RoundNumber = round.RoundNumber,
                        MemberId = member.Id,
                        Amount = existing?.Amount ?? 0m,
                        Date = round.ExpectedDate,
                        IsPaid = false,
                        IsLate = true
                    });
                }
            }
        }

        return late;
    }

    public static TontineUserSummary CalculateUserSummary(
        Tontine tontine,
        IReadOnlyList<TontineRound> rounds,
        IReadOnlyList<TontineMember> members,
        IReadOnlyList<TontineContribution> contributions)
    {
        var pot = CalculatePot(tontine, members);
        var currentUser = members.FirstOrDefault(m => m.IsCurrentUser);

        decimal totalContributed = 0m;
        decimal totalReceived = 0m;

        if (currentUser != null)
        {
            totalContributed = contributions
                .Where(c => c.MemberId == currentUser.Id && c.IsPaid)
                .Sum(c => c.Amount);

            totalReceived = rounds
                .Where(r => r.BeneficiaryMemberId == currentUser.Id && r.IsPaidOut)
                .Sum(r => r.ExpectedAmount > 0m ? r.ExpectedAmount : pot);
        }

        int completed = rounds.Count(r => r.IsPaidOut);
        int remaining = Math.Max(0, rounds.Count - completed);

        return new TontineUserSummary(
            PotAmount: pot,
            TotalContributedByUser: totalContributed,
            TotalReceivedByUser: totalReceived,
            NetUserBalance: totalReceived - totalContributed,
            CompletedRounds: completed,
            RemainingRounds: remaining);
    }
}
