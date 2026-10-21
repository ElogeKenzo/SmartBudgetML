using SmartBudgetML.Models;
using SmartBudgetML.Services.Database;
using Xunit;

namespace SmartBudgetML.Tests.Database;

public class SqliteRepositoryTests : IAsyncLifetime
{
    private readonly string _testDbPath;
    private readonly DatabaseService _databaseService;

    public SqliteRepositoryTests()
    {
        // Use an isolated temporary database for each test run
        _testDbPath = Path.Combine(Path.GetTempPath(), $"smartbudget_test_{Guid.NewGuid():N}.db3");
        _databaseService = new DatabaseService(_testDbPath);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        // Close this test's pooled connection first, otherwise Windows keeps the file locked.
        var connection = await _databaseService.GetConnectionAsync();
        await connection.CloseAsync();

        // Teardown must really remove the database: a failure here means a connection leaked.
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            var target = _testDbPath + suffix;
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }

        Assert.False(File.Exists(_testDbPath), $"Test database was not deleted: {_testDbPath}");
    }

    [Fact]
    public async Task InitializeAsync_ShouldCreateTablesAndSeedDefaultCategories()
    {
        // Act
        await _databaseService.InitializeAsync();
        var categories = await _databaseService.Categories.GetAllAsync();
        var settings = await _databaseService.Settings.GetAllAsync();

        // Assert
        Assert.NotEmpty(categories);
        Assert.True(categories.Count >= 17, "Should have seeded 17 Cameroonian default categories.");
        Assert.Contains(categories, c => c.Key == "food_market" && c.Name == "Alimentation / Marché");
        Assert.Contains(categories, c => c.Key == "transport" && c.Name == "Transport (Taxi / Moto / Car)");
        Assert.Contains(categories, c => c.Key == "salary" && c.IsIncome);

        Assert.Single(settings);
        Assert.True(settings[0].DarkMode, "Default theme should be dark mode.");
        Assert.Equal("XAF", settings[0].CurrencyCode);
        Assert.Equal("fr", settings[0].Language);
    }

    [Fact]
    public async Task ExpenseRepository_InsertAndGetById_ShouldPersistCorrectly()
    {
        // Arrange
        await _databaseService.InitializeAsync();
        var category = (await _databaseService.Categories.GetAllAsync()).First();

        var expense = new Expense
        {
            Description = "Whole Foods Market",
            Amount = 45.80m,
            Date = DateTime.Today,
            CategoryId = category.Id,
            IsMlPredicted = false,
            Notes = "Weekly organic groceries"
        };

        // Act
        var insertedCount = await _databaseService.Expenses.InsertAsync(expense);
        var retrieved = await _databaseService.Expenses.GetByIdAsync(expense.Id);

        // Assert
        Assert.Equal(1, insertedCount);
        Assert.NotNull(retrieved);
        Assert.Equal("Whole Foods Market", retrieved.Description);
        Assert.Equal(45.80m, retrieved.Amount);
        Assert.Equal(category.Id, retrieved.CategoryId);
        Assert.False(retrieved.IsMlPredicted);
    }

    [Fact]
    public async Task ExpenseRepository_FindAsync_ShouldFilterByDateAndCategory()
    {
        // Arrange
        await _databaseService.InitializeAsync();
        var categories = await _databaseService.Categories.GetAllAsync();
        var cat1 = categories[0];
        var cat2 = categories[1];

        await _databaseService.Expenses.InsertAsync(new Expense { Description = "Uber Ride", Amount = 15m, CategoryId = cat1.Id, Date = DateTime.Today });
        await _databaseService.Expenses.InsertAsync(new Expense { Description = "Coffee", Amount = 4.5m, CategoryId = cat1.Id, Date = DateTime.Today });
        await _databaseService.Expenses.InsertAsync(new Expense { Description = "Metro Ticket", Amount = 2.5m, CategoryId = cat2.Id, Date = DateTime.Today });

        // Act
        var cat1Expenses = await _databaseService.Expenses.FindAsync(e => e.CategoryId == cat1.Id);
        var totalCat1 = await _databaseService.Expenses.CountAsync(e => e.CategoryId == cat1.Id);

        // Assert
        Assert.Equal(2, cat1Expenses.Count);
        Assert.Equal(2, totalCat1);
    }

    [Fact]
    public async Task ExpenseRepository_DeleteAsync_ShouldRemoveEntity()
    {
        // Arrange
        await _databaseService.InitializeAsync();
        var expense = new Expense { Description = "Temporary Item", Amount = 10m, Date = DateTime.Today };
        await _databaseService.Expenses.InsertAsync(expense);

        // Act
        var deleteResult = await _databaseService.Expenses.DeleteAsync(expense);
        var afterDelete = await _databaseService.Expenses.GetByIdAsync(expense.Id);

        // Assert
        Assert.Equal(1, deleteResult);
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task UserProfileRepository_InsertAndUpdate_ShouldTrackChanges()
    {
        // Arrange
        await _databaseService.InitializeAsync();
        var profile = new UserProfile
        {
            FullName = "Abel Kenzo",
            CurrencySymbol = "€",
            MonthlyIncome = 3500m,
            TargetSavingsRate = 25.0,
            IsOnboardingCompleted = true
        };

        // Act
        await _databaseService.UserProfiles.InsertAsync(profile);
        profile.MonthlyIncome = 4000m;
        await _databaseService.UserProfiles.UpdateAsync(profile);

        var updated = await _databaseService.UserProfiles.GetByIdAsync(profile.Id);

        // Assert
        Assert.NotNull(updated);
        Assert.Equal(4000m, updated.MonthlyIncome);
        Assert.Equal("€", updated.CurrencySymbol);
    }
}
