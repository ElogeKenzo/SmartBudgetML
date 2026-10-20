using System.Globalization;
using System.Text;
using SmartBudgetML.Models;
using SmartBudgetML.Services.Data;
using SmartBudgetML.Services.Database;
using SmartBudgetML.Services.Formatting;
using SmartBudgetML.Services.ML;
using SmartBudgetML.Services.ML.Models;

namespace SmartBudgetML.Tests.Honesty;

public class Phase1HonestyTests : IDisposable
{
    private readonly string _tempDir;

    public Phase1HonestyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"smartbudget_p1_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup lock in test runner teardown
        }
    }

    // =========================================================================
    // 1.1 (A2) ModelEvaluatorTests — Real ML evaluation vs fake 95.8%
    // =========================================================================

    [Fact]
    public void ModelEvaluator_BeforeEvaluation_ReturnsUnevaluatedState()
    {
        var evaluator = new ModelEvaluator(seed: 42);
        var metrics = evaluator.GetCurrentMetrics();

        Assert.False(metrics.IsEvaluated);
        Assert.Equal("Non évalué", metrics.StatusDisplay);
        Assert.Null(metrics.EvaluatedAtUtc);
    }

    [Fact]
    public async Task ModelEvaluator_EvaluateAsync_ComputesDeterministicMetrics_AndVariesOnDegradedData()
    {
        var evaluator1 = new ModelEvaluator(seed: 42);
        var evaluator2 = new ModelEvaluator(seed: 42);

        var metrics1 = await evaluator1.EvaluateAsync();
        var metrics2 = await evaluator2.EvaluateAsync();

        Assert.True(metrics1.IsEvaluated);
        Assert.InRange(metrics1.MicroAccuracy, 0.0, 1.0);
        Assert.InRange(metrics1.MacroAccuracy, 0.0, 1.0);
        Assert.InRange(metrics1.MacroF1, 0.0, 1.0);
        Assert.True(metrics1.TrainingSampleCount > 0);
        Assert.True(metrics1.ValidationSampleCount > 0);
        Assert.NotEqual(95.8, Math.Round(metrics1.MicroAccuracy * 100.0, 1));

        // Deterministic with fixed seed
        Assert.Equal(metrics1.MicroAccuracy, metrics2.MicroAccuracy, precision: 6);
        Assert.Equal(metrics1.MacroF1, metrics2.MacroF1, precision: 6);

        // Degraded training data (scrambled labels) must produce lower accuracy on the same validation set
        var defaultData = SeedTrainingData.GetDefaultDataset();
        var degradedTraining = defaultData.Select((item, idx) => new ExpenseTransactionData
        {
            Description = item.Description,
            CategoryName = idx % 2 == 0 ? "Other" : "Investments"
        }).ToList();

        var degradedMetrics = await evaluator1.EvaluateAsync(trainingSamples: degradedTraining);
        Assert.NotEqual(metrics1.MicroAccuracy, degradedMetrics.MicroAccuracy);
        Assert.True(degradedMetrics.MicroAccuracy < metrics1.MicroAccuracy);
    }

    // =========================================================================
    // 1.2 (A3) MlConfidenceThreshold persistence & auto-categorization policy
    // =========================================================================

    [Fact]
    public async Task AppSettingsProvider_PersistsMlConfidenceThreshold_AndChangesAutoSelectionBehavior()
    {
        var dbPath = Path.Combine(_tempDir, "threshold_test.db3");
        var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        var provider = new AppSettingsProvider(db);
        var initial = await provider.GetOrCreateSettingsAsync();
        Assert.Equal(0.70f, initial.MlConfidenceThreshold, precision: 2);

        // Prediction with 0.75 confidence passes at 0.70 threshold
        Assert.True(await provider.ShouldAutoSelectCategoryAsync(0.75f));

        // Raise threshold to 0.85 and persist
        initial.MlConfidenceThreshold = 0.85f;
        await provider.SaveSettingsAsync(initial);

        // Re-instantiate provider on same DB to prove persistence
        var provider2 = new AppSettingsProvider(db);
        var reloaded = await provider2.GetOrCreateSettingsAsync();
        Assert.Equal(0.85f, reloaded.MlConfidenceThreshold, precision: 2);

        // Now 0.75 confidence must NOT auto-select
        Assert.False(await provider2.ShouldAutoSelectCategoryAsync(0.75f));
        Assert.True(await provider2.ShouldAutoSelectCategoryAsync(0.90f));

        // Disabling AutoCategorizeEnabled prevents auto-selection regardless of confidence
        reloaded.AutoCategorizeEnabled = false;
        await provider2.SaveSettingsAsync(reloaded);
        Assert.False(await provider2.ShouldAutoSelectCategoryAsync(0.99f));
    }

    // =========================================================================
    // 1.3 (A4) CsvExportTests — RFC 4180, UTF-8 BOM, accents, disk verification
    // =========================================================================

    [Fact]
    public async Task CsvExpenseExporter_HandlesRfc4180QuotesCommasNewlinesAccents_AndWritesUtf8BomFile()
    {
        var exportService = new LocalFileExportService(_tempDir);
        var exporter = new CsvExpenseExporter(exportService);

        var categories = new List<Category>
        {
            new() { Id = 1, Name = "Alimentation & Marché" },
            new() { Id = 2, Name = "Transport, Taxi & Moto" }
        };

        var expenses = new List<Expense>
        {
            new()
            {
                Id = 10,
                Date = new DateTime(2026, 3, 15),
                Description = "Achat \"plantain mûr\", huile, et épices\r\nMarché Mokolo",
                CategoryId = 1,
                Amount = 12500.50m,
                IsIncome = false,
                Notes = "Note avec accents: éèêçàù et virgule, guillemet \"test\""
            }
        };

        var filePath = await exporter.ExportToFileAsync(expenses, categories, "test_export.csv");

        Assert.True(File.Exists(filePath));
        var rawBytes = await File.ReadAllBytesAsync(filePath);
        Assert.True(rawBytes.Length > 3);
        // Verify UTF-8 BOM (0xEF, 0xBB, 0xBF)
        Assert.Equal(0xEF, rawBytes[0]);
        Assert.Equal(0xBB, rawBytes[1]);
        Assert.Equal(0xBF, rawBytes[2]);

        var content = Encoding.UTF8.GetString(rawBytes, 3, rawBytes.Length - 3);
        Assert.Contains("2026-03-15", content);
        Assert.Contains("\"Achat \"\"plantain mûr\"\", huile, et épices\r\nMarché Mokolo\"", content);
        Assert.Contains("\"Alimentation & Marché\"", content);
        Assert.Contains("\"Transport, Taxi & Moto\"", exporter.GenerateCsv(
            new[] { new Expense { Date = new DateTime(2026, 3, 16), CategoryId = 2, Amount = 500m, Description = "Moto" } },
            categories));
        Assert.Contains("12500.50", content);
        Assert.Contains("éèêçàù", content);
    }

    [Fact]
    public void CsvExpenseExporter_EmptyExpenses_ReturnsHeaderOnly()
    {
        var exportService = new LocalFileExportService(_tempDir);
        var exporter = new CsvExpenseExporter(exportService);

        var csv = exporter.GenerateCsv(Enumerable.Empty<Expense>(), Enumerable.Empty<Category>());
        var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Assert.Single(lines);
        Assert.StartsWith("Date,Description,Category,Amount,Type", lines[0]);
    }

    // =========================================================================
    // 1.4 (A5 & A6) DatabaseBackupTests & Real Database Size
    // =========================================================================

    [Fact]
    public async Task DatabaseService_BackupAndRestore_RestoresWipedData_AndRejectsCorruptFile()
    {
        var dbPath = Path.Combine(_tempDir, "main_budget.db3");
        var backupPath = Path.Combine(_tempDir, "backup_budget.db3");
        var corruptPath = Path.Combine(_tempDir, "corrupt_file.db3");

        var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        await db.Expenses.InsertAsync(new Expense
        {
            Amount = 45000m,
            Description = "Cotisation Tontine",
            CategoryId = 1,
            Date = new DateTime(2026, 3, 10)
        });

        var sizeBytes = await db.GetDatabaseSizeBytesAsync();
        var formattedSize = await db.GetFormattedDatabaseSizeAsync();
        Assert.True(sizeBytes > 0);
        Assert.NotEqual("240 KB", formattedSize);
        Assert.NotEqual("180 KB", formattedSize);

        // Perform real SQLite backup
        var savedBackupPath = await db.BackupDatabaseAsync(backupPath);
        Assert.True(File.Exists(savedBackupPath));

        // Wipe expenses in original DB
        var allExpenses = await db.Expenses.GetAllAsync();
        foreach (var exp in allExpenses)
        {
            await db.Expenses.DeleteAsync(exp);
        }
        Assert.Equal(0, await db.Expenses.CountAsync());

        // Attempt to restore a corrupt non-SQLite file -> must throw and keep DB intact
        await File.WriteAllTextAsync(corruptPath, "NOT A VALID SQLITE FILE HEADER");
        await Assert.ThrowsAsync<InvalidDataException>(() => db.RestoreDatabaseAsync(corruptPath));

        // Restore from valid backup -> expense is back
        await db.RestoreDatabaseAsync(backupPath);
        var restoredExpenses = await db.Expenses.GetAllAsync();
        Assert.Single(restoredExpenses);
        Assert.Equal("Cotisation Tontine", restoredExpenses[0].Description);
        Assert.Equal(45000m, restoredExpenses[0].Amount);
    }

    // =========================================================================
    // 1.5 (A7) Persistent User Feedback in SQLite
    // =========================================================================

    [Fact]
    public async Task ExpenseCategorizerService_PersistsFeedbackInSqlite_AcrossServiceInstances()
    {
        var dbPath = Path.Combine(_tempDir, "feedback_persist.db3");
        var db = new DatabaseService(dbPath);
        await db.InitializeAsync();

        const string novelPhrase = "Cotisation Njangi Quartier Bastos";
        const string targetCategory = "Investments";

        // Instance 1 records feedback
        var service1 = new ExpenseCategorizerService(db, seed: 42);
        await service1.EnsureTrainedAsync();
        for (int i = 0; i < 4; i++)
        {
            await service1.RetrainWithFeedbackAsync(novelPhrase, targetCategory);
        }

        var storedFeedback = await db.CategoryFeedbacks.GetAllAsync();
        Assert.Single(storedFeedback);
        Assert.Equal(4, storedFeedback[0].Count);
        Assert.Equal(targetCategory, storedFeedback[0].CategoryName);

        // Instance 2 created fresh on the same DB (simulating app restart)
        var service2 = new ExpenseCategorizerService(db, seed: 42);
        await service2.EnsureTrainedAsync();

        var prediction = service2.Predict(novelPhrase);
        Assert.Equal(targetCategory, prediction.PredictedCategory);
    }

    // =========================================================================
    // 1.6 (A13) CurrencyFormatterTests — Culture-invariant XAF/EUR/USD formatting
    // =========================================================================

    [Theory]
    [InlineData("fr-FR", "XAF", 12500, "12\u00A0500 FCFA")]
    [InlineData("fr-CM", "XAF", 12500, "12\u00A0500 FCFA")]
    [InlineData("en-US", "XAF", 12500, "12\u00A0500 FCFA")]
    [InlineData("fr-FR", "FCFA", 12500.6, "12\u00A0501 FCFA")]
    [InlineData("fr-FR", "USD", 1250.5, "$1,250.50")]
    [InlineData("en-US", "USD", 1250.5, "$1,250.50")]
    [InlineData("fr-FR", "EUR", 1250.5, "1\u00A0250,50 €")]
    [InlineData("en-US", "EUR", 1250.5, "1\u00A0250,50 €")]
    public void CurrencyFormatter_FormatsDeterministically_AcrossThreadCultures(
        string cultureName,
        string currencyCode,
        decimal amount,
        string expected)
    {
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        var originalUiCulture = Thread.CurrentThread.CurrentUICulture;
        try
        {
            var culture = new CultureInfo(cultureName);
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            var formatter = new CurrencyFormatter();
            var actual = formatter.Format(amount, currencyCode);

            Assert.Equal(expected, actual);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
            Thread.CurrentThread.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void BudgetForecastingService_UsesCurrencyFormatter_InsteadOfHostCultureC0()
    {
        var formatter = new CurrencyFormatter();
        var service = new BudgetForecastingService(formatter);

        var expenses = new List<Expense>
        {
            new() { Amount = 50000m, Date = new DateTime(2026, 3, 5), IsIncome = false },
            new() { Amount = 60000m, Date = new DateTime(2026, 3, 10), IsIncome = false }
        };

        var result = service.ForecastMonthEnd(
            expenses,
            monthlyBudget: 80000m,
            asOfDate: new DateTime(2026, 3, 10),
            currencyCode: "XAF");

        Assert.Equal(BudgetHealthStatus.Exceeded, result.Status);
        Assert.Contains("FCFA", result.StatusMessage);
    }
}
