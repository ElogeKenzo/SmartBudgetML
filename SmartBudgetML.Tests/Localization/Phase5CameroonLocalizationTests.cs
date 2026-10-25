using SmartBudgetML.Models;
using SmartBudgetML.Resources.Strings;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.Localization;
using SmartBudgetML.Services.Security;
using SQLite;
using Xunit;

namespace SmartBudgetML.Tests.Localization;

public class Phase5CameroonLocalizationTests
{
    [Fact]
    public void ResxStringLocalizer_DefaultLanguageShouldBeFrench_AndFrEnKeysMustHave100PercentParity()
    {
        var localizer = new ResxStringLocalizer<AppStrings>();

        Assert.Equal("fr", localizer.CurrentLanguage);

        var frDict = localizer.GetAllStrings("fr");
        var enDict = localizer.GetAllStrings("en");

        Assert.True(frDict.Count >= 60, $"Expected at least 60 localized UI keys, got {frDict.Count}");
        Assert.Equal(frDict.Count, enDict.Count);

        var missingInEn = frDict.Keys.Except(enDict.Keys, StringComparer.Ordinal).ToList();
        var missingInFr = enDict.Keys.Except(frDict.Keys, StringComparer.Ordinal).ToList();

        Assert.Empty(missingInEn);
        Assert.Empty(missingInFr);

        foreach (var kvp in frDict)
        {
            Assert.False(string.IsNullOrWhiteSpace(kvp.Value), $"FR value for key '{kvp.Key}' is empty.");
            Assert.False(string.IsNullOrWhiteSpace(enDict[kvp.Key]), $"EN value for key '{kvp.Key}' is empty.");
        }

        // Verify runtime language switching
        Assert.Equal("Tableau de bord", localizer["Nav_Home"].Value);
        localizer.SetLanguage("en");
        Assert.Equal("Dashboard", localizer["Nav_Home"].Value);
    }

    [Fact]
    public async Task DefaultCurrencyAndLanguage_ShouldBeXafFcfaAndFrench_AcrossModelsAndDatabaseSeed()
    {
        var defaultSettings = new AppSettings();
        var defaultProfile = new UserProfile();

        Assert.Equal("XAF", defaultSettings.CurrencyCode);
        Assert.Equal("fr", defaultSettings.Language);
        Assert.Equal("FCFA", defaultProfile.CurrencySymbol);

        var dbPath = Path.Combine(Path.GetTempPath(), $"sb_p5_seed_{Guid.NewGuid():N}.db3");
        try
        {
            var db = new DatabaseService(dbPath, new InMemorySecureKeyStore());
            await db.InitializeAsync();

            var seededSettings = (await db.Settings.GetAllAsync()).Single();
            Assert.Equal("XAF", seededSettings.CurrencyCode);
            Assert.Equal("fr", seededSettings.Language);

            var categories = await db.Categories.GetAllAsync();
            // 14 expense categories + 3 income categories = 17 Cameroonian default categories
            Assert.True(categories.Count >= 17, $"Expected >= 17 Cameroonian categories, got {categories.Count}");

            string[] requiredKeys =
            {
                "food_market", "transport", "housing_rent", "utilities", "telecom_data",
                "healthcare", "education", "tontine", "family_solidarity", "events",
                "church_donations", "entertainment", "savings", "other",
                "salary", "business_hustle", "transfers_received"
            };

            foreach (var key in requiredKeys)
            {
                Assert.Contains(categories, c => c.Key == key);
            }

            // Default limits should be 0 (with suggestions available via CategoryCatalog)
            Assert.All(categories, c => Assert.Equal(0m, c.BudgetLimit));
            Assert.True(CategoryCatalog.GetSuggestedBudgetXaf("food_market") >= 25_000m);

            await db.PurgeDatabaseAndKeyAsync();
        }
        finally
        {
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task LegacyCategoriesMigration_ShouldAssignStableKeys_PreserveExistingExpenses_AndAddNewCameroonianCategories()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"sb_p5_mig_{Guid.NewGuid():N}.db3");
        var keyStore = new InMemorySecureKeyStore();
        var masterKey = await keyStore.CreateMasterKeyAsync();

        try
        {
            // 1. Simulate a v1.0 database with legacy English categories (no Key) and existing user expenses
            var connStr = new SQLiteConnectionString(dbPath, DatabaseConstants.Flags, storeDateTimeAsTicks: true, key: masterKey);
            var rawConn = new SQLiteAsyncConnection(connStr);
            await rawConn.CreateTableAsync<Category>();
            await rawConn.CreateTableAsync<Expense>();
            await rawConn.CreateTableAsync<AppSettings>();

            var legacyFood = new Category { Name = "Food & Dining", Icon = "utensils", ColorHex = "#F59E0B", BudgetLimit = 350m, IsDefault = true };
            var legacyTransport = new Category { Name = "Transportation", Icon = "car", ColorHex = "#3B82F6", BudgetLimit = 150m, IsDefault = true };
            var legacySalary = new Category { Name = "Salary / Income", Icon = "wallet", ColorHex = "#10B981", BudgetLimit = 0m, IsDefault = true, IsIncome = true };
            var customCat = new Category { Name = "Mon Projet Cacao", Icon = "tag", ColorHex = "#14B8A6", BudgetLimit = 50000m, IsDefault = false };

            await rawConn.InsertAsync(legacyFood);
            await rawConn.InsertAsync(legacyTransport);
            await rawConn.InsertAsync(legacySalary);
            await rawConn.InsertAsync(customCat);

            var existingExpense = new Expense
            {
                Description = "Poisson braisé Bonapriso",
                Amount = 4500m,
                CategoryId = legacyFood.Id,
                Date = DateTime.Today
            };
            await rawConn.InsertAsync(existingExpense);
            await rawConn.CloseAsync();

            // 2. Initialize DatabaseService -> triggers non-destructive category migration
            var db = new DatabaseService(dbPath, keyStore);
            await db.InitializeAsync();

            var migratedCategories = await db.Categories.GetAllAsync();
            var migratedFood = migratedCategories.FirstOrDefault(c => c.Id == legacyFood.Id);
            var migratedTransport = migratedCategories.FirstOrDefault(c => c.Id == legacyTransport.Id);
            var migratedSalary = migratedCategories.FirstOrDefault(c => c.Id == legacySalary.Id);
            var migratedCustom = migratedCategories.FirstOrDefault(c => c.Id == customCat.Id);

            Assert.NotNull(migratedFood);
            Assert.Equal("food_market", migratedFood!.Key);
            Assert.Equal("Alimentation / Marché", CategoryCatalog.GetDisplayName(migratedFood, "fr"));
            Assert.Equal("Food & Dining", CategoryCatalog.GetDisplayName(migratedFood, "en"));

            Assert.NotNull(migratedTransport);
            Assert.Equal("transport", migratedTransport!.Key);

            Assert.NotNull(migratedSalary);
            Assert.Equal("salary", migratedSalary!.Key);

            // Custom category preserved intact
            Assert.NotNull(migratedCustom);
            Assert.Equal("Mon Projet Cacao", migratedCustom!.Name);
            Assert.Equal(50000m, migratedCustom.BudgetLimit);

            // New Cameroonian categories (tontine, telecom_data, family_solidarity, etc.) added
            Assert.Contains(migratedCategories, c => c.Key == "tontine");
            Assert.Contains(migratedCategories, c => c.Key == "telecom_data");
            Assert.Contains(migratedCategories, c => c.Key == "family_solidarity");

            // Existing expense foreign key preserved 100%
            var expenses = await db.Expenses.GetAllAsync();
            Assert.Single(expenses);
            Assert.Equal(legacyFood.Id, expenses[0].CategoryId);
            Assert.Equal(4500m, expenses[0].Amount);

            await db.PurgeDatabaseAndKeyAsync();
        }
        finally
        {
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }
}
