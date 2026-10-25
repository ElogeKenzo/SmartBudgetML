using SQLite;
using SmartBudgetML.Models;
using SmartBudgetML.Resources.Strings;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.Localization;

namespace SmartBudgetML.Services.Tontines;

public static class TontineDraftValidator
{
    public const int MinimumMembers = 2;

    public static IReadOnlyList<TontineValidationError> Validate(TontineDraft draft)
    {
        var errors = new List<TontineValidationError>();

        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            errors.Add(TontineValidationError.NameRequired);
        }

        errors.AddRange(ValidateAmount(draft.ContributionAmount));

        var members = draft.Members ?? new List<TontineMemberDraft>();
        if (members.Count < MinimumMembers)
        {
            errors.Add(TontineValidationError.TooFewMembers);
        }

        if (members.Any(m => string.IsNullOrWhiteSpace(m.Name)))
        {
            errors.Add(TontineValidationError.MemberNameRequired);
        }

        if (members.Select(m => m.TurnOrder).Distinct().Count() != members.Count)
        {
            errors.Add(TontineValidationError.TurnOrdersNotUnique);
        }

        if (members.Any(m => m.TurnOrder < 1 || m.TurnOrder > members.Count))
        {
            errors.Add(TontineValidationError.TurnOrderOutOfRange);
        }

        if (members.Count(m => m.IsCurrentUser) != 1)
        {
            errors.Add(TontineValidationError.CurrentUserMissing);
        }

        return errors;
    }

    // XAF has no decimals (rule 8).
    public static IEnumerable<TontineValidationError> ValidateAmount(decimal amount)
    {
        if (amount <= 0m)
        {
            yield return TontineValidationError.AmountNotPositive;
        }
        else if (decimal.Truncate(amount) != amount)
        {
            yield return TontineValidationError.AmountNotWhole;
        }
    }
}

public class TontineService : ITontineService
{
    /// <summary>A contribution shows in the home banner from this many days before its date.</summary>
    public const int DueSoonDays = 7;

    private readonly IDatabaseService _database;
    private readonly IStringLocalizer<AppStrings> _localizer;

    public TontineService(IDatabaseService database, IStringLocalizer<AppStrings> localizer)
    {
        _database = database;
        _localizer = localizer;
    }

    public async Task<IReadOnlyList<Tontine>> GetTontinesAsync()
    {
        await _database.InitializeAsync();
        var tontines = await _database.Tontines.GetAllAsync();
        return tontines
            .OrderByDescending(t => t.IsActive)
            .ThenBy(t => t.StartDate)
            .ThenBy(t => t.Id)
            .ToList();
    }

    public async Task<TontineDetails?> GetDetailsAsync(int tontineId, DateTime today)
    {
        await _database.InitializeAsync();
        var tontine = await _database.Tontines.GetByIdAsync(tontineId);
        if (tontine is null)
        {
            return null;
        }

        var members = (await _database.TontineMembers.FindAsync(m => m.TontineId == tontineId))
            .OrderBy(m => m.TurnOrder).ThenBy(m => m.Id).ToList();
        var rounds = (await _database.TontineRounds.FindAsync(r => r.TontineId == tontineId))
            .OrderBy(r => r.RoundNumber).ToList();
        var contributions = await _database.TontineContributions.FindAsync(c => c.TontineId == tontineId);

        var currentRound = rounds.FirstOrDefault(r => !r.IsPaidOut);
        var beneficiary = currentRound is null
            ? null
            : members.FirstOrDefault(m => m.Id == currentRound.BeneficiaryMemberId);

        var statuses = currentRound is null
            ? new List<TontineMemberStatus>()
            : members
                .Where(m => !m.IsWithdrawn)
                .Select(m => new TontineMemberStatus(m, contributions.Any(c =>
                    c.RoundNumber == currentRound.RoundNumber && c.MemberId == m.Id && c.IsPaid)))
                .ToList();

        var membersById = members.ToDictionary(m => m.Id);
        var late = TontineCalculator.DetectLateContributions(rounds, members, contributions, today)
            .Where(c => membersById.ContainsKey(c.MemberId))
            .Select(c => new TontineLateItem(c.RoundNumber, c.Date, membersById[c.MemberId]))
            .OrderBy(l => l.RoundNumber).ThenBy(l => l.Member.TurnOrder)
            .ToList();

        var summary = TontineCalculator.CalculateUserSummary(tontine, rounds, members, contributions);

        return new TontineDetails(tontine, members, rounds, contributions, currentRound, beneficiary, statuses, late, summary);
    }

    public async Task<int> CreateAsync(TontineDraft draft)
    {
        var errors = TontineDraftValidator.Validate(draft);
        if (errors.Count > 0)
        {
            throw new TontineValidationException(errors);
        }

        var connection = await _database.GetConnectionAsync();
        var me = draft.Members.Single(m => m.IsCurrentUser);
        var tontine = new Tontine
        {
            Name = draft.Name.Trim(),
            ContributionAmount = draft.ContributionAmount,
            Frequency = draft.Frequency,
            StartDate = draft.StartDate.Date,
            TotalMembers = draft.Members.Count,
            MyTurnOrder = me.TurnOrder,
            IsActive = true
        };

        await connection.RunInTransactionAsync(db =>
        {
            db.Insert(tontine);

            var members = draft.Members
                .OrderBy(m => m.TurnOrder)
                .Select(m => new TontineMember
                {
                    TontineId = tontine.Id,
                    Name = m.Name.Trim(),
                    TurnOrder = m.TurnOrder,
                    IsCurrentUser = m.IsCurrentUser
                })
                .ToList();
            foreach (var member in members)
            {
                db.Insert(member);
            }

            db.InsertAll(TontineCalculator.GenerateSchedule(tontine, members));
        });

        return tontine.Id;
    }

    public async Task UpdateAsync(int tontineId, string name, decimal contributionAmount)
    {
        var errors = new List<TontineValidationError>();
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add(TontineValidationError.NameRequired);
        }
        errors.AddRange(TontineDraftValidator.ValidateAmount(contributionAmount));
        if (errors.Count > 0)
        {
            throw new TontineValidationException(errors);
        }

        var connection = await _database.GetConnectionAsync();
        await connection.RunInTransactionAsync(db =>
        {
            var tontine = RequireTontine(db, tontineId);
            tontine.Name = name.Trim();
            tontine.ContributionAmount = contributionAmount;
            db.Update(tontine);

            var members = db.Table<TontineMember>().Where(m => m.TontineId == tontineId).ToList();
            var pot = TontineCalculator.CalculatePot(tontine, members);
            foreach (var round in db.Table<TontineRound>().Where(r => r.TontineId == tontineId && !r.IsPaidOut).ToList())
            {
                round.ExpectedAmount = pot;
                db.Update(round);
            }
        });
    }

    public async Task DeleteAsync(int tontineId)
    {
        var connection = await _database.GetConnectionAsync();
        await connection.RunInTransactionAsync(db =>
        {
            db.Execute("DELETE FROM TontineContributions WHERE TontineId = ?", tontineId);
            db.Execute("DELETE FROM TontineRounds WHERE TontineId = ?", tontineId);
            db.Execute("DELETE FROM TontineMembers WHERE TontineId = ?", tontineId);
            db.Delete<Tontine>(tontineId);
        });
    }

    public async Task WithdrawMemberAsync(int tontineId, int memberId)
    {
        var connection = await _database.GetConnectionAsync();
        await connection.RunInTransactionAsync(db =>
        {
            var tontine = RequireTontine(db, tontineId);
            var member = db.Table<TontineMember>().FirstOrDefault(m => m.Id == memberId && m.TontineId == tontineId)
                ?? throw new InvalidOperationException($"Member {memberId} is not in tontine {tontineId}.");
            if (member.IsWithdrawn)
            {
                return;
            }

            member.IsWithdrawn = true;
            db.Update(member);

            var members = db.Table<TontineMember>().Where(m => m.TontineId == tontineId).ToList();
            var rounds = db.Table<TontineRound>().Where(r => r.TontineId == tontineId).ToList();
            var paidOut = rounds.Where(r => r.IsPaidOut).OrderBy(r => r.RoundNumber).ToList();
            var alreadyServed = paidOut.Select(r => r.BeneficiaryMemberId).ToHashSet();

            // Rounds already paid out stay as they are; the others go to the members still waiting, in turn order.
            foreach (var round in rounds.Where(r => !r.IsPaidOut))
            {
                db.Delete(round);
            }

            var waiting = members
                .Where(m => !m.IsWithdrawn && !alreadyServed.Contains(m.Id))
                .OrderBy(m => m.TurnOrder).ThenBy(m => m.Id)
                .ToList();
            var pot = TontineCalculator.CalculatePot(tontine, members);
            int roundNumber = paidOut.Count;
            foreach (var next in waiting)
            {
                roundNumber++;
                db.Insert(new TontineRound
                {
                    TontineId = tontineId,
                    RoundNumber = roundNumber,
                    BeneficiaryMemberId = next.Id,
                    ExpectedDate = TontineCalculator.GetRoundDate(tontine, roundNumber),
                    ExpectedAmount = pot
                });
            }

            tontine.TotalMembers = members.Count(m => !m.IsWithdrawn);
            tontine.IsActive = waiting.Count > 0;
            db.Update(tontine);
        });
    }

    public async Task<TontineRecordOutcome> RecordContributionAsync(int tontineId, int roundNumber, int memberId, DateTime date)
    {
        var categoryId = await RequireCategoryIdAsync("tontine");
        var connection = await _database.GetConnectionAsync();
        var outcome = TontineRecordOutcome.AlreadyRecorded;

        await connection.RunInTransactionAsync(db =>
        {
            var tontine = RequireTontine(db, tontineId);
            var round = db.Table<TontineRound>().FirstOrDefault(r => r.TontineId == tontineId && r.RoundNumber == roundNumber)
                ?? throw new InvalidOperationException($"Round {roundNumber} does not exist in tontine {tontineId}.");
            var member = db.Table<TontineMember>().FirstOrDefault(m => m.Id == memberId && m.TontineId == tontineId)
                ?? throw new InvalidOperationException($"Member {memberId} is not in tontine {tontineId}.");
            if (member.IsWithdrawn)
            {
                throw new InvalidOperationException($"Member {memberId} has left tontine {tontineId}.");
            }

            var contribution = db.Table<TontineContribution>().FirstOrDefault(c =>
                c.TontineId == tontineId && c.RoundNumber == roundNumber && c.MemberId == memberId);
            if (contribution is null)
            {
                contribution = new TontineContribution
                {
                    TontineId = tontineId,
                    RoundNumber = roundNumber,
                    MemberId = memberId,
                    Amount = tontine.ContributionAmount,
                    Date = date.Date,
                    IsPaid = true,
                    IsLate = date.Date > round.ExpectedDate.Date
                };
                db.Insert(contribution);
                outcome = TontineRecordOutcome.Recorded;
            }
            else if (!contribution.IsPaid)
            {
                contribution.IsPaid = true;
                contribution.Date = date.Date;
                contribution.IsLate = date.Date > round.ExpectedDate.Date;
                db.Update(contribution);
                outcome = TontineRecordOutcome.Recorded;
            }

            if (member.IsCurrentUser)
            {
                InsertLinkedExpenseOnce(db, new Expense
                {
                    Description = _localizer["Tontines_ContributionExpense", tontine.Name, roundNumber].Value,
                    Amount = contribution.Amount,
                    Date = contribution.Date,
                    CategoryId = categoryId,
                    IsFixedOrRecurring = true,
                    SourceType = ExpenseSources.TontineContribution,
                    SourceId = contribution.Id
                });
            }
        });

        return outcome;
    }

    public async Task<TontineRecordOutcome> RecordPayoutAsync(int tontineId, int roundNumber, DateTime date)
    {
        var categoryId = await RequireCategoryIdAsync("transfers_received");
        var connection = await _database.GetConnectionAsync();
        var outcome = TontineRecordOutcome.AlreadyRecorded;

        await connection.RunInTransactionAsync(db =>
        {
            var tontine = RequireTontine(db, tontineId);
            var round = db.Table<TontineRound>().FirstOrDefault(r => r.TontineId == tontineId && r.RoundNumber == roundNumber)
                ?? throw new InvalidOperationException($"Round {roundNumber} does not exist in tontine {tontineId}.");

            if (!round.IsPaidOut)
            {
                round.IsPaidOut = true;
                db.Update(round);
                outcome = TontineRecordOutcome.Recorded;

                if (db.Table<TontineRound>().Count(r => r.TontineId == tontineId && !r.IsPaidOut) == 0)
                {
                    tontine.IsActive = false;
                    db.Update(tontine);
                }
            }

            var beneficiary = db.Table<TontineMember>().FirstOrDefault(m => m.Id == round.BeneficiaryMemberId);
            if (beneficiary?.IsCurrentUser == true)
            {
                InsertLinkedExpenseOnce(db, new Expense
                {
                    Description = _localizer["Tontines_PayoutIncome", tontine.Name, roundNumber].Value,
                    Amount = round.ExpectedAmount,
                    Date = date.Date,
                    CategoryId = categoryId,
                    IsIncome = true,
                    IsExceptional = true,
                    SourceType = ExpenseSources.TontinePayout,
                    SourceId = round.Id
                });
            }
        });

        return outcome;
    }

    public async Task<TontineOverview> GetOverviewAsync(DateTime today)
    {
        await _database.InitializeAsync();
        var active = (await _database.Tontines.FindAsync(t => t.IsActive)).ToList();
        var dues = new List<TontineDue>();
        DateTime? nextDue = null;

        foreach (var tontine in active)
        {
            var me = await _database.TontineMembers.FirstOrDefaultAsync(m =>
                m.TontineId == tontine.Id && m.IsCurrentUser && !m.IsWithdrawn);
            if (me is null)
            {
                continue;
            }

            var paidRounds = (await _database.TontineContributions.FindAsync(c =>
                    c.TontineId == tontine.Id && c.MemberId == me.Id && c.IsPaid))
                .Select(c => c.RoundNumber)
                .ToHashSet();
            var unpaid = (await _database.TontineRounds.FindAsync(r => r.TontineId == tontine.Id))
                .Where(r => !paidRounds.Contains(r.RoundNumber))
                .OrderBy(r => r.RoundNumber)
                .ToList();

            if (unpaid.Count > 0 && (nextDue is null || unpaid[0].ExpectedDate < nextDue))
            {
                nextDue = unpaid[0].ExpectedDate.Date;
            }

            dues.AddRange(unpaid
                .Where(r => r.ExpectedDate.Date <= today.Date.AddDays(DueSoonDays))
                .Select(r => new TontineDue(
                    tontine.Id,
                    tontine.Name,
                    r.RoundNumber,
                    tontine.ContributionAmount,
                    r.ExpectedDate.Date,
                    IsOverdue: r.ExpectedDate.Date < today.Date)));
        }

        return new TontineOverview(
            active.Count,
            nextDue,
            dues.OrderBy(d => d.DueDate).ThenBy(d => d.TontineId).ToList());
    }

    private static Tontine RequireTontine(SQLiteConnection db, int tontineId) =>
        db.Find<Tontine>(tontineId) ?? throw new InvalidOperationException($"Tontine {tontineId} does not exist.");

    private static void InsertLinkedExpenseOnce(SQLiteConnection db, Expense expense)
    {
        var sourceType = expense.SourceType;
        var sourceId = expense.SourceId;
        bool exists = db.Table<Expense>().Count(e => e.SourceType == sourceType && e.SourceId == sourceId) > 0;
        if (!exists)
        {
            db.Insert(expense);
        }
    }

    private async Task<int> RequireCategoryIdAsync(string key)
    {
        await _database.InitializeAsync();
        var category = await _database.Categories.FirstOrDefaultAsync(c => c.Key == key);
        return category?.Id ?? throw new InvalidOperationException($"Category '{key}' is missing.");
    }
}
