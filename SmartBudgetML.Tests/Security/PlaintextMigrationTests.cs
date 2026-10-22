using SmartBudgetML.Models;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.Security;
using Xunit;

namespace SmartBudgetML.Tests.Security;

/// <summary>
/// Lot 8C step 4: migrating a plaintext database to SQLCipher must keep every row with its
/// original id (so Expense.CategoryId still points to the same category) and every table,
/// including the tontine tables.
/// </summary>
public class PlaintextMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"migration_gaps_{Guid.NewGuid():N}");
    private readonly string _dbPath;

    public PlaintextMigrationTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "legacy.db3");
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Migration_PreservesIdsWithGaps_CategoryLinks_AndTontineTables()
    {
        // 1. Plaintext database with id gaps in categories and expenses.
        var plain = new DatabaseService(_dbPath, keyStore: null);
        await plain.InitializeAsync();

        var categories = (await plain.Categories.GetAllAsync()).OrderBy(c => c.Id).ToList();
        Assert.True(categories.Count >= 5);
        await plain.Categories.DeleteAsync(categories[1]);
        await plain.Categories.DeleteAsync(categories[2]);
        var linked = categories[^1];

        var expenseA = new Expense { Description = "A", Amount = 1000m, CategoryId = categories[0].Id, Date = new DateTime(2026, 10, 1) };
        var expenseB = new Expense { Description = "B", Amount = 2000m, CategoryId = linked.Id, Date = new DateTime(2026, 10, 2) };
        var expenseC = new Expense { Description = "C", Amount = 3000m, CategoryId = linked.Id, Date = new DateTime(2026, 10, 3) };
        await plain.Expenses.InsertAsync(expenseA);
        await plain.Expenses.InsertAsync(expenseB);
        await plain.Expenses.InsertAsync(expenseC);
        await plain.Expenses.DeleteAsync(expenseA);

        var tontine = new Tontine { Name = "Njangi bureau", ContributionAmount = 10000m, TotalMembers = 5 };
        await plain.Tontines.InsertAsync(tontine);
        await plain.TontineMembers.InsertAsync(new TontineMember { TontineId = tontine.Id, Name = "Moi", TurnOrder = 2, IsCurrentUser = true });

        var before = await SnapshotAsync(plain);
        var conn = await plain.GetConnectionAsync();
        await conn.CloseAsync();

        // 2. Reopen with a key store: triggers the plaintext -> encrypted migration.
        var encrypted = new DatabaseService(_dbPath, new InMemorySecureKeyStore());
        await encrypted.InitializeAsync();
        Assert.True(encrypted.IsEncrypted);

        var after = await SnapshotAsync(encrypted);
        // Seeding re-adds the deleted default categories with new ids; every original row keeps its id.
        Assert.Subset(after.CategoryIds.Split(',').ToHashSet(), before.CategoryIds.Split(',').ToHashSet());
        Assert.Equal(before.ExpenseLinks, after.ExpenseLinks);
        Assert.Equal(before.TontineIds, after.TontineIds);
        Assert.Equal(before.MemberLinks, after.MemberLinks);

        var migratedB = (await encrypted.Expenses.GetAllAsync()).Single(e => e.Description == "B");
        var migratedCategory = (await encrypted.Categories.GetAllAsync()).Single(c => c.Id == migratedB.CategoryId);
        Assert.Equal(linked.Key, migratedCategory.Key);

        var encConn = await encrypted.GetConnectionAsync();
        await encConn.CloseAsync();
    }

    private static async Task<Snapshot> SnapshotAsync(DatabaseService db)
    {
        var categories = await db.Categories.GetAllAsync();
        var expenses = await db.Expenses.GetAllAsync();
        var tontines = await db.Tontines.GetAllAsync();
        var members = await db.TontineMembers.GetAllAsync();
        return new Snapshot(
            string.Join(",", categories.OrderBy(c => c.Id).Select(c => $"{c.Id}:{c.Key}")),
            string.Join(",", expenses.OrderBy(e => e.Id).Select(e => $"{e.Id}->{e.CategoryId}:{e.Amount}")),
            string.Join(",", tontines.OrderBy(t => t.Id).Select(t => $"{t.Id}:{t.Name}")),
            string.Join(",", members.OrderBy(m => m.Id).Select(m => $"{m.Id}->{m.TontineId}:{m.Name}")));
    }

    private sealed record Snapshot(string CategoryIds, string ExpenseLinks, string TontineIds, string MemberLinks);
}
