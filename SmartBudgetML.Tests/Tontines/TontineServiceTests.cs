using SmartBudgetML.Models;
using SmartBudgetML.Resources.Strings;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.Localization;
using SmartBudgetML.Services.Security;
using SmartBudgetML.Services.Tontines;
using Xunit;

namespace SmartBudgetML.Tests.Tontines;

/// <summary>
/// Lot 8E (N1): tontines live in the encrypted database. Every test uses a real temporary
/// SQLCipher file and reopens it where persistence matters. Member names are placeholders.
/// </summary>
public class TontineServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly InMemorySecureKeyStore _keys = new();

    public TontineServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SmartBudgetML_Tontines_" + Guid.NewGuid().ToString("N"));
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

    private string DbPath => Path.Combine(_tempDir, "tontines.db3");

    private async Task<(DatabaseService Db, TontineService Service)> OpenAsync()
    {
        var db = new DatabaseService(DbPath, _keys);
        await db.InitializeAsync();
        return (db, new TontineService(db, new ResxStringLocalizer<AppStrings>()));
    }

    private static async Task CloseAsync(DatabaseService db)
    {
        var connection = await db.GetConnectionAsync();
        await connection.CloseAsync();
    }

    private static TontineDraft Draft(
        int memberCount = 4,
        int myTurn = 2,
        TontineFrequency frequency = TontineFrequency.Monthly,
        DateTime? start = null,
        decimal amount = 10_000m)
    {
        var draft = new TontineDraft
        {
            Name = "Njangi test",
            ContributionAmount = amount,
            Frequency = frequency,
            StartDate = start ?? new DateTime(2026, 11, 15)
        };
        int other = 0;
        for (int order = 1; order <= memberCount; order++)
        {
            draft.Members.Add(order == myTurn
                ? new TontineMemberDraft("Moi", order, IsCurrentUser: true)
                : new TontineMemberDraft($"Membre {(char)('A' + other++)}", order, IsCurrentUser: false));
        }
        return draft;
    }

    [Fact]
    public async Task Create_ThenReopenDatabase_ReturnsSameTontineMembersAndRounds()
    {
        var (db, service) = await OpenAsync();
        var id = await service.CreateAsync(Draft());
        await CloseAsync(db);

        var (_, reopened) = await OpenAsync();
        var details = await reopened.GetDetailsAsync(id, new DateTime(2026, 11, 1));

        Assert.NotNull(details);
        Assert.Equal("Njangi test", details!.Tontine.Name);
        Assert.Equal(10_000m, details.Tontine.ContributionAmount);
        Assert.Equal(4, details.Members.Count);
        Assert.Equal(2, details.CurrentUser!.TurnOrder);
        Assert.Equal(4, details.Rounds.Count);
        Assert.Equal(40_000m, details.Summary.PotAmount);
        Assert.All(details.Rounds, r => Assert.Equal(40_000m, r.ExpectedAmount));
        Assert.Equal(details.Members[0].Id, details.CurrentBeneficiary!.Id);
        Assert.Single(await reopened.GetTontinesAsync());
    }

    [Fact]
    public async Task RecordContribution_Twice_StoresOneContributionAndOneExpense()
    {
        var (db, service) = await OpenAsync();
        var id = await service.CreateAsync(Draft());
        var me = (await service.GetDetailsAsync(id, DateTime.Today))!.CurrentUser!;

        var first = await service.RecordContributionAsync(id, 1, me.Id, new DateTime(2026, 11, 15));
        var second = await service.RecordContributionAsync(id, 1, me.Id, new DateTime(2026, 11, 16));

        Assert.Equal(TontineRecordOutcome.Recorded, first);
        Assert.Equal(TontineRecordOutcome.AlreadyRecorded, second);
        Assert.Single(await db.TontineContributions.GetAllAsync());

        var expenses = await db.Expenses.GetAllAsync();
        var expense = Assert.Single(expenses);
        var tontineCategory = await db.Categories.FirstOrDefaultAsync(c => c.Key == "tontine");
        Assert.Equal(tontineCategory!.Id, expense.CategoryId);
        Assert.Equal(10_000m, expense.Amount);
        Assert.False(expense.IsIncome);
        Assert.Equal(ExpenseSources.TontineContribution, expense.SourceType);
        Assert.Equal((await db.TontineContributions.GetAllAsync())[0].Id, expense.SourceId);
    }

    [Fact]
    public async Task RecordContribution_OfAnotherMember_CreatesNoExpense()
    {
        var (db, service) = await OpenAsync();
        var id = await service.CreateAsync(Draft());
        var other = (await service.GetDetailsAsync(id, DateTime.Today))!.Members.First(m => !m.IsCurrentUser);

        await service.RecordContributionAsync(id, 1, other.Id, new DateTime(2026, 11, 15));

        Assert.Single(await db.TontineContributions.GetAllAsync());
        Assert.Empty(await db.Expenses.GetAllAsync());
    }

    [Fact]
    public async Task UniqueIndex_RejectsDuplicateContributionRow()
    {
        var (db, service) = await OpenAsync();
        var id = await service.CreateAsync(Draft());
        var row = new TontineContribution { TontineId = id, RoundNumber = 1, MemberId = 1, Amount = 10_000m, IsPaid = true };
        await db.TontineContributions.InsertAsync(row);

        await Assert.ThrowsAsync<SQLite.SQLiteException>(() => db.TontineContributions.InsertAsync(
            new TontineContribution { TontineId = id, RoundNumber = 1, MemberId = 1, Amount = 10_000m, IsPaid = true }));
    }

    [Fact]
    public async Task RecordPayout_ToUser_BooksOneExceptionalIncomeAndAdvancesRound()
    {
        var (db, service) = await OpenAsync();
        var id = await service.CreateAsync(Draft(myTurn: 1));

        var first = await service.RecordPayoutAsync(id, 1, new DateTime(2026, 11, 15));
        var second = await service.RecordPayoutAsync(id, 1, new DateTime(2026, 11, 15));

        Assert.Equal(TontineRecordOutcome.Recorded, first);
        Assert.Equal(TontineRecordOutcome.AlreadyRecorded, second);
        var income = Assert.Single(await db.Expenses.GetAllAsync());
        var transfers = await db.Categories.FirstOrDefaultAsync(c => c.Key == "transfers_received");
        Assert.True(income.IsIncome);
        Assert.True(income.IsExceptional);
        Assert.Equal(transfers!.Id, income.CategoryId);
        Assert.Equal(40_000m, income.Amount);
        Assert.Equal(ExpenseSources.TontinePayout, income.SourceType);

        var details = await service.GetDetailsAsync(id, new DateTime(2026, 11, 16));
        Assert.Equal(2, details!.CurrentRound!.RoundNumber);
        Assert.Equal(40_000m, details.Summary.TotalReceivedByUser);
        Assert.Equal(1, details.Summary.CompletedRounds);
    }

    [Fact]
    public async Task RecordPayout_ToOtherMember_BooksNoIncome_AndLastPayoutClosesTontine()
    {
        var (db, service) = await OpenAsync();
        var id = await service.CreateAsync(Draft(memberCount: 2, myTurn: 2));

        await service.RecordPayoutAsync(id, 1, new DateTime(2026, 11, 15));
        Assert.Empty(await db.Expenses.GetAllAsync());
        Assert.True((await db.Tontines.GetByIdAsync(id))!.IsActive);

        await service.RecordPayoutAsync(id, 2, new DateTime(2026, 12, 15));
        Assert.False((await db.Tontines.GetByIdAsync(id))!.IsActive);
        Assert.True((await service.GetDetailsAsync(id, new DateTime(2026, 12, 16)))!.IsFinished);
    }

    [Fact]
    public async Task LateContributions_AreComputedFromStoredRows()
    {
        var (_, service) = await OpenAsync();
        var id = await service.CreateAsync(Draft(memberCount: 3, myTurn: 1));
        var members = (await service.GetDetailsAsync(id, DateTime.Today))!.Members;
        await service.RecordContributionAsync(id, 1, members[0].Id, new DateTime(2026, 11, 15));
        await service.RecordContributionAsync(id, 1, members[1].Id, new DateTime(2026, 11, 15));

        // Round 1 (15 Nov) is past, round 2 (15 Dec) is not.
        var details = await service.GetDetailsAsync(id, new DateTime(2026, 11, 20));

        var late = Assert.Single(details!.LateContributions);
        Assert.Equal(1, late.RoundNumber);
        Assert.Equal(members[2].Id, late.Member.Id);
        Assert.Equal(new DateTime(2026, 11, 15), late.DueDate);
    }

    [Fact]
    public async Task Schedule_CrossesYearEnd_ForMonthlyAndWeekly()
    {
        var (_, service) = await OpenAsync();
        var monthly = await service.CreateAsync(Draft(start: new DateTime(2026, 11, 30)));
        var weekly = await service.CreateAsync(Draft(frequency: TontineFrequency.Weekly, start: new DateTime(2026, 12, 24)));
        var biWeekly = await service.CreateAsync(Draft(frequency: TontineFrequency.BiWeekly, start: new DateTime(2026, 12, 24)));

        var monthlyDates = (await service.GetDetailsAsync(monthly, DateTime.Today))!.Rounds.Select(r => r.ExpectedDate);
        var weeklyDates = (await service.GetDetailsAsync(weekly, DateTime.Today))!.Rounds.Select(r => r.ExpectedDate);
        var biWeeklyDates = (await service.GetDetailsAsync(biWeekly, DateTime.Today))!.Rounds.Select(r => r.ExpectedDate);

        Assert.Equal(new[] { new DateTime(2026, 11, 30), new DateTime(2026, 12, 30), new DateTime(2027, 1, 30), new DateTime(2027, 2, 28) }, monthlyDates);
        Assert.Equal(new[] { new DateTime(2026, 12, 24), new DateTime(2026, 12, 31), new DateTime(2027, 1, 7), new DateTime(2027, 1, 14) }, weeklyDates);
        Assert.Equal(new[] { new DateTime(2026, 12, 24), new DateTime(2027, 1, 7), new DateTime(2027, 1, 21), new DateTime(2027, 2, 4) }, biWeeklyDates);
    }

    [Fact]
    public async Task WithdrawMember_RebuildsUnpaidRoundsAndShrinksPot()
    {
        var (_, service) = await OpenAsync();
        var id = await service.CreateAsync(Draft(memberCount: 4, myTurn: 4));
        var members = (await service.GetDetailsAsync(id, DateTime.Today))!.Members;
        await service.RecordPayoutAsync(id, 1, new DateTime(2026, 11, 15));

        // The member due to receive round 2 leaves.
        await service.WithdrawMemberAsync(id, members[1].Id);

        var details = await service.GetDetailsAsync(id, new DateTime(2026, 11, 20));
        Assert.Equal(3, details!.Rounds.Count);
        Assert.Equal(new[] { members[0].Id, members[2].Id, members[3].Id }, details.Rounds.Select(r => r.BeneficiaryMemberId));
        Assert.True(details.Rounds[0].IsPaidOut);
        Assert.Equal(40_000m, details.Rounds[0].ExpectedAmount);
        Assert.Equal(30_000m, details.Rounds[1].ExpectedAmount);
        Assert.Equal(new DateTime(2026, 12, 15), details.Rounds[1].ExpectedDate);
        Assert.Equal(members[2].Id, details.CurrentBeneficiary!.Id);
        Assert.Equal(3, details.Tontine.TotalMembers);
        Assert.DoesNotContain(details.CurrentRoundStatuses, s => s.Member.Id == members[1].Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RecordContributionAsync(id, 2, members[1].Id, new DateTime(2026, 12, 15)));
    }

    [Fact]
    public async Task Overview_ListsDueAndOverdueContributionsOfTheUser()
    {
        var (_, service) = await OpenAsync();
        var id = await service.CreateAsync(Draft(memberCount: 3, myTurn: 2));
        var me = (await service.GetDetailsAsync(id, DateTime.Today))!.CurrentUser!;

        var before = await service.GetOverviewAsync(new DateTime(2026, 11, 1));
        Assert.Equal(1, before.ActiveCount);
        Assert.Equal(new DateTime(2026, 11, 15), before.NextDueDate);
        Assert.Empty(before.Dues);

        var dueSoon = await service.GetOverviewAsync(new DateTime(2026, 11, 10));
        var due = Assert.Single(dueSoon.Dues);
        Assert.False(due.IsOverdue);
        Assert.Equal(10_000m, due.Amount);

        var late = await service.GetOverviewAsync(new DateTime(2026, 11, 16));
        Assert.True(Assert.Single(late.Dues).IsOverdue);

        await service.RecordContributionAsync(id, 1, me.Id, new DateTime(2026, 11, 16));
        var paid = await service.GetOverviewAsync(new DateTime(2026, 11, 16));
        Assert.Empty(paid.Dues);
        Assert.Equal(new DateTime(2026, 12, 15), paid.NextDueDate);
    }

    [Fact]
    public async Task Update_ChangesNameAndPotOfUnpaidRounds()
    {
        var (_, service) = await OpenAsync();
        var id = await service.CreateAsync(Draft(memberCount: 2));
        await service.RecordPayoutAsync(id, 1, new DateTime(2026, 11, 15));

        await service.UpdateAsync(id, "Njangi renommée", 15_000m);

        var details = await service.GetDetailsAsync(id, DateTime.Today);
        Assert.Equal("Njangi renommée", details!.Tontine.Name);
        Assert.Equal(20_000m, details.Rounds[0].ExpectedAmount);
        Assert.Equal(30_000m, details.Rounds[1].ExpectedAmount);
    }

    [Fact]
    public async Task Delete_RemovesTontineRowsButKeepsBookedExpenses()
    {
        var (db, service) = await OpenAsync();
        var id = await service.CreateAsync(Draft());
        var me = (await service.GetDetailsAsync(id, DateTime.Today))!.CurrentUser!;
        await service.RecordContributionAsync(id, 1, me.Id, new DateTime(2026, 11, 15));

        await service.DeleteAsync(id);

        Assert.Null(await service.GetDetailsAsync(id, DateTime.Today));
        Assert.Empty(await db.TontineMembers.GetAllAsync());
        Assert.Empty(await db.TontineRounds.GetAllAsync());
        Assert.Empty(await db.TontineContributions.GetAllAsync());
        Assert.Single(await db.Expenses.GetAllAsync());
    }

    [Theory]
    [InlineData(0, TontineValidationError.AmountNotPositive)]
    [InlineData(-5, TontineValidationError.AmountNotPositive)]
    [InlineData(2500.5, TontineValidationError.AmountNotWhole)]
    public void Validator_RejectsBadAmounts(double amount, TontineValidationError expected)
    {
        var errors = TontineDraftValidator.Validate(Draft(amount: (decimal)amount));
        Assert.Contains(expected, errors);
    }

    [Fact]
    public void Validator_RejectsOneMemberDuplicateOrdersAndMissingUser()
    {
        Assert.Contains(TontineValidationError.TooFewMembers, TontineDraftValidator.Validate(Draft(memberCount: 1, myTurn: 1)));

        var duplicate = Draft();
        duplicate.Members[1] = duplicate.Members[1] with { TurnOrder = 1 };
        Assert.Contains(TontineValidationError.TurnOrdersNotUnique, TontineDraftValidator.Validate(duplicate));

        var outOfRange = Draft();
        outOfRange.Members[0] = outOfRange.Members[0] with { TurnOrder = 9 };
        Assert.Contains(TontineValidationError.TurnOrderOutOfRange, TontineDraftValidator.Validate(outOfRange));

        Assert.Contains(TontineValidationError.CurrentUserMissing, TontineDraftValidator.Validate(Draft(myTurn: 0)));

        var blankName = Draft();
        blankName.Members[0] = blankName.Members[0] with { Name = "  " };
        Assert.Contains(TontineValidationError.MemberNameRequired, TontineDraftValidator.Validate(blankName));

        Assert.Empty(TontineDraftValidator.Validate(Draft()));
    }

    [Fact]
    public async Task Create_WithInvalidDraft_ThrowsAndStoresNothing()
    {
        var (db, service) = await OpenAsync();
        var ex = await Assert.ThrowsAsync<TontineValidationException>(() => service.CreateAsync(Draft(memberCount: 1, myTurn: 1)));
        Assert.Contains(TontineValidationError.TooFewMembers, ex.Errors);
        Assert.Empty(await db.Tontines.GetAllAsync());
    }

    [Fact]
    public async Task ExistingDatabase_WithoutSourceColumns_IsMigratedWithoutLoss()
    {
        // Database created by the 8D schema: Expenses without IsExceptional, SourceType, SourceId.
        var (db, _) = await OpenAsync();
        await db.Expenses.InsertAsync(new Expense { Description = "Ancienne dépense", Amount = 700m, CategoryId = 1, Date = new DateTime(2026, 9, 1) });
        var connection = await db.GetConnectionAsync();
        await connection.ExecuteAsync("DROP INDEX IF EXISTS UX_Expenses_Source;");
        await connection.ExecuteAsync("CREATE TABLE Expenses_old AS SELECT Id, Description, Amount, Date, CategoryId, PredictedCategoryId, IsMlPredicted, PredictionConfidence, Notes, IsIncome, IsFixedOrRecurring, CreatedAt FROM Expenses;");
        await connection.ExecuteAsync("DROP TABLE Expenses;");
        await connection.ExecuteAsync("ALTER TABLE Expenses_old RENAME TO Expenses;");
        await CloseAsync(db);

        var (reopened, _) = await OpenAsync();

        var expense = Assert.Single(await reopened.Expenses.GetAllAsync());
        Assert.Equal("Ancienne dépense", expense.Description);
        Assert.Null(expense.SourceType);
        Assert.False(expense.IsExceptional);
    }
}
